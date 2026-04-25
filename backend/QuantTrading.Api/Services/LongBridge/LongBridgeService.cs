using System.Net.Http.Headers;
using System.Net;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using QuantTrading.Api.Data;
using QuantTrading.Api.Models;

namespace QuantTrading.Api.Services.LongBridge;

public class LongBridgeService : ILongBridgeService
{
    private static readonly string[] DefaultSearchMarkets = ["US", "HK", "SH", "SZ", "SG"];
    private static readonly TimeSpan SecurityListCacheDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan StockSnapshotCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CompanyProfileCacheDuration = TimeSpan.FromMinutes(30);
    private static readonly DateTime UnknownQuoteTimestamp = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
    private static readonly Regex StooqKlineRowRegex = new(
        "<tr><td[^>]*>\\d+</td><td[^>]*>([^<]+)</td><td>([^<]+)</td><td>([^<]+)</td><td>([^<]+)</td><td>([^<]+)</td><td[^>]*>[^<]*</td><td[^>]*>[^<]*</td><td>([^<]+)</td></tr>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex StooqSummaryRowRegex = new(
        "<tr[^>]*>\\s*<td[^>]*>(?<key>.*?)</td>\\s*<td[^>]*>(?<value>.*?)</td>\\s*</tr>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex NumberTokenRegex = new("[-+]?[0-9]+(?:[\\.,][0-9]+)*", RegexOptions.Compiled);
    private static readonly Regex PairNumberRegex = new("([-+]?[0-9]+(?:[\\.,][0-9]+)*)\\s*/\\s*([-+]?[0-9]+(?:[\\.,][0-9]+)*)", RegexOptions.Compiled);
    private static readonly Regex FrontmatterTitleRegex = new(@"^title:\s*""?(?<title>[^""]+)""?\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MarkdownHeadingRegex = new(@"^#\s+(?<title>.+)$", RegexOptions.Multiline | RegexOptions.Compiled);
    private const string EastMoneySuggestUrl = "https://searchapi.eastmoney.com/api/suggest/get";
    private const string EastMoneySuggestToken = "D43BF722C8E33BDC906FB84D85E326E8";
    private const string EastMoneyQuoteUrl = "https://push2.eastmoney.com/api/qt/stock/get";
    private static readonly string[] LongbridgeQuotePageLocales = ["zh-CN", "en"];

    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LongBridgeService> _logger;
    private readonly SemaphoreSlim _securityListCacheLock = new(1, 1);
    private readonly Dictionary<string, CachedSecurityList> _securityListCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CachedStockSnapshot> _stockSnapshotCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CachedCompanyProfile> _companyProfileCache = new(StringComparer.OrdinalIgnoreCase);

    public LongBridgeService(
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        ILogger<LongBridgeService> logger)
    {
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    private async Task<LongBridgeRuntimeConfig> GetRuntimeConfigAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<QuantTradingDbContext>();

        var configs = await dbContext.SystemConfigs
            .Where(c => c.Category == "longbridge" || c.Category == "proxy")
            .ToListAsync();

        var grouped = configs
            .GroupBy(c => c.Category, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(c => c.Key, c => c.Value, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

        var longBridgeConfig = grouped.GetValueOrDefault("longbridge") ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var proxyConfig = grouped.GetValueOrDefault("proxy") ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var fallbackProxyUrl = _configuration["Proxy:Url"];
        var host = proxyConfig.GetValueOrDefault("Host") ?? string.Empty;
        var port = ParseInt(proxyConfig.GetValueOrDefault("Port"));
        var username = proxyConfig.GetValueOrDefault("Username") ?? string.Empty;
        var password = proxyConfig.GetValueOrDefault("Password") ?? string.Empty;

        PopulateProxyPartsFromUrl(proxyConfig.GetValueOrDefault("Url") ?? fallbackProxyUrl, ref host, ref port, ref username, ref password);

        return new LongBridgeRuntimeConfig
        {
            BaseUrl = NormalizeBaseUrl(
                longBridgeConfig.GetValueOrDefault("BaseUrl")
                ?? _configuration["LongBridge:BaseUrl"]
                ?? "https://openapi.longbridge.com"),
            AppKey = longBridgeConfig.GetValueOrDefault("AppKey")
                ?? _configuration["LongBridge:AppKey"]
                ?? string.Empty,
            AppSecret = longBridgeConfig.GetValueOrDefault("AppSecret")
                ?? _configuration["LongBridge:AppSecret"]
                ?? string.Empty,
            AccessToken = longBridgeConfig.GetValueOrDefault("AccessToken")
                ?? _configuration["LongBridge:AccessToken"]
                ?? string.Empty,
            ProxyEnabled = ParseBool(proxyConfig.GetValueOrDefault("Enabled"), ParseBool(_configuration["Proxy:Enabled"])),
            ProxyHost = host,
            ProxyPort = port,
            ProxyUsername = username,
            ProxyPassword = password
        };
    }

    private static string NormalizeCredential(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private static string NormalizeBaseUrl(string? value)
    {
        var normalized = NormalizeCredential(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "https://openapi.longbridge.com";
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return "https://openapi.longbridge.com";
        }

        var host = uri.Host.ToLowerInvariant();
        if (host == "open.longbridge.com")
        {
            return "https://openapi.longbridge.com";
        }

        if (host == "open.longbridge.cn")
        {
            return "https://openapi.longbridge.cn";
        }

        if (host == "openapi.longbridge.com" || host == "openapi.longbridge.cn")
        {
            return $"{uri.Scheme}://{uri.Host}";
        }

        return $"{uri.Scheme}://{uri.Host}";
    }

    private static bool ParseBool(string? value, bool fallback = false)
    {
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static int ParseInt(string? value, int fallback = 0)
    {
        return int.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static void PopulateProxyPartsFromUrl(string? proxyUrl, ref string host, ref int port, ref string username, ref string password)
    {
        if (string.IsNullOrWhiteSpace(proxyUrl) || !Uri.TryCreate(proxyUrl, UriKind.Absolute, out var uri))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            host = uri.Host;
        }

        if (port == 0)
        {
            port = uri.IsDefaultPort
                ? uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? 443 : 80
                : uri.Port;
        }

        if (!string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            var userInfo = uri.UserInfo.Split(':', 2);
            if (string.IsNullOrWhiteSpace(username) && userInfo.Length >= 1)
            {
                username = Uri.UnescapeDataString(userInfo[0]);
            }

            if (string.IsNullOrWhiteSpace(password) && userInfo.Length == 2)
            {
                password = Uri.UnescapeDataString(userInfo[1]);
            }
        }
    }

    private static HttpClient CreateHttpClient(LongBridgeRuntimeConfig config)
    {
        var handler = new HttpClientHandler();
        if (config.ProxyEnabled && !string.IsNullOrWhiteSpace(config.ProxyHost) && config.ProxyPort > 0)
        {
            var proxyUri = BuildProxyUri(config.ProxyHost, config.ProxyPort, config.ProxyUsername, config.ProxyPassword);
            handler.Proxy = new WebProxy(proxyUri);
            handler.UseProxy = true;
        }

        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(config.BaseUrl),
            Timeout = TimeSpan.FromSeconds(8)
        };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9,en;q=0.8");
        return client;
    }

    private static Uri BuildProxyUri(string host, int port, string? username, string? password)
    {
        var builder = new UriBuilder("http", host, port);
        if (!string.IsNullOrWhiteSpace(username))
        {
            builder.UserName = Uri.EscapeDataString(username);
            if (!string.IsNullOrWhiteSpace(password))
            {
                builder.Password = Uri.EscapeDataString(password);
            }
        }

        return builder.Uri;
    }

    private async Task<string?> SendRequestAsync(string path, HttpMethod method, object? body = null)
    {
        var result = await SendRequestWithResultAsync(path, method, body);
        return result.Content;
    }

    private async Task<string?> SendRequestWithFallbackPathsAsync(
        IEnumerable<string> paths,
        HttpMethod method,
        object? body = null)
    {
        var candidates = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        string? firstError = null;
        foreach (var path in candidates)
        {
            var result = await SendRequestWithResultAsync(path, method, body);
            if (result.Success && !string.IsNullOrWhiteSpace(result.Content))
            {
                return result.Content;
            }

            if (firstError == null && !string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                firstError = result.ErrorMessage;
            }
        }

        if (!string.IsNullOrWhiteSpace(firstError))
        {
            _logger.LogWarning("LongBridge request failed after trying {Count} paths. First error: {Error}", candidates.Count, firstError);
        }

        return null;
    }

    private async Task<LongBridgeApiCallResult> SendRequestWithResultAsync(string path, HttpMethod method, object? body = null)
    {
        try
        {
            var runtimeConfig = await GetRuntimeConfigAsync();
            runtimeConfig = runtimeConfig with
            {
                BaseUrl = NormalizeCredential(runtimeConfig.BaseUrl),
                AppKey = NormalizeCredential(runtimeConfig.AppKey),
                AppSecret = NormalizeCredential(runtimeConfig.AppSecret),
                AccessToken = NormalizeCredential(runtimeConfig.AccessToken),
                ProxyHost = NormalizeCredential(runtimeConfig.ProxyHost),
                ProxyUsername = NormalizeCredential(runtimeConfig.ProxyUsername),
                ProxyPassword = NormalizeCredential(runtimeConfig.ProxyPassword)
            };

            if (string.IsNullOrWhiteSpace(runtimeConfig.AppKey) ||
                string.IsNullOrWhiteSpace(runtimeConfig.AppSecret) ||
                string.IsNullOrWhiteSpace(runtimeConfig.AccessToken))
            {
                const string missingCredentials = "LongBridge 凭据未配置完整，请先保存 App Key、App Secret 和 Access Token。";
                _logger.LogWarning("LongBridge credentials are not configured");
                return new LongBridgeApiCallResult(false, null, missingCredentials);
            }

            try
            {
                return await ExecuteRequestAsync(runtimeConfig, path, method, body);
            }
            catch (HttpRequestException ex) when (TryGetChinaBaseUrl(runtimeConfig.BaseUrl, out var chinaBaseUrl))
            {
                _logger.LogWarning(ex, "LongBridge request failed via {BaseUrl}, retrying with {FallbackBaseUrl}", runtimeConfig.BaseUrl, chinaBaseUrl);
                var fallbackRuntime = runtimeConfig with { BaseUrl = chinaBaseUrl };
                return await ExecuteRequestAsync(fallbackRuntime, path, method, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling LongBridge API: {Path}", path);
            return new LongBridgeApiCallResult(false, null, $"调用 LongBridge 接口时发生异常：{ex.Message}");
        }
    }

    private static bool TryGetChinaBaseUrl(string? baseUrl, out string fallbackBaseUrl)
    {
        fallbackBaseUrl = string.Empty;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Host.Equals("openapi.longbridge.com", StringComparison.OrdinalIgnoreCase))
        {
            fallbackBaseUrl = $"{uri.Scheme}://openapi.longbridge.cn";
            return true;
        }

        if (uri.Host.Equals("open.longbridge.com", StringComparison.OrdinalIgnoreCase))
        {
            fallbackBaseUrl = $"{uri.Scheme}://openapi.longbridge.cn";
            return true;
        }

        return false;
    }

    private async Task<LongBridgeApiCallResult> ExecuteRequestAsync(
        LongBridgeRuntimeConfig runtimeConfig,
        string path,
        HttpMethod method,
        object? body = null)
    {
        using var client = CreateHttpClient(runtimeConfig);
        Uri requestUri;
        if (Uri.TryCreate(path, UriKind.Absolute, out var absoluteUri) &&
            (absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps))
        {
            requestUri = absoluteUri;
        }
        else
        {
            requestUri = new Uri(client.BaseAddress!, path);
        }

        var request = new HttpRequestMessage(method, requestUri);
        string bodyStr = body != null ? JsonConvert.SerializeObject(body) : "";
        if (body != null)
        {
            request.Content = new StringContent(bodyStr, Encoding.UTF8, "application/json");
        }

        request.Headers.Add("x-api-key", runtimeConfig.AppKey);
        request.Headers.TryAddWithoutValidation("authorization", BuildAuthorizationHeaderValue(runtimeConfig.AccessToken, runtimeConfig.AppSecret));
        ApplySignature(request, bodyStr, runtimeConfig.AppSecret);

        var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            return new LongBridgeApiCallResult(true, content, null);
        }

        var upstreamMessage = ExtractErrorMessage(content);
        if (IsExpectedQuoteProbeNotFound(response.StatusCode, path, upstreamMessage))
        {
            _logger.LogDebug(
                "LongBridge quote REST probe not available: {StatusCode} path={Path}",
                response.StatusCode,
                path);
            return new LongBridgeApiCallResult(false, null, null);
        }

        _logger.LogError(
            "LongBridge API error: {StatusCode} path={Path} uri={Uri} content={Content}",
            response.StatusCode,
            path,
            requestUri,
            content);
        return new LongBridgeApiCallResult(false, null, upstreamMessage);
    }

    private static bool IsExpectedQuoteProbeNotFound(HttpStatusCode statusCode, string path, string? message)
    {
        if (statusCode != HttpStatusCode.NotFound)
        {
            return false;
        }

        if (!string.Equals(message, "api not found", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var normalizedPath = path.TrimStart('/');
        return normalizedPath.StartsWith("v1/quote/realtime?", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith("v1/quote?", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith("quote/realtime?", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith("quote?", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildAuthorizationHeaderValue(string accessToken, string appSecret)
    {
        if (accessToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return accessToken;
        }

        if (string.IsNullOrWhiteSpace(appSecret))
        {
            return $"Bearer {accessToken}";
        }

        return accessToken;
    }

    private static void ApplySignature(HttpRequestMessage request, string body, string appSecret)
    {
        var authorization = GetSingleHeaderValue(request, "authorization");
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        const string signedHeaders = "authorization;x-api-key;x-timestamp";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        request.Headers.Remove("x-timestamp");
        request.Headers.Remove("x-api-signature");
        request.Headers.Add("x-timestamp", timestamp);

        var requestUri = request.RequestUri ?? throw new InvalidOperationException("LongBridge request URI is missing");
        var query = requestUri.Query.StartsWith('?') ? requestUri.Query[1..] : requestUri.Query;
        var headersText =
            $"authorization:{authorization}\n" +
            $"x-api-key:{GetSingleHeaderValue(request, "x-api-key")}\n" +
            $"x-timestamp:{timestamp}\n";

        var plainText =
            $"{request.Method.Method.ToUpperInvariant()}|" +
            $"{requestUri.AbsolutePath}|" +
            $"{query}|" +
            $"{headersText}|" +
            $"{signedHeaders}|";

        if (!string.IsNullOrEmpty(body))
        {
            plainText += ComputeSha1Hex(body);
        }

        var textToSign = $"HMAC-SHA256|{ComputeSha1Hex(plainText)}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
        var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(textToSign))).ToLowerInvariant();

        request.Headers.TryAddWithoutValidation(
            "x-api-signature",
            $"HMAC-SHA256 SignedHeaders={signedHeaders}, Signature={signature}");
    }

    private static string GetSingleHeaderValue(HttpRequestMessage request, string headerName)
    {
        return request.Headers.TryGetValues(headerName, out var values)
            ? values.FirstOrDefault()?.Trim() ?? string.Empty
            : string.Empty;
    }

    private static string ExtractErrorMessage(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "LongBridge 返回了空响应。";
        }

        try
        {
            var json = JObject.Parse(content);
            var message = json["message"]?.ToString();
            if (!string.IsNullOrWhiteSpace(message))
            {
                return message;
            }
        }
        catch
        {
            // Ignore parse errors and fall back to raw content.
        }

        return content;
    }

    private static string ComputeSha1Hex(string content)
    {
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
    }

    public async Task<LongBridgeConnectionTestResult> TestConnectionAsync()
    {
        var result = await SendRequestWithResultAsync("/v1/asset/account", HttpMethod.Get);
        if (result.Success)
        {
            return new LongBridgeConnectionTestResult
            {
                Success = true,
                Message = "LongBridge 连接成功"
            };
        }

        // LongBridge 主链路在部分网络环境下可能不可达，保持回退行情可用并给出明确提示。
        var fallbackKline = await GetKlineFromNasdaqAsync("AAPL.US", "D", 5);
        if (fallbackKline.Count > 0)
        {
            return new LongBridgeConnectionTestResult
            {
                Success = true,
                Message = "LongBridge 主接口当前不可达，系统已自动切换到公共行情回退源（Nasdaq）。"
            };
        }

        return new LongBridgeConnectionTestResult
        {
            Success = false,
            Message = result.ErrorMessage ?? "LongBridge 连接失败"
        };
    }

    public async Task<StockQuote?> GetQuoteAsync(string symbol)
    {
        var normalizedSymbol = NormalizeSymbol(symbol);
        if (string.IsNullOrWhiteSpace(normalizedSymbol))
        {
            return null;
        }

        if (IsAStockSymbol(normalizedSymbol))
        {
            var eastMoneyQuote = await GetAStockQuoteSnapshotAsync(normalizedSymbol);
            if (eastMoneyQuote != null)
            {
                return ConvertEastMoneyQuoteToStockQuote(normalizedSymbol, eastMoneyQuote);
            }
        }

        var quotes = await GetQuotesInternalAsync(new List<string> { normalizedSymbol }, allowFallback: true);
        var quote = quotes.FirstOrDefault();
        if (quote == null)
        {
            return null;
        }

        if (!IsAStockSymbol(normalizedSymbol) && (quote.PreviousClose <= 0 || quote.Turnover <= 0))
        {
            var snapshot = await GetStockSnapshotFromStooqAsync(normalizedSymbol);
            if (quote.PreviousClose <= 0 && snapshot.PreviousClose > 0)
            {
                quote.PreviousClose = snapshot.PreviousClose;
            }

            if (quote.Turnover <= 0 && snapshot.Turnover > 0)
            {
                quote.Turnover = snapshot.Turnover;
            }
        }

        if (quote.PreviousClose > 0)
        {
            quote.Change = quote.Price - quote.PreviousClose;
            quote.ChangePercent = quote.PreviousClose > 0
                ? quote.Change / quote.PreviousClose * 100
                : 0;
        }

        return quote;
    }

    public async Task<StockQuote?> GetQuoteStrictAsync(string symbol)
    {
        var normalizedSymbol = NormalizeSymbol(symbol);
        if (string.IsNullOrWhiteSpace(normalizedSymbol))
        {
            return null;
        }

        if (IsAStockSymbol(normalizedSymbol))
        {
            var pageQuote = await GetQuoteFromLongbridgePageAsync(normalizedSymbol);
            if (pageQuote != null && pageQuote.Price > 0 && pageQuote.Timestamp > DateTime.UnixEpoch)
            {
                return pageQuote;
            }

            return null;
        }

        var quotes = await GetQuotesInternalAsync(new List<string> { normalizedSymbol }, allowFallback: false);
        var quote = quotes.FirstOrDefault();
        if (quote == null || quote.Price <= 0 || quote.Timestamp <= DateTime.UnixEpoch)
        {
            quote = await GetQuoteFromLongbridgePageAsync(normalizedSymbol);
            if (quote == null || quote.Price <= 0 || quote.Timestamp <= DateTime.UnixEpoch)
            {
                return null;
            }
        }

        if (quote.PreviousClose > 0 && quote.Change == 0)
        {
            quote.Change = quote.Price - quote.PreviousClose;
        }

        if (quote.PreviousClose > 0 && quote.ChangePercent == 0)
        {
            quote.ChangePercent = quote.Change / quote.PreviousClose * 100;
        }

        return quote;
    }

    public async Task<List<StockQuote>> GetQuotesAsync(List<string> symbols)
    {
        return await GetQuotesInternalAsync(symbols, allowFallback: true);
    }

    private async Task<List<StockQuote>> GetQuotesInternalAsync(List<string> symbols, bool allowFallback)
    {
        var result = new List<StockQuote>();

        var normalizedSymbols = symbols
            .Select(NormalizeSymbol)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!normalizedSymbols.Any())
        {
            return result;
        }

        // LongBridge quote/realtime expects repeated symbol parameters, e.g.:
        // /v1/quote/realtime?symbol=AAPL.US&symbol=MSFT.US
        var symbolsQuery = string.Join("&", normalizedSymbols
            .Select(symbol => $"symbol={Uri.EscapeDataString(symbol)}"));
        var response = await SendRequestWithFallbackPathsAsync(
            [
                $"/v1/quote/realtime?{symbolsQuery}",
                $"/v1/quote?{symbolsQuery}",
                $"/quote/realtime?{symbolsQuery}",
                $"/quote?{symbolsQuery}"
            ],
            HttpMethod.Get);

        if (string.IsNullOrEmpty(response))
        {
            response = string.Empty;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(response))
            {
                var json = JObject.Parse(response);
                var code = json["code"]?.Value<int?>();
                if (code.HasValue && code.Value != 0)
                {
                    var message = json["message"]?.ToString() ?? json["msg"]?.ToString() ?? "unknown";
                    _logger.LogWarning("LongBridge quote business error: code={Code}, message={Message}", code.Value, message);
                }

                var data = json["data"]?["secu_quote"] as JArray
                    ?? json["data"]?["quote"] as JArray
                    ?? json["data"]?["list"] as JArray
                    ?? json["data"] as JArray
                    ?? json["secu_quote"] as JArray
                    ?? json["quote"] as JArray
                    ?? json["list"] as JArray;

                if (data != null)
                {
                    foreach (var item in data)
                    {
                        var parsedSymbol = NormalizeSymbol(item["symbol"]?.ToString() ?? string.Empty);
                        if (string.IsNullOrWhiteSpace(parsedSymbol))
                        {
                            continue;
                        }

                        var price = ParseDecimalToken(item["last_done"]);
                        if (price <= 0)
                        {
                            price = ParseDecimalToken(item["last"]);
                        }

                        var previousClose = ParseDecimalToken(item["prev_close"]);
                        if (previousClose <= 0)
                        {
                            previousClose = ParseDecimalToken(item["pre_close"]);
                        }
                        if (previousClose <= 0)
                        {
                            previousClose = ParseDecimalToken(item["last_close"]);
                        }

                        var change = ParseDecimalToken(item["change_value"]);
                        if (change == 0)
                        {
                            change = ParseDecimalToken(item["change"]);
                        }
                        if (change == 0 && price > 0 && previousClose > 0)
                        {
                            change = price - previousClose;
                        }

                        var changePercent = ParseDecimalToken(item["change_rate"]);
                        if (changePercent == 0 && price > 0 && previousClose > 0)
                        {
                            changePercent = (price - previousClose) / previousClose * 100;
                        }

                        result.Add(new StockQuote
                        {
                            Symbol = parsedSymbol,
                            Price = price,
                            PreviousClose = previousClose,
                            Open = ParseDecimalToken(item["open"]),
                            High = ParseDecimalToken(item["high"]),
                            Low = ParseDecimalToken(item["low"]),
                            Volume = ParseLongToken(item["volume"]),
                            Turnover = ParseDecimalToken(item["turnover"]),
                            Change = change,
                            ChangePercent = changePercent,
                            Timestamp = ParseQuoteTimestamp(item["timestamp"])
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing quote response");
        }

        if (allowFallback && result.Count > 0)
        {
            foreach (var quote in result)
            {
                if (!IsAStockSymbol(quote.Symbol))
                {
                    continue;
                }

                var shouldSupplement = quote.Open <= 0
                    || quote.High <= 0
                    || quote.Low <= 0
                    || quote.Volume <= 0
                    || quote.Turnover <= 0
                    || quote.Timestamp <= UnknownQuoteTimestamp;

                if (!shouldSupplement)
                {
                    continue;
                }

                var eastMoneyQuote = await GetAStockQuoteSnapshotAsync(quote.Symbol);
                if (eastMoneyQuote == null)
                {
                    continue;
                }

                MergeQuoteFromEastMoney(quote, eastMoneyQuote);
            }
        }

        var missingSymbols = normalizedSymbols
            .Where(s => !result.Any(q => SymbolEquals(q.Symbol, s)))
            .ToList();

        foreach (var missing in missingSymbols)
        {
            var pageQuote = await GetQuoteFromLongbridgePageAsync(missing);
            if (pageQuote != null)
            {
                if (allowFallback && IsAStockSymbol(missing))
                {
                    var eastMoneyQuote = await GetAStockQuoteSnapshotAsync(missing);
                    if (eastMoneyQuote != null)
                    {
                        MergeQuoteFromEastMoney(pageQuote, eastMoneyQuote);
                    }
                }

                result.Add(pageQuote);
                continue;
            }

            if (allowFallback)
            {
                var eastMoneyQuote = await GetAStockQuoteSnapshotAsync(missing);
                if (eastMoneyQuote != null)
                {
                    result.Add(ConvertEastMoneyQuoteToStockQuote(missing, eastMoneyQuote));
                    continue;
                }

                var fallbackQuote = await GetQuoteFromPublicFallbacksAsync(missing);
                if (fallbackQuote != null)
                {
                    result.Add(fallbackQuote);
                }
            }
        }

        return result;
    }

    public async Task<List<StockKline>> GetKlineAsync(string symbol, string period, DateTime? start = null, DateTime? end = null, int count = 100)
    {
        var result = new List<StockKline>();
        var normalizedSymbol = NormalizeSymbol(symbol);
        if (string.IsNullOrWhiteSpace(normalizedSymbol))
        {
            return result;
        }

        var periodMap = new Dictionary<string, string>
        {
            { "1", "1" }, { "5", "5" }, { "15", "15" }, { "60", "60" },
            { "D", "1D" }, { "W", "1W" }, { "M", "1M" }, { "Y", "1M" },
            { "1m", "1" }, { "5m", "5" }, { "15m", "15" }, { "30m", "30" },
            { "1h", "60" }, { "1d", "1D" }, { "1w", "1W" }, { "1M", "1M" }, { "1y", "1M" }
        };

        var lbPeriod = periodMap.GetValueOrDefault(period, "1D");
        var url = $"/v1/quote/candlesticks?symbol={normalizedSymbol}&period={lbPeriod}&count={count}";

        var response = await SendRequestAsync(url, HttpMethod.Get);

        try
        {
            if (!string.IsNullOrWhiteSpace(response))
            {
                var json = JObject.Parse(response);
                var data = json["data"]?["candlesticks"] as JArray;

                if (data != null)
                {
                    foreach (var item in data)
                    {
                        result.Add(new StockKline
                        {
                            Symbol = normalizedSymbol,
                            Open = item["open"]?.Value<decimal>() ?? 0,
                            High = item["high"]?.Value<decimal>() ?? 0,
                            Low = item["low"]?.Value<decimal>() ?? 0,
                            Close = item["close"]?.Value<decimal>() ?? 0,
                            Volume = item["volume"]?.Value<long>() ?? 0,
                            Turnover = item["turnover"]?.Value<decimal>() ?? 0,
                            Timestamp = DateTimeOffset.FromUnixTimeSeconds(item["timestamp"]?.Value<long>() ?? 0).UtcDateTime,
                            Period = period
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing kline response");
        }

        var hasRangeFilter = start.HasValue || end.HasValue;
        if (result.Any())
        {
            var ranged = ApplyKlineRange(result, start, end, count);
            if (ranged.Any() || !hasRangeFilter)
            {
                return ranged;
            }
        }

        var fallbackCount = hasRangeFilter ? Math.Max(count, 2500) : count;

        var fallback = await GetKlineFromYahooAsync(normalizedSymbol, period, fallbackCount);
        if (fallback.Any())
        {
            var ranged = ApplyKlineRange(fallback, start, end, count);
            if (ranged.Any() || !hasRangeFilter)
            {
                return ranged;
            }
        }

        fallback = await GetKlineFromNasdaqAsync(normalizedSymbol, period, fallbackCount, start, end);
        if (fallback.Any())
        {
            var ranged = ApplyKlineRange(fallback, start, end, count);
            if (ranged.Any() || !hasRangeFilter)
            {
                return ranged;
            }
        }

        fallback = await GetKlineFromStooqAsync(normalizedSymbol, period, fallbackCount);
        return ApplyKlineRange(fallback, start, end, count);
    }

    public async Task<Stock?> GetStockInfoAsync(string symbol)
    {
        var normalizedSymbol = NormalizeSymbol(symbol);
        if (string.IsNullOrWhiteSpace(normalizedSymbol))
        {
            return null;
        }

        var staticInfo = IsAStockSymbol(normalizedSymbol)
            ? null
            : await GetStaticSecurityInfoAsync(normalizedSymbol);
        var candidates = await SearchStocksAsync(normalizedSymbol);
        var stockMeta = candidates.FirstOrDefault(s => s.Symbol.Equals(normalizedSymbol, StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault(s => s.Symbol.StartsWith($"{normalizedSymbol}.", StringComparison.OrdinalIgnoreCase));

        if (stockMeta == null)
        {
            // get_security_list 在部分账号/市场可能不完整，允许直接按代码继续走行情链路。
            _logger.LogWarning(
                "Stock not found in LongBridge security list, fallback to direct symbol lookup: {Symbol}",
                normalizedSymbol);
            stockMeta = new Stock
            {
                Symbol = normalizedSymbol,
                Name = staticInfo?.Name ?? normalizedSymbol.Split('.').FirstOrDefault() ?? normalizedSymbol,
                Market = staticInfo?.Market ?? InferMarketFromSymbol(normalizedSymbol)
            };
        }

        var resolvedSymbol = NormalizeSymbol(stockMeta.Symbol);
        if (staticInfo == null && !IsAStockSymbol(resolvedSymbol))
        {
            staticInfo = await GetStaticSecurityInfoAsync(resolvedSymbol);
        }
        var resolvedMarket = string.IsNullOrWhiteSpace(staticInfo?.Market)
            ? (string.IsNullOrWhiteSpace(stockMeta.Market) ? InferMarketFromSymbol(resolvedSymbol) : stockMeta.Market)
            : staticInfo!.Market;
        var resolvedCurrency = ResolveCurrencyCode(staticInfo?.Currency, resolvedMarket);
        var resolvedName = await ResolveDisplayNameAsync(resolvedSymbol, stockMeta.Name, staticInfo);

        var quote = await GetQuoteAsync(resolvedSymbol);
        var snapshot = IsAStockSymbol(resolvedSymbol)
            ? new StockSnapshot()
            : await GetStockSnapshotFromStooqAsync(resolvedSymbol);
        var eastMoneyQuote = await GetAStockQuoteSnapshotAsync(resolvedSymbol);
        if (eastMoneyQuote != null)
        {
            quote = MergeQuoteWithAStockFallback(quote, resolvedSymbol, eastMoneyQuote);
            if (snapshot.MarketCap <= 0 && eastMoneyQuote.MarketCap > 0)
            {
                snapshot.MarketCap = eastMoneyQuote.MarketCap;
            }

            if (snapshot.Pe <= 0 && eastMoneyQuote.Pe > 0)
            {
                snapshot.Pe = eastMoneyQuote.Pe;
            }

            if (IsPoorDisplayName(resolvedName, resolvedSymbol) && !string.IsNullOrWhiteSpace(eastMoneyQuote.Name))
            {
                resolvedName = eastMoneyQuote.Name;
            }
        }

        if (IsAStockSymbol(resolvedSymbol)
            && (snapshot.MarketCap <= 0
                || snapshot.Pe <= 0
                || IsPoorDisplayName(resolvedName, resolvedSymbol)))
        {
            var markdownProfile = await TryFetchCompanyMarkdownAsync(resolvedSymbol);
            if (markdownProfile != null)
            {
                ApplySnapshotFieldsFromMarkdown(snapshot, markdownProfile.Fields);
                if (IsPoorDisplayName(resolvedName, resolvedSymbol)
                    && !IsPoorDisplayName(markdownProfile.Title, resolvedSymbol))
                {
                    resolvedName = markdownProfile.Title;
                }
            }
        }

        var orderedKlines = new List<StockKline>();
        var requiresKlineBackfill = (snapshot.High52Week <= 0 || snapshot.Low52Week <= 0 || snapshot.AvgVolume <= 0)
            && !IsAStockSymbol(resolvedSymbol);
        if (requiresKlineBackfill)
        {
            var dailyKlines = await GetKlineAsync(resolvedSymbol, "D", count: 160);
            orderedKlines = dailyKlines
                .Where(k => k.Timestamp != default)
                .OrderBy(k => k.Timestamp)
                .ToList();
        }

        var latestKline = orderedKlines.LastOrDefault();
        var previousClose = quote?.PreviousClose ?? 0;
        if (previousClose <= 0 && snapshot.PreviousClose > 0)
        {
            previousClose = snapshot.PreviousClose;
        }

        if (previousClose <= 0 && orderedKlines.Count >= 2)
        {
            previousClose = orderedKlines[^2].Close;
        }

        var currentPrice = quote?.Price ?? latestKline?.Close ?? 0;
        var open = quote?.Open > 0 ? quote.Open : (latestKline?.Open ?? 0);
        var high = quote?.High > 0 ? quote.High : (latestKline?.High ?? 0);
        var low = quote?.Low > 0 ? quote.Low : (latestKline?.Low ?? 0);
        var volume = quote?.Volume > 0 ? quote.Volume : (latestKline?.Volume ?? 0);

        decimal change = 0;
        decimal changePercent = 0;
        if (previousClose > 0 && currentPrice > 0)
        {
            change = currentPrice - previousClose;
            changePercent = change / previousClose * 100;
        }

        var high52Week = snapshot.High52Week;
        var low52Week = snapshot.Low52Week;
        if ((high52Week <= 0 || low52Week <= 0) && orderedKlines.Count > 0)
        {
            high52Week = high52Week > 0 ? high52Week : orderedKlines.Max(k => k.High);
            low52Week = low52Week > 0 ? low52Week : orderedKlines.Min(k => k.Low);
        }

        var avgVolume = snapshot.AvgVolume;
        if (avgVolume <= 0 && orderedKlines.Count > 0)
        {
            var recentVolumes = orderedKlines.TakeLast(60).Select(k => (decimal)k.Volume).ToList();
            if (recentVolumes.Count > 0)
            {
                avgVolume = (long)Math.Round(recentVolumes.Average());
            }
        }

        var marketCap = snapshot.MarketCap;
        if (marketCap <= 0 && currentPrice > 0 && (staticInfo?.TotalShares ?? 0) > 0)
        {
            marketCap = currentPrice * staticInfo!.TotalShares;
        }

        var eps = snapshot.Eps;
        if (eps <= 0)
        {
            eps = staticInfo?.EpsTtm > 0
                ? staticInfo.EpsTtm
                : (staticInfo?.Eps > 0 ? staticInfo.Eps : 0);
        }

        var pe = snapshot.Pe;
        if (pe <= 0 && eps > 0 && currentPrice > 0)
        {
            pe = currentPrice / eps;
        }

        var dividendYield = snapshot.DividendYield > 0
            ? snapshot.DividendYield
            : (staticInfo?.DividendYield ?? 0);

        var updatedAt = quote?.Timestamp is { } quoteTimestamp && quoteTimestamp > UnknownQuoteTimestamp
            ? quoteTimestamp
            : (latestKline?.Timestamp ?? DateTime.UtcNow);

        var hasValidMarketData = currentPrice > 0
            || previousClose > 0
            || open > 0
            || high > 0
            || low > 0
            || volume > 0
            || high52Week > 0
            || low52Week > 0
            || avgVolume > 0
            || marketCap > 0;

        if (!hasValidMarketData)
        {
            _logger.LogWarning("No valid market data found for symbol {Symbol}", resolvedSymbol);
            return null;
        }

        return new Stock
        {
            Symbol = resolvedSymbol,
            Name = resolvedName,
            Market = resolvedMarket,
            Currency = resolvedCurrency,
            CurrentPrice = currentPrice,
            PreviousClose = previousClose,
            Open = open,
            High = high,
            Low = low,
            Volume = volume,
            Change = change,
            ChangePercent = changePercent,
            MarketCap = marketCap,
            High52Week = high52Week,
            Low52Week = low52Week,
            AvgVolume = avgVolume,
            Pe = pe,
            Eps = eps,
            Dividend = dividendYield,
            LastUpdated = updatedAt
        };
    }

    public async Task<List<Stock>> SearchStocksAsync(string keyword)
    {
        var normalizedKeyword = (keyword ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedKeyword))
        {
            return new List<Stock>();
        }

        var results = new List<Stock>();

        StaticSecurityInfo? staticInfo = null;
        if (LooksLikeSymbolKeyword(normalizedKeyword))
        {
            var inferredSymbol = NormalizeSymbol(normalizedKeyword);
            if (!IsAStockSymbol(inferredSymbol))
            {
                staticInfo = await GetStaticSecurityInfoAsync(inferredSymbol);
            }
        }
        if (staticInfo != null)
        {
            results.Add(new Stock
            {
                Symbol = staticInfo.Symbol,
                Name = staticInfo.Name,
                Market = staticInfo.Market,
                Currency = ResolveCurrencyCode(staticInfo.Currency, staticInfo.Market)
            });
        }

        var cnFallback = await SearchAStockByEastMoneyAsync(normalizedKeyword);
        if (cnFallback.Count > 0)
        {
            results.AddRange(cnFallback);
        }

        var candidateMarkets = GetCandidateMarkets(normalizedKeyword)
            .Select(NormalizeMarketCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (candidateMarkets.Contains("US"))
        {
            // LongBridge 文档中 get_security_list 仅支持美股夜盘，避免对 SH/SZ/HK 调用导致 param_error。
            var usSecurities = await GetMarketSecuritiesAsync("US");
            if (usSecurities.Count > 0)
            {
                results.AddRange(usSecurities.Where(item => IsKeywordMatch(item, normalizedKeyword)));
            }
        }

        results = results
            .Where(item => !string.IsNullOrWhiteSpace(item.Symbol))
            .GroupBy(item => item.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => GetMatchScore(item, normalizedKeyword))
            .ThenBy(item => item.Symbol)
            .Take(20)
            .ToList();

        if (results.Count > 0)
        {
            return results;
        }

        if (staticInfo != null)
        {
            return
            [
                new Stock
                {
                    Symbol = staticInfo.Symbol,
                    Name = staticInfo.Name,
                    Market = staticInfo.Market,
                    Currency = ResolveCurrencyCode(staticInfo.Currency, staticInfo.Market)
                }
            ];
        }

        return new List<Stock>();
    }

    public async Task<LongBridgeCompanyProfile?> GetCompanyProfileAsync(string symbol)
    {
        var normalized = NormalizeSymbol(symbol);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (_companyProfileCache.TryGetValue(normalized, out var cached)
            && DateTime.UtcNow - cached.FetchedAtUtc < CompanyProfileCacheDuration)
        {
            return cached.Profile;
        }

        var stock = await GetStockInfoAsync(normalized);
        if (stock == null)
        {
            return null;
        }

        string overview = $"{stock.Name}（{stock.Symbol}）的实时行情已接入 Longbridge，支持在本页面查看行情、K线、交易与 AI 分析。";
        string sourceUrl = string.Empty;
        var fields = new List<KeyValuePair<string, string>>
        {
            new("代码", stock.Symbol),
            new("名称", stock.Name),
            new("市场", stock.Market),
            new("币种", ResolveCurrencyCode(stock.Currency, stock.Market)),
            new("现价", stock.CurrentPrice > 0 ? stock.CurrentPrice.ToString("F2", CultureInfo.InvariantCulture) : "-"),
            new("涨跌幅", $"{stock.ChangePercent:+0.00;-0.00;0.00}%"),
            new("开盘", stock.Open > 0 ? stock.Open.ToString("F2", CultureInfo.InvariantCulture) : "-"),
            new("最高", stock.High > 0 ? stock.High.ToString("F2", CultureInfo.InvariantCulture) : "-"),
            new("最低", stock.Low > 0 ? stock.Low.ToString("F2", CultureInfo.InvariantCulture) : "-"),
            new("昨收", stock.PreviousClose > 0 ? stock.PreviousClose.ToString("F2", CultureInfo.InvariantCulture) : "-"),
            new("成交量", stock.Volume > 0 ? stock.Volume.ToString("N0", CultureInfo.InvariantCulture) : "-"),
            new("市值", stock.MarketCap > 0 ? stock.MarketCap.ToString("F2", CultureInfo.InvariantCulture) : "-"),
            new("市盈率", stock.Pe > 0 ? stock.Pe.ToString("F2", CultureInfo.InvariantCulture) : "-"),
            new("每股收益", stock.Eps > 0 ? stock.Eps.ToString("F2", CultureInfo.InvariantCulture) : "-"),
            new("股息率", stock.Dividend > 0 ? $"{stock.Dividend:F2}%" : "-"),
            new("52周区间", stock.High52Week > 0 && stock.Low52Week > 0
                ? $"{stock.Low52Week:F2} - {stock.High52Week:F2}"
                : "-"),
            new("行情时间", stock.LastUpdated == default
                ? "-"
                : stock.LastUpdated.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture))
        };

        var markdown = await TryFetchCompanyMarkdownAsync(normalized);
        if (markdown != null)
        {
            if (!string.IsNullOrWhiteSpace(markdown.Title) && !IsPoorDisplayName(markdown.Title, normalized))
            {
                stock.Name = markdown.Title;
            }

            if (!string.IsNullOrWhiteSpace(markdown.Overview))
            {
                overview = markdown.Overview;
            }

            sourceUrl = markdown.SourceUrl;
            foreach (var item in markdown.Fields)
            {
                if (!string.IsNullOrWhiteSpace(item.Key) && !string.IsNullOrWhiteSpace(item.Value))
                {
                    fields.Add(item);
                }
            }
        }

        var profile = new LongBridgeCompanyProfile
        {
            Symbol = stock.Symbol,
            Name = stock.Name,
            Overview = overview,
            SourceUrl = sourceUrl,
            Fields = fields
                .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList()
        };

        _companyProfileCache[normalized] = new CachedCompanyProfile(DateTime.UtcNow, profile);
        return profile;
    }

    private async Task<StaticSecurityInfo?> GetStaticSecurityInfoAsync(string symbol)
    {
        var normalized = NormalizeSymbol(symbol);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var list = await GetStaticSecurityInfosAsync([normalized]);
        return list.FirstOrDefault(item => SymbolEquals(item.Symbol, normalized));
    }

    private async Task<List<StaticSecurityInfo>> GetStaticSecurityInfosAsync(IEnumerable<string> symbols)
    {
        var normalizedSymbols = symbols
            .Select(NormalizeSymbol)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToList();

        if (!normalizedSymbols.Any())
        {
            return new List<StaticSecurityInfo>();
        }

        var query = string.Join("&", normalizedSymbols.Select(item => $"symbol={Uri.EscapeDataString(item)}"));
        var response = await SendRequestWithFallbackPathsAsync(
            [
                $"/v1/quote/static_info?{query}",
                $"/v1/quote/static?{query}",
                $"/quote/static_info?{query}",
                $"/quote/static?{query}"
            ],
            HttpMethod.Get);
        if (string.IsNullOrWhiteSpace(response))
        {
            return new List<StaticSecurityInfo>();
        }

        try
        {
            var json = JObject.Parse(response);
            var rows = json["data"]?["secu_static_info"] as JArray
                ?? json["data"]?["security_static_info"] as JArray
                ?? json["data"]?["list"] as JArray
                ?? json["data"] as JArray
                ?? json["secu_static_info"] as JArray
                ?? json["security_static_info"] as JArray
                ?? json["list"] as JArray;

            if (rows == null)
            {
                return new List<StaticSecurityInfo>();
            }

            var result = new List<StaticSecurityInfo>();
            foreach (var item in rows)
            {
                var symbol = NormalizeSymbol(item["symbol"]?.ToString() ?? string.Empty);
                if (string.IsNullOrWhiteSpace(symbol))
                {
                    continue;
                }

                var name = item["name_cn"]?.ToString()
                    ?? item["name_zh"]?.ToString()
                    ?? item["name_hk"]?.ToString()
                    ?? item["name_en"]?.ToString()
                    ?? item["name"]?.ToString()
                    ?? symbol;
                var market = InferMarketFromSymbol(symbol, item["market"]?.ToString() ?? string.Empty);
                var currency = item["currency"]?.ToString();

                result.Add(new StaticSecurityInfo
                {
                    Symbol = symbol,
                    Name = string.IsNullOrWhiteSpace(name) ? symbol : name.Trim(),
                    Market = string.IsNullOrWhiteSpace(market) ? InferMarketFromSymbol(symbol) : market,
                    Currency = ResolveCurrencyCode(currency, market),
                    TotalShares = ParseLongToken(item["total_shares"]),
                    CirculatingShares = ParseLongToken(item["circulating_shares"]),
                    Eps = ParseDecimalToken(item["eps"]),
                    EpsTtm = ParseDecimalToken(item["eps_ttm"]),
                    DividendYield = ParseDecimalToken(item["dividend_yield"])
                });
            }

            return result
                .GroupBy(item => item.Symbol, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse static info response for symbols: {Symbols}", string.Join(",", normalizedSymbols));
            return new List<StaticSecurityInfo>();
        }
    }

    private async Task<string> ResolveDisplayNameAsync(string symbol, string? fallbackName, StaticSecurityInfo? staticInfo = null)
    {
        var normalized = NormalizeSymbol(symbol);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var firstCandidate = staticInfo?.Name;
        if (!IsPoorDisplayName(firstCandidate, normalized))
        {
            return firstCandidate!.Trim();
        }

        if (!IsPoorDisplayName(fallbackName, normalized))
        {
            return fallbackName!.Trim();
        }

        var markdownTitle = await TryGetCompanyTitleFromMarkdownAsync(normalized);
        if (!IsPoorDisplayName(markdownTitle, normalized))
        {
            return markdownTitle!;
        }

        var pageTitle = await TryGetCompanyTitleFromQuotePageAsync(normalized);
        if (!IsPoorDisplayName(pageTitle, normalized))
        {
            return pageTitle!;
        }

        var cnName = await TryResolveAStockNameBySymbolAsync(normalized);
        if (!IsPoorDisplayName(cnName, normalized))
        {
            return cnName!;
        }

        return normalized.Split('.').FirstOrDefault() ?? normalized;
    }

    private async Task<string?> TryGetCompanyTitleFromMarkdownAsync(string symbol)
    {
        var markdown = await TryFetchCompanyMarkdownAsync(symbol);
        return markdown?.Title;
    }

    private async Task<string?> TryGetCompanyTitleFromQuotePageAsync(string symbol)
    {
        var normalized = NormalizeSymbol(symbol);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        foreach (var locale in LongbridgeQuotePageLocales)
        {
            var url = $"https://longbridge.com/{locale}/quote/{Uri.EscapeDataString(normalized)}";
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd("QuantTrading/1.0");
                request.Headers.AcceptLanguage.ParseAdd(locale);

                var response = await client.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                var html = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(html))
                {
                    continue;
                }

                var titleMatch = Regex.Match(html, @"<title>(?<title>.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (!titleMatch.Success)
                {
                    continue;
                }

                var decoded = WebUtility.HtmlDecode(titleMatch.Groups["title"].Value);
                var normalizedTitle = NormalizeCompanyTitle(decoded, normalized);
                if (!string.IsNullOrWhiteSpace(normalizedTitle))
                {
                    return normalizedTitle;
                }
            }
            catch
            {
                // ignore locale fallback errors
            }
        }

        return null;
    }

    private async Task<MarkdownCompanyProfile?> TryFetchCompanyMarkdownAsync(string symbol)
    {
        var normalized = NormalizeSymbol(symbol);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        foreach (var locale in LongbridgeQuotePageLocales)
        {
            var url = $"https://longbridge.com/{locale}/quote/{Uri.EscapeDataString(normalized)}.md";
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/markdown"));
                var response = await client.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                var markdown = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(markdown))
                {
                    continue;
                }

                var title = ExtractCompanyTitleFromMarkdown(markdown, normalized);
                var overview = ExtractCompanyOverviewFromMarkdown(markdown);
                var fields = ExtractCompanyFieldsFromMarkdown(markdown);
                return new MarkdownCompanyProfile
                {
                    Title = title,
                    Overview = overview,
                    SourceUrl = url,
                    Fields = fields
                };
            }
            catch
            {
                // ignore locale fallback errors
            }
        }

        return null;
    }

    private static void ApplySnapshotFieldsFromMarkdown(StockSnapshot snapshot, IEnumerable<KeyValuePair<string, string>> fields)
    {
        foreach (var field in fields)
        {
            var key = (field.Key ?? string.Empty).Trim();
            var value = (field.Value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value) || value == "-")
            {
                continue;
            }

            if ((key.Contains("市值", StringComparison.OrdinalIgnoreCase)
                    || key.Contains("market cap", StringComparison.OrdinalIgnoreCase))
                && snapshot.MarketCap <= 0)
            {
                snapshot.MarketCap = ParseScaledDecimalValue(value);
                continue;
            }

            if ((key.Contains("市盈率", StringComparison.OrdinalIgnoreCase)
                    || key.Contains("PE", StringComparison.OrdinalIgnoreCase))
                && snapshot.Pe <= 0)
            {
                snapshot.Pe = ParseFirstDecimalValue(value);
                continue;
            }

            if ((key.Contains("每股收益", StringComparison.OrdinalIgnoreCase)
                    || key.Contains("EPS", StringComparison.OrdinalIgnoreCase))
                && snapshot.Eps <= 0)
            {
                snapshot.Eps = ParseFirstDecimalValue(value);
                continue;
            }

            if ((key.Contains("股息率", StringComparison.OrdinalIgnoreCase)
                    || key.Contains("Dividend", StringComparison.OrdinalIgnoreCase))
                && snapshot.DividendYield <= 0)
            {
                snapshot.DividendYield = ParsePercentValue(value);
                continue;
            }

            if ((key.Contains("52周区间", StringComparison.OrdinalIgnoreCase)
                    || key.Contains("52 Week", StringComparison.OrdinalIgnoreCase))
                && (snapshot.High52Week <= 0 || snapshot.Low52Week <= 0))
            {
                if (TryParseRangePair(value, out var low, out var high))
                {
                    snapshot.Low52Week = snapshot.Low52Week > 0 ? snapshot.Low52Week : low;
                    snapshot.High52Week = snapshot.High52Week > 0 ? snapshot.High52Week : high;
                }
                continue;
            }

            if ((key.Contains("52周最高", StringComparison.OrdinalIgnoreCase)
                    || key.Contains("52-week high", StringComparison.OrdinalIgnoreCase))
                && snapshot.High52Week <= 0)
            {
                snapshot.High52Week = ParseFirstDecimalValue(value);
                continue;
            }

            if ((key.Contains("52周最低", StringComparison.OrdinalIgnoreCase)
                    || key.Contains("52-week low", StringComparison.OrdinalIgnoreCase))
                && snapshot.Low52Week <= 0)
            {
                snapshot.Low52Week = ParseFirstDecimalValue(value);
                continue;
            }

            if ((key.Contains("平均成交量", StringComparison.OrdinalIgnoreCase)
                    || key.Contains("avg volume", StringComparison.OrdinalIgnoreCase))
                && snapshot.AvgVolume <= 0)
            {
                snapshot.AvgVolume = ParseLongValue(value);
            }
        }
    }

    private static bool TryParseRangePair(string value, out decimal low, out decimal high)
    {
        low = 0;
        high = 0;
        var text = StripHtml(value);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var matches = NumberTokenRegex.Matches(text);
        if (matches.Count < 2)
        {
            return false;
        }

        var first = ParseFlexibleDecimalToken(matches[0].Value);
        var second = ParseFlexibleDecimalToken(matches[1].Value);
        if (first <= 0 || second <= 0)
        {
            return false;
        }

        low = Math.Min(first, second);
        high = Math.Max(first, second);
        return true;
    }

    private static string ExtractCompanyTitleFromMarkdown(string markdown, string symbol)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return symbol;
        }

        var titleMatch = FrontmatterTitleRegex.Match(markdown);
        if (titleMatch.Success)
        {
            var title = NormalizeCompanyTitle(titleMatch.Groups["title"].Value, symbol);
            if (!string.IsNullOrWhiteSpace(title))
            {
                return title;
            }
        }

        var headingMatch = MarkdownHeadingRegex.Match(markdown);
        if (headingMatch.Success)
        {
            var title = NormalizeCompanyTitle(headingMatch.Groups["title"].Value, symbol);
            if (!string.IsNullOrWhiteSpace(title))
            {
                return title;
            }
        }

        return symbol.Split('.').FirstOrDefault() ?? symbol;
    }

    private static string ExtractCompanyOverviewFromMarkdown(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var lines = markdown.Split('\n');
        var start = Array.FindIndex(lines, line =>
            Regex.IsMatch(line.Trim(), @"^##\s*(公司概览|公司概況|公司概况|公司簡介|Company Overview)", RegexOptions.IgnoreCase));
        var begin = start >= 0 ? start + 1 : 0;
        var end = lines.Length;
        for (var i = begin; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("## ", StringComparison.Ordinal))
            {
                end = i;
                break;
            }
        }

        var chunk = string.Join('\n', lines[begin..end]);
        var plain = Regex.Replace(chunk, @"\|.*\|", string.Empty);
        plain = Regex.Replace(plain, @"[`*_>#-]", " ");
        plain = Regex.Replace(plain, @"\s+", " ").Trim();
        return plain;
    }

    private static List<KeyValuePair<string, string>> ExtractCompanyFieldsFromMarkdown(string markdown)
    {
        var fields = new List<KeyValuePair<string, string>>();
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return fields;
        }

        var lines = markdown.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith('|') || !trimmed.EndsWith('|'))
            {
                continue;
            }

            var parts = trimmed
                .Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToList();
            if (parts.Count < 2)
            {
                continue;
            }

            var key = Regex.Replace(parts[0], @"[`*_]", string.Empty).Trim();
            var value = Regex.Replace(parts[1], @"[`*_]", string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (key is "项目" or "指标" or "Item" or "Detail" || key.All(ch => ch == '-'))
            {
                continue;
            }

            fields.Add(new KeyValuePair<string, string>(key, value));
            if (fields.Count >= 16)
            {
                break;
            }
        }

        return fields;
    }

    private static string NormalizeCompanyTitle(string rawTitle, string symbol)
    {
        var title = (rawTitle ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var normalizedSymbol = NormalizeSymbol(symbol);
        var baseSymbol = normalizedSymbol.Split('.').FirstOrDefault() ?? normalizedSymbol;

        title = title
            .Replace(normalizedSymbol, string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(baseSymbol, string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("()", string.Empty, StringComparison.Ordinal)
            .Replace("（）", string.Empty, StringComparison.Ordinal)
            .Trim();

        title = Regex.Replace(title, @"[\(\[（【].*?[\)\]）】]", string.Empty).Trim();
        title = title.Replace("Longbridge", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        title = title.Trim('-', '—', '|', ':', '：', ' ');
        return title;
    }

    private static bool IsPoorDisplayName(string? value, string symbol)
    {
        var name = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return true;
        }

        var normalizedSymbol = NormalizeSymbol(symbol);
        var baseSymbol = normalizedSymbol.Split('.').FirstOrDefault() ?? normalizedSymbol;
        if (name.Equals(normalizedSymbol, StringComparison.OrdinalIgnoreCase)
            || name.Equals(baseSymbol, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (Regex.IsMatch(name, @"^\d{4,6}$"))
        {
            return true;
        }

        return false;
    }

    private static string ResolveCurrencyCode(string? currency, string? market)
    {
        var normalized = (currency ?? string.Empty).Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            return normalized;
        }

        var marketCode = NormalizeMarketCode(market);
        return marketCode switch
        {
            "HK" => "HKD",
            "SH" or "SZ" => "CNY",
            "SG" => "SGD",
            _ => "USD"
        };
    }

    private async Task<List<Stock>> GetMarketSecuritiesAsync(string market)
    {
        await _securityListCacheLock.WaitAsync();
        try
        {
            if (_securityListCache.TryGetValue(market, out var cached) &&
                DateTime.UtcNow - cached.FetchedAtUtc < SecurityListCacheDuration)
            {
                return cached.Securities;
            }
        }
        finally
        {
            _securityListCacheLock.Release();
        }

        var fetched = await FetchMarketSecuritiesAsync(market);

        await _securityListCacheLock.WaitAsync();
        try
        {
            if (fetched.Count == 0 &&
                _securityListCache.TryGetValue(market, out var staleCached) &&
                staleCached.Securities.Count > 0)
            {
                return staleCached.Securities;
            }

            _securityListCache[market] = new CachedSecurityList(DateTime.UtcNow, fetched);
        }
        finally
        {
            _securityListCacheLock.Release();
        }

        return fetched;
    }

    private async Task<List<Stock>> FetchMarketSecuritiesAsync(string market)
    {
        var result = new List<Stock>();
        var normalizedMarket = NormalizeMarketCode(market);
        if (!normalizedMarket.Equals("US", StringComparison.OrdinalIgnoreCase))
        {
            return result;
        }

        try
        {
            foreach (var category in GetSecurityListCategories(normalizedMarket))
            {
                var response = await SendRequestAsync($"/v1/quote/get_security_list?market={normalizedMarket}&category={Uri.EscapeDataString(category)}", HttpMethod.Get);
                if (string.IsNullOrWhiteSpace(response))
                {
                    continue;
                }

                var chunk = ParseSecurityListResponse(response, market);
                if (chunk.Count > 0)
                {
                    result.AddRange(chunk);
                }
            }

            if (result.Count == 0)
            {
                var fallback = await SendRequestAsync($"/v1/quote/get_security_list?market={normalizedMarket}", HttpMethod.Get);
                if (!string.IsNullOrWhiteSpace(fallback))
                {
                    result.AddRange(ParseSecurityListResponse(fallback, normalizedMarket));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load security list for market {Market}", market);
        }

        return result
            .GroupBy(item => item.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static IEnumerable<string> GetSecurityListCategories(string market)
    {
        _ = market;
        return ["Overnight"];
    }

    private List<Stock> ParseSecurityListResponse(string response, string market)
    {
        var result = new List<Stock>();

        var json = JObject.Parse(response);
        var list = json["data"]?["secu_list"] as JArray
            ?? json["data"]?["list"] as JArray
            ?? json["data"] as JArray
            ?? json["secu_list"] as JArray
            ?? json["list"] as JArray;

        if (list == null)
        {
            return result;
        }

        foreach (var item in list)
        {
            var symbol = (item["symbol"]?.ToString() ?? string.Empty).Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(symbol))
            {
                continue;
            }

            var name = item["name"]?.ToString()
                ?? item["name_cn"]?.ToString()
                ?? item["name_en"]?.ToString()
                ?? item["name_zh"]?.ToString()
                ?? item["name_hk"]?.ToString()
                ?? symbol;

            result.Add(new Stock
            {
                Symbol = symbol,
                Name = string.IsNullOrWhiteSpace(name) ? symbol : name,
                Market = InferMarketFromSymbol(symbol, market)
            });
        }

        return result;
    }

    private async Task<List<Stock>> SearchAStockByEastMoneyAsync(string keyword)
    {
        var query = (keyword ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(query))
        {
            return new List<Stock>();
        }

        if (query.StartsWith("SH", StringComparison.OrdinalIgnoreCase)
            || query.StartsWith("SZ", StringComparison.OrdinalIgnoreCase))
        {
            var maybeCode = query.Length >= 8 ? query[2..8] : query;
            if (maybeCode.Length == 6 && maybeCode.All(char.IsDigit))
            {
                query = maybeCode;
            }
        }

        if (query.Contains('.', StringComparison.Ordinal))
        {
            var head = query.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? query;
            if (head.Length == 6 && head.All(char.IsDigit))
            {
                query = head;
            }
        }

        var encoded = Uri.EscapeDataString(query);
        var url = $"{EastMoneySuggestUrl}?input={encoded}&type=14&token={EastMoneySuggestToken}";

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("QuantTrading/1.0");
            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return new List<Stock>();
            }

            var raw = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new List<Stock>();
            }

            var json = JObject.Parse(raw);
            var rows = json["QuotationCodeTable"]?["Data"] as JArray;
            if (rows == null)
            {
                return new List<Stock>();
            }

            return rows
                .Select(item =>
                {
                    var code = (item["Code"]?.ToString() ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(code) || !code.All(char.IsDigit) || code.Length != 6)
                    {
                        return null;
                    }

                    var market = ResolveAStockMarket(item);
                    if (string.IsNullOrWhiteSpace(market))
                    {
                        return null;
                    }

                    var symbol = $"{code}.{market}";
                    var name = (item["Name"]?.ToString() ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        return null;
                    }

                    return new Stock
                    {
                        Symbol = symbol,
                        Name = name,
                        Market = market,
                        Currency = "CNY"
                    };
                })
                .Where(item => item != null)
                .Cast<Stock>()
                .DistinctBy(item => item.Symbol, StringComparer.OrdinalIgnoreCase)
                .Take(20)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EastMoney fallback search failed for keyword {Keyword}", query);
            return new List<Stock>();
        }
    }

    private async Task<string?> TryResolveAStockNameBySymbolAsync(string symbol)
    {
        var normalized = NormalizeSymbol(symbol);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var market = InferMarketFromSymbol(normalized);
        if (market is not ("SH" or "SZ"))
        {
            return null;
        }

        var code = normalized.Split('.').FirstOrDefault() ?? normalized;
        if (string.IsNullOrWhiteSpace(code) || code.Length != 6 || !code.All(char.IsDigit))
        {
            return null;
        }

        var rows = await SearchAStockByEastMoneyAsync(code);
        var matched = rows.FirstOrDefault(item => SymbolEquals(item.Symbol, normalized))
            ?? rows.FirstOrDefault(item => item.Symbol.StartsWith($"{code}.", StringComparison.OrdinalIgnoreCase));
        return matched?.Name;
    }

    private static string ResolveAStockMarket(JToken item)
    {
        var explicitMarket = (item["MktNum"]?.ToString() ?? string.Empty).Trim();
        if (explicitMarket == "1")
        {
            return "SH";
        }

        if (explicitMarket == "0")
        {
            return "SZ";
        }

        var typeName = (item["SecurityTypeName"]?.ToString() ?? string.Empty).Trim();
        if (typeName.Contains("沪", StringComparison.Ordinal))
        {
            return "SH";
        }

        if (typeName.Contains("深", StringComparison.Ordinal))
        {
            return "SZ";
        }

        var code = (item["Code"]?.ToString() ?? string.Empty).Trim();
        if (code.Length == 6 && code.All(char.IsDigit))
        {
            return code.StartsWith("6", StringComparison.Ordinal)
                || code.StartsWith("9", StringComparison.Ordinal)
                || code.StartsWith("5", StringComparison.Ordinal)
                ? "SH"
                : "SZ";
        }

        return string.Empty;
    }

    private static IEnumerable<string> GetCandidateMarkets(string keyword)
    {
        var normalized = (keyword ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return DefaultSearchMarkets;
        }

        if (normalized.StartsWith("SH", StringComparison.OrdinalIgnoreCase) && normalized.Length == 8 && normalized.Skip(2).All(char.IsDigit))
        {
            return ["SH"];
        }

        if (normalized.StartsWith("SZ", StringComparison.OrdinalIgnoreCase) && normalized.Length == 8 && normalized.Skip(2).All(char.IsDigit))
        {
            return ["SZ"];
        }

        if (normalized.Contains('.'))
        {
            var suffix = normalized.Split('.').LastOrDefault() ?? string.Empty;
            if (suffix.Equals("US", StringComparison.OrdinalIgnoreCase) ||
                suffix.Equals("HK", StringComparison.OrdinalIgnoreCase) ||
                suffix.Equals("SG", StringComparison.OrdinalIgnoreCase) ||
                suffix.Equals("SH", StringComparison.OrdinalIgnoreCase) ||
                suffix.Equals("SZ", StringComparison.OrdinalIgnoreCase))
            {
                return [suffix];
            }

            if (suffix.Equals("CN", StringComparison.OrdinalIgnoreCase))
            {
                return ["SH", "SZ"];
            }

            return [];
        }

        if (normalized.All(char.IsDigit))
        {
            if (normalized.Length == 5)
            {
                return ["HK"];
            }

            if (normalized.Length == 6)
            {
                return normalized.StartsWith("6", StringComparison.Ordinal) || normalized.StartsWith("9", StringComparison.Ordinal)
                    ? ["SH"]
                    : ["SZ"];
            }
        }

        return DefaultSearchMarkets;
    }

    private static bool IsKeywordMatch(Stock stock, string keyword)
    {
        var symbol = stock.Symbol?.ToUpperInvariant() ?? string.Empty;
        var baseSymbol = symbol.Split('.').FirstOrDefault() ?? symbol;
        var name = stock.Name?.ToUpperInvariant() ?? string.Empty;

        return symbol.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || baseSymbol.StartsWith(keyword, StringComparison.OrdinalIgnoreCase)
            || name.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private static int GetMatchScore(Stock stock, string keyword)
    {
        var symbol = stock.Symbol?.ToUpperInvariant() ?? string.Empty;
        var baseSymbol = symbol.Split('.').FirstOrDefault() ?? symbol;
        var name = stock.Name?.ToUpperInvariant() ?? string.Empty;

        if (symbol == keyword)
        {
            return 0;
        }

        if (baseSymbol == keyword)
        {
            return 1;
        }

        if (symbol.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (baseSymbol.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        if (name.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }

        if (symbol.Contains(keyword, StringComparison.OrdinalIgnoreCase))
        {
            return 5;
        }

        if (name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
        {
            return 6;
        }

        return 7;
    }

    private static string NormalizeSymbol(string symbol)
    {
        var normalized = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var cnPrefixMatch = Regex.Match(normalized, @"^(SH|SZ)(\d{6})$");
        if (cnPrefixMatch.Success)
        {
            return $"{cnPrefixMatch.Groups[2].Value}.{cnPrefixMatch.Groups[1].Value}";
        }

        if (normalized.Contains('.'))
        {
            return normalized;
        }

        if (normalized.All(char.IsDigit) && normalized.Length == 6)
        {
            var market = normalized.StartsWith("6", StringComparison.Ordinal)
                || normalized.StartsWith("9", StringComparison.Ordinal)
                || normalized.StartsWith("5", StringComparison.Ordinal)
                ? "SH"
                : "SZ";
            return $"{normalized}.{market}";
        }

        if (normalized.All(char.IsDigit) && normalized.Length == 5)
        {
            return $"{normalized}.HK";
        }

        return $"{normalized}.US";
    }

    private static bool SymbolEquals(string? left, string? right)
    {
        var normalizedLeft = NormalizeSymbol(left ?? string.Empty);
        var normalizedRight = NormalizeSymbol(right ?? string.Empty);
        if (normalizedLeft.Equals(normalizedRight, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var leftBase = normalizedLeft.Split('.').FirstOrDefault() ?? normalizedLeft;
        var rightBase = normalizedRight.Split('.').FirstOrDefault() ?? normalizedRight;
        return leftBase.Equals(rightBase, StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeSymbolKeyword(string keyword)
    {
        var normalized = (keyword ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (Regex.IsMatch(normalized, @"^(SH|SZ)\d{6}$"))
        {
            return true;
        }

        if (Regex.IsMatch(normalized, @"^[A-Z0-9]{1,12}(\.(US|HK|SH|SZ|SG|CN))?$"))
        {
            return true;
        }

        return false;
    }

    private async Task<StockQuote?> GetQuoteFromLongbridgePageAsync(string symbol)
    {
        var normalizedSymbol = NormalizeSymbol(symbol);
        if (string.IsNullOrWhiteSpace(normalizedSymbol))
        {
            return null;
        }

        DateTime? snapshotTimestamp = null;
        foreach (var locale in LongbridgeQuotePageLocales)
        {
            var url = $"https://longbridge.com/{locale}/quote/{Uri.EscapeDataString(normalizedSymbol)}";
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd("QuantTrading/1.0");
                request.Headers.AcceptLanguage.ParseAdd(locale);

                var response = await client.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                var html = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(html))
                {
                    continue;
                }

                var jsonMatch = Regex.Match(
                    html,
                    "<script id=\"__NEXT_DATA__\" type=\"application/json\">(?<json>.*?)</script>",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase);
                if (!jsonMatch.Success)
                {
                    continue;
                }

                var nextData = JObject.Parse(WebUtility.HtmlDecode(jsonMatch.Groups["json"].Value));
                var pageProps = nextData["props"]?["pageProps"];
                var quoteNode = TryFindQuoteNodeForSymbol(pageProps, normalizedSymbol);
                if (quoteNode == null)
                {
                    continue;
                }

                var price = ParseDecimalToken(quoteNode["last_done"]);
                if (price <= 0)
                {
                    price = ParseDecimalToken(quoteNode["lastDone"]);
                }

                if (price <= 0)
                {
                    continue;
                }

                var previousClose = ParseDecimalToken(quoteNode["prev_close"]);
                if (previousClose <= 0)
                {
                    previousClose = ParseDecimalToken(quoteNode["prevClose"]);
                }

                var changePercent = NormalizeChangePercent(ParseDecimalToken(quoteNode["change"]));
                if (changePercent == 0)
                {
                    changePercent = ParseDecimalToken(quoteNode["change_rate"]);
                }

                if (previousClose <= 0 && changePercent > -99.9m && changePercent < 1000m)
                {
                    var ratio = 1 + changePercent / 100m;
                    if (ratio > 0)
                    {
                        previousClose = price / ratio;
                    }
                }

                var change = previousClose > 0 ? price - previousClose : 0;
                var timestamp = ParseQuoteTimestamp(quoteNode["timestamp"]);
                if (timestamp <= DateTime.UnixEpoch)
                {
                    snapshotTimestamp ??= await TryGetLongbridgeQuoteSnapshotTimeAsync(normalizedSymbol, locale);
                    timestamp = snapshotTimestamp ?? DateTime.UtcNow;
                }

                return new StockQuote
                {
                    Symbol = normalizedSymbol,
                    Price = price,
                    PreviousClose = previousClose,
                    Change = change,
                    ChangePercent = changePercent != 0
                        ? changePercent
                        : (previousClose > 0 ? change / previousClose * 100 : 0),
                    Open = ParseDecimalToken(quoteNode["open"]),
                    High = ParseDecimalToken(quoteNode["high"]),
                    Low = ParseDecimalToken(quoteNode["low"]),
                    Volume = ParseLongToken(quoteNode["volume"]),
                    Turnover = ParseDecimalToken(quoteNode["turnover"]),
                    Timestamp = timestamp
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse quote page JSON for {Symbol} ({Locale})", normalizedSymbol, locale);
            }
        }

        return null;
    }

    private async Task<DateTime?> TryGetLongbridgeQuoteSnapshotTimeAsync(string symbol, string? preferredLocale = null)
    {
        var normalized = NormalizeSymbol(symbol);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var locales = BuildLocaleCandidates(preferredLocale);
        foreach (var locale in locales)
        {
            var url = $"https://longbridge.com/{locale}/quote/{Uri.EscapeDataString(normalized)}.md";
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/markdown"));
                var response = await client.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                var markdown = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(markdown))
                {
                    continue;
                }

                var match = Regex.Match(markdown, "^datetime:\\s*\"(?<dt>[^\"]+)\"\\s*$", RegexOptions.Multiline);
                if (!match.Success)
                {
                    continue;
                }

                if (DateTime.TryParse(
                    match.Groups["dt"].Value.Trim(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsed))
                {
                    return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
                }
            }
            catch
            {
                // ignore markdown fallback errors
            }
        }

        return null;
    }

    private static IEnumerable<string> BuildLocaleCandidates(string? preferredLocale)
    {
        var preferred = (preferredLocale ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            yield return preferred;
        }

        foreach (var locale in LongbridgeQuotePageLocales)
        {
            if (locale.Equals(preferred, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return locale;
        }
    }

    private static JObject? TryFindQuoteNodeForSymbol(JToken? token, string normalizedSymbol)
    {
        if (token == null)
        {
            return null;
        }

        if (token is JObject obj)
        {
            var candidate = NormalizeSymbolFromPageNode(obj);
            var hasQuoteValue = ParseDecimalToken(obj["last_done"]) > 0
                || ParseDecimalToken(obj["lastDone"]) > 0;
            if (!string.IsNullOrWhiteSpace(candidate)
                && SymbolEquals(candidate, normalizedSymbol)
                && hasQuoteValue)
            {
                return obj;
            }

            foreach (var property in obj.Properties())
            {
                var found = TryFindQuoteNodeForSymbol(property.Value, normalizedSymbol);
                if (found != null)
                {
                    return found;
                }
            }
        }
        else if (token is JArray array)
        {
            foreach (var item in array)
            {
                var found = TryFindQuoteNodeForSymbol(item, normalizedSymbol);
                if (found != null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    private static string NormalizeSymbolFromPageNode(JObject obj)
    {
        var stockCode = (obj["stockCode"]?.ToString() ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(stockCode))
        {
            return NormalizeSymbol(stockCode);
        }

        var ticker = (obj["ticker"]?.ToString() ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(ticker))
        {
            return NormalizeSymbol(ticker);
        }

        var counterId = (obj["counter_id"]?.ToString() ?? obj["counterId"]?.ToString() ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(counterId))
        {
            var match = Regex.Match(counterId.ToUpperInvariant(), @"ST/(?<market>US|HK|SH|SZ|SG|CN)/(?<code>[A-Z0-9]+)");
            if (match.Success)
            {
                var market = match.Groups["market"].Value;
                var code = match.Groups["code"].Value;
                if (market == "CN" && code.All(char.IsDigit) && code.Length == 6)
                {
                    market = code.StartsWith("6", StringComparison.Ordinal)
                        || code.StartsWith("9", StringComparison.Ordinal)
                        || code.StartsWith("5", StringComparison.Ordinal)
                        ? "SH"
                        : "SZ";
                }

                return NormalizeSymbol($"{code}.{market}");
            }
        }

        var symbolValue = (obj["symbol"]?.ToString() ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(symbolValue))
        {
            symbolValue = (obj["code"]?.ToString() ?? string.Empty).Trim();
        }

        if (string.IsNullOrWhiteSpace(symbolValue))
        {
            return string.Empty;
        }

        var marketHint = (obj["market"]?.ToString() ?? string.Empty).Trim().ToUpperInvariant();
        if (marketHint == "CN" && symbolValue.All(char.IsDigit) && symbolValue.Length == 6)
        {
            marketHint = symbolValue.StartsWith("6", StringComparison.Ordinal)
                || symbolValue.StartsWith("9", StringComparison.Ordinal)
                || symbolValue.StartsWith("5", StringComparison.Ordinal)
                ? "SH"
                : "SZ";
        }

        if (!symbolValue.Contains('.') && !string.IsNullOrWhiteSpace(marketHint))
        {
            symbolValue = $"{symbolValue}.{marketHint}";
        }

        return NormalizeSymbol(symbolValue);
    }

    private static decimal NormalizeChangePercent(decimal raw)
    {
        if (raw == 0)
        {
            return 0;
        }

        return Math.Abs(raw) <= 1.5m ? raw * 100 : raw;
    }

    private static bool IsAStockSymbol(string? symbol)
    {
        var market = InferMarketFromSymbol(symbol ?? string.Empty);
        return market is "SH" or "SZ";
    }

    private static bool TryBuildEastMoneySecId(string symbol, out string secId)
    {
        secId = string.Empty;
        var normalized = NormalizeSymbol(symbol);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var parts = normalized.Split('.', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        var code = parts[0];
        var market = parts[1].ToUpperInvariant();
        if (!Regex.IsMatch(code, @"^\d{6}$"))
        {
            return false;
        }

        secId = market switch
        {
            "SH" => $"1.{code}",
            "SZ" => $"0.{code}",
            _ => string.Empty
        };

        return !string.IsNullOrWhiteSpace(secId);
    }

    private async Task<EastMoneyQuoteSnapshot?> GetAStockQuoteSnapshotAsync(string symbol)
    {
        var normalized = NormalizeSymbol(symbol);
        if (!IsAStockSymbol(normalized) || !TryBuildEastMoneySecId(normalized, out var secId))
        {
            return null;
        }

        var url = $"{EastMoneyQuoteUrl}?secid={Uri.EscapeDataString(secId)}&fields=f57,f58,f43,f44,f45,f46,f47,f48,f60,f169,f170,f116,f162";
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("QuantTrading/1.0");
            request.Headers.Referrer = new Uri("https://quote.eastmoney.com/");

            using var response = await client.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            var json = JObject.Parse(content);
            var data = json["data"] as JObject;
            if (data == null)
            {
                return null;
            }

            var price = ParseEastMoneyScaledPrice(data["f43"]);
            if (price <= 0)
            {
                return null;
            }

            var quoteTime = DateTime.UtcNow;
            return new EastMoneyQuoteSnapshot
            {
                Symbol = normalized,
                Name = data["f58"]?.ToString() ?? string.Empty,
                Price = price,
                Open = ParseEastMoneyScaledPrice(data["f46"]),
                High = ParseEastMoneyScaledPrice(data["f44"]),
                Low = ParseEastMoneyScaledPrice(data["f45"]),
                PreviousClose = ParseEastMoneyScaledPrice(data["f60"]),
                Change = ParseEastMoneyScaledPrice(data["f169"]),
                ChangePercent = ParseEastMoneyScaledPrice(data["f170"]),
                Volume = ParseEastMoneyVolume(data["f47"]),
                Turnover = ParseDecimalToken(data["f48"]),
                MarketCap = ParseDecimalToken(data["f116"]),
                Pe = ParseEastMoneyScaledPrice(data["f162"]),
                Timestamp = quoteTime
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch EastMoney quote snapshot for {Symbol}", normalized);
            return null;
        }
    }

    private static long ParseEastMoneyVolume(JToken? token)
    {
        var hands = ParseLongToken(token);
        if (hands <= 0)
        {
            return 0;
        }

        try
        {
            return checked(hands * 100);
        }
        catch
        {
            return hands;
        }
    }

    private static decimal ParseEastMoneyScaledPrice(JToken? token)
    {
        var raw = ParseDecimalToken(token);
        if (raw == 0)
        {
            return 0;
        }

        if (decimal.Truncate(raw) == raw || Math.Abs(raw) >= 1000)
        {
            return raw / 100m;
        }

        return raw;
    }

    private static StockQuote ConvertEastMoneyQuoteToStockQuote(string symbol, EastMoneyQuoteSnapshot quote)
    {
        return new StockQuote
        {
            Symbol = NormalizeSymbol(symbol),
            Price = quote.Price,
            Open = quote.Open,
            High = quote.High,
            Low = quote.Low,
            Volume = quote.Volume,
            Turnover = quote.Turnover,
            PreviousClose = quote.PreviousClose,
            Change = quote.Change,
            ChangePercent = quote.ChangePercent,
            Timestamp = quote.Timestamp
        };
    }

    private static StockQuote MergeQuoteWithAStockFallback(
        StockQuote? original,
        string symbol,
        EastMoneyQuoteSnapshot fallback)
    {
        var merged = original ?? new StockQuote { Symbol = NormalizeSymbol(symbol) };
        MergeQuoteFromEastMoney(merged, fallback);
        return merged;
    }

    private static void MergeQuoteFromEastMoney(StockQuote target, EastMoneyQuoteSnapshot fallback)
    {
        if (target.Price <= 0 && fallback.Price > 0)
        {
            target.Price = fallback.Price;
        }

        if (target.PreviousClose <= 0 && fallback.PreviousClose > 0)
        {
            target.PreviousClose = fallback.PreviousClose;
        }

        if (target.Open <= 0 && fallback.Open > 0)
        {
            target.Open = fallback.Open;
        }

        if (target.High <= 0 && fallback.High > 0)
        {
            target.High = fallback.High;
        }

        if (target.Low <= 0 && fallback.Low > 0)
        {
            target.Low = fallback.Low;
        }

        if (target.Volume <= 0 && fallback.Volume > 0)
        {
            target.Volume = fallback.Volume;
        }

        if (target.Turnover <= 0 && fallback.Turnover > 0)
        {
            target.Turnover = fallback.Turnover;
        }

        if (target.Change == 0 && fallback.Change != 0)
        {
            target.Change = fallback.Change;
        }

        if (target.ChangePercent == 0 && fallback.ChangePercent != 0)
        {
            target.ChangePercent = fallback.ChangePercent;
        }

        if (target.Timestamp <= UnknownQuoteTimestamp && fallback.Timestamp > UnknownQuoteTimestamp)
        {
            target.Timestamp = fallback.Timestamp;
        }
    }

    private async Task<StockQuote?> GetQuoteFromPublicFallbacksAsync(string symbol)
    {
        var stooqQuote = await GetQuoteFromStooqAsync(symbol);
        if (stooqQuote?.Price > 0)
        {
            return stooqQuote;
        }

        var yahooKlines = await GetKlineFromYahooAsync(symbol, "D", 5);
        var yahooQuote = BuildQuoteFromKlines(symbol, yahooKlines);
        if (yahooQuote?.Price > 0)
        {
            return yahooQuote;
        }

        var nasdaqKlines = await GetKlineFromNasdaqAsync(symbol, "D", 5);
        var nasdaqQuote = BuildQuoteFromKlines(symbol, nasdaqKlines);
        if (nasdaqQuote?.Price > 0)
        {
            return nasdaqQuote;
        }

        return stooqQuote ?? yahooQuote ?? nasdaqQuote;
    }

    private static StockQuote? BuildQuoteFromKlines(string symbol, IReadOnlyCollection<StockKline>? klines)
    {
        var ordered = (klines ?? Array.Empty<StockKline>())
            .Where(k => k.Close > 0)
            .OrderBy(k => k.Timestamp)
            .ToList();

        if (!ordered.Any())
        {
            return null;
        }

        var latest = ordered[^1];
        var previous = ordered.Count >= 2 ? ordered[^2] : null;
        var previousClose = previous?.Close ?? latest.Open;
        if (previousClose <= 0)
        {
            previousClose = latest.Close;
        }

        var change = latest.Close - previousClose;
        var changePercent = previousClose > 0
            ? change / previousClose * 100
            : 0;

        return new StockQuote
        {
            Symbol = NormalizeSymbol(symbol),
            Price = latest.Close,
            Open = latest.Open > 0 ? latest.Open : latest.Close,
            High = latest.High > 0 ? latest.High : latest.Close,
            Low = latest.Low > 0 ? latest.Low : latest.Close,
            Volume = latest.Volume,
            Turnover = latest.Turnover,
            PreviousClose = previousClose,
            Change = change,
            ChangePercent = changePercent,
            Timestamp = latest.Timestamp == default ? DateTime.UtcNow : latest.Timestamp
        };
    }

    private static string InferMarketFromSymbol(string symbol, string fallback = "US")
    {
        var normalized = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (!normalized.Contains('.'))
        {
            return fallback.ToUpperInvariant();
        }

        var suffix = normalized.Split('.').LastOrDefault() ?? string.Empty;
        return suffix switch
        {
            "HK" => "HK",
            "CN" => "CN",
            "SH" => "SH",
            "SZ" => "SZ",
            "SG" => "SG",
            "US" => "US",
            _ => fallback.ToUpperInvariant()
        };
    }

    private async Task<StockQuote?> GetQuoteFromStooqAsync(string symbol)
    {
        try
        {
            var stooqSymbol = ToStooqSymbol(symbol);
            var response = await SendRequestAsync(
                $"https://stooq.com/q/l/?s={Uri.EscapeDataString(stooqSymbol)}&f=sd2t2ohlcvn&e=csv",
                HttpMethod.Get);

            if (string.IsNullOrWhiteSpace(response))
            {
                return null;
            }

            var line = response.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(line) || line.Contains("N/D", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var parts = line.Split(',');
            if (parts.Length < 8)
            {
                return null;
            }

            var symbolPart = NormalizeSymbol(parts[0]);
            var open = ParseDecimalValue(parts[3]);
            var high = ParseDecimalValue(parts[4]);
            var low = ParseDecimalValue(parts[5]);
            var close = ParseDecimalValue(parts[6]);
            var volume = ParseLongValue(parts[7]);

            var timestamp = ParseStooqDateTime(parts[1], parts[2]);
            return new StockQuote
            {
                Symbol = symbolPart,
                Price = close,
                Open = open,
                High = high,
                Low = low,
                Volume = volume,
                Timestamp = timestamp
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load quote from Stooq for {Symbol}", symbol);
            return null;
        }
    }

    private async Task<List<StockKline>> GetKlineFromStooqAsync(string symbol, string period, int count)
    {
        var result = new List<StockKline>();
        try
        {
            var stooqSymbol = ToStooqSymbol(symbol);
            var stooqInterval = MapPeriodToStooqInterval(period);
            var response = await SendRequestAsync(
                $"https://stooq.com/q/d/?s={Uri.EscapeDataString(stooqSymbol)}&i={stooqInterval}",
                HttpMethod.Get);

            if (string.IsNullOrWhiteSpace(response))
            {
                return result;
            }

            var matches = StooqKlineRowRegex.Matches(response);
            foreach (Match match in matches)
            {
                if (!match.Success || match.Groups.Count < 7)
                {
                    continue;
                }

                var timestamp = ParseStooqDate(match.Groups[1].Value);
                var open = ParseDecimalValue(match.Groups[2].Value);
                var high = ParseDecimalValue(match.Groups[3].Value);
                var low = ParseDecimalValue(match.Groups[4].Value);
                var close = ParseDecimalValue(match.Groups[5].Value);
                var volume = ParseLongValue(match.Groups[6].Value);

                result.Add(new StockKline
                {
                    Symbol = NormalizeSymbol(symbol),
                    Open = open,
                    High = high,
                    Low = low,
                    Close = close,
                    Volume = volume,
                    Turnover = 0,
                    Timestamp = timestamp,
                    Period = period
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load kline from Stooq for {Symbol}", symbol);
        }

        return result
            .OrderBy(k => k.Timestamp)
            .TakeLast(Math.Max(count, 1))
            .ToList();
    }

    private async Task<List<StockKline>> GetKlineFromYahooAsync(string symbol, string period, int count)
    {
        var result = new List<StockKline>();
        try
        {
            var normalizedSymbol = NormalizeSymbol(symbol);
            var yahooSymbol = ToYahooSymbol(normalizedSymbol);
            if (string.IsNullOrWhiteSpace(yahooSymbol))
            {
                return result;
            }

            var interval = MapPeriodToYahooInterval(period);
            var range = MapPeriodToYahooRange(period, count);
            var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(yahooSymbol)}?interval={interval}&range={range}&includePrePost=false&events=history";

            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(20)
            };

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/135.0.0.0 Safari/537.36");
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Yahoo kline request failed for {Symbol}: {StatusCode}", normalizedSymbol, response.StatusCode);
                return result;
            }

            var content = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(content))
            {
                return result;
            }

            var json = JObject.Parse(content);
            var errorToken = json["chart"]?["error"];
            if (errorToken is { Type: not JTokenType.Null })
            {
                var description = errorToken["description"]?.ToString() ?? errorToken.ToString(Formatting.None);
                _logger.LogWarning("Yahoo kline returned error for {Symbol}: {Error}", normalizedSymbol, description);
                return result;
            }

            var node = json["chart"]?["result"]?.FirstOrDefault();
            var timestamps = node?["timestamp"] as JArray;
            var quote = node?["indicators"]?["quote"]?.FirstOrDefault();
            var opens = quote?["open"] as JArray;
            var highs = quote?["high"] as JArray;
            var lows = quote?["low"] as JArray;
            var closes = quote?["close"] as JArray;
            var volumes = quote?["volume"] as JArray;

            if (timestamps == null || timestamps.Count == 0 || closes == null)
            {
                return result;
            }

            for (var i = 0; i < timestamps.Count; i++)
            {
                var ts = timestamps[i]?.Value<long?>();
                if (!ts.HasValue || ts.Value <= 0)
                {
                    continue;
                }

                var open = ParseDecimalToken(opens?[i]);
                var high = ParseDecimalToken(highs?[i]);
                var low = ParseDecimalToken(lows?[i]);
                var close = ParseDecimalToken(closes[i]);
                if (close <= 0 && open <= 0)
                {
                    continue;
                }

                if (open <= 0)
                {
                    open = close;
                }

                if (close <= 0)
                {
                    close = open;
                }

                if (high <= 0)
                {
                    high = Math.Max(open, close);
                }

                if (low <= 0)
                {
                    low = Math.Min(open, close);
                }

                result.Add(new StockKline
                {
                    Symbol = normalizedSymbol,
                    Open = open,
                    High = high,
                    Low = low,
                    Close = close,
                    Volume = ParseLongToken(volumes?[i]),
                    Turnover = 0,
                    Timestamp = DateTimeOffset.FromUnixTimeSeconds(ts.Value).UtcDateTime,
                    Period = period
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load kline from Yahoo for {Symbol}", symbol);
        }

        return result
            .OrderBy(k => k.Timestamp)
            .TakeLast(Math.Max(count, 1))
            .ToList();
    }

    private async Task<List<StockKline>> GetKlineFromNasdaqAsync(
        string symbol,
        string period,
        int count,
        DateTime? start = null,
        DateTime? end = null)
    {
        var result = new List<StockKline>();
        try
        {
            var normalizedSymbol = NormalizeSymbol(symbol);
            if (!normalizedSymbol.EndsWith(".US", StringComparison.OrdinalIgnoreCase))
            {
                return result;
            }

            var ticker = normalizedSymbol.Split('.', 2)[0];
            if (string.IsNullOrWhiteSpace(ticker))
            {
                return result;
            }

            var fromDate = (start ?? GuessNasdaqStartDate(period, count)).Date;
            var toDate = (end ?? DateTime.UtcNow).Date;
            if (toDate < fromDate)
            {
                (fromDate, toDate) = (toDate, fromDate);
            }

            var url =
                $"https://api.nasdaq.com/api/quote/{Uri.EscapeDataString(ticker)}/historical" +
                $"?assetclass=stocks&fromdate={fromDate:yyyy-MM-dd}&todate={toDate:yyyy-MM-dd}&limit=5000";

            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(20)
            };

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)");
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Nasdaq kline request failed for {Symbol}: {StatusCode}", normalizedSymbol, response.StatusCode);
                return result;
            }

            var content = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(content))
            {
                return result;
            }

            var json = JObject.Parse(content);
            var rows = json["data"]?["tradesTable"]?["rows"] as JArray;
            if (rows == null || rows.Count == 0)
            {
                return result;
            }

            foreach (var row in rows)
            {
                var dateText = row["date"]?.ToString();
                if (!DateTime.TryParseExact(dateText, "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    continue;
                }

                var open = ParseDecimalWithCurrency(row["open"]?.ToString());
                var high = ParseDecimalWithCurrency(row["high"]?.ToString());
                var low = ParseDecimalWithCurrency(row["low"]?.ToString());
                var close = ParseDecimalWithCurrency(row["close"]?.ToString());
                var volume = ParseLongWithSeparators(row["volume"]?.ToString());

                if (close <= 0 && open <= 0)
                {
                    continue;
                }

                if (open <= 0)
                {
                    open = close;
                }

                if (close <= 0)
                {
                    close = open;
                }

                if (high <= 0)
                {
                    high = Math.Max(open, close);
                }

                if (low <= 0)
                {
                    low = Math.Min(open, close);
                }

                result.Add(new StockKline
                {
                    Symbol = normalizedSymbol,
                    Open = open,
                    High = high,
                    Low = low,
                    Close = close,
                    Volume = volume,
                    Turnover = 0,
                    Timestamp = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc),
                    Period = period
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load kline from Nasdaq for {Symbol}", symbol);
        }

        var ordered = result.OrderBy(k => k.Timestamp).ToList();
        ordered = AggregateDailyKlineByPeriod(ordered, period);
        return ordered
            .TakeLast(Math.Max(count, 1))
            .ToList();
    }

    private static List<StockKline> ApplyKlineRange(
        IEnumerable<StockKline> source,
        DateTime? start,
        DateTime? end,
        int count)
    {
        IEnumerable<StockKline> query = source
            .Where(k => k.Timestamp != default)
            .OrderBy(k => k.Timestamp);

        if (start.HasValue)
        {
            var startUtc = NormalizeToUtc(start.Value);
            query = query.Where(k => k.Timestamp >= startUtc);
        }

        if (end.HasValue)
        {
            var endUtc = NormalizeToUtc(end.Value);
            query = query.Where(k => k.Timestamp <= endUtc);
        }

        var ranged = query.ToList();
        if (count > 0 && ranged.Count > count)
        {
            ranged = ranged.TakeLast(count).ToList();
        }

        return ranged;
    }

    private static DateTime NormalizeToUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    private static DateTime GuessNasdaqStartDate(string period, int count)
    {
        var normalized = (period ?? string.Empty).Trim().ToUpperInvariant();
        var effectiveCount = Math.Max(count, 180);
        return normalized switch
        {
            "W" or "1W" => DateTime.UtcNow.AddDays(-effectiveCount * 10),
            "M" or "1M" => DateTime.UtcNow.AddDays(-effectiveCount * 35),
            "Y" or "1Y" => DateTime.UtcNow.AddDays(-effectiveCount * 370),
            _ => DateTime.UtcNow.AddDays(-Math.Max(effectiveCount * 2, 365))
        };
    }

    private static List<StockKline> AggregateDailyKlineByPeriod(List<StockKline> daily, string period)
    {
        if (daily.Count == 0)
        {
            return daily;
        }

        var normalized = (period ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized is not ("W" or "1W" or "M" or "1M" or "Y" or "1Y"))
        {
            return daily;
        }

        if (normalized is "W" or "1W")
        {
            return daily
                .GroupBy(k => GetWeekStart(k.Timestamp))
                .OrderBy(g => g.Key)
                .Select(g => BuildAggregatedKline(g.ToList(), g.Key, period))
                .ToList();
        }

        if (normalized is "M" or "1M")
        {
            return daily
                .GroupBy(k => new DateTime(k.Timestamp.Year, k.Timestamp.Month, 1, 0, 0, 0, DateTimeKind.Utc))
                .OrderBy(g => g.Key)
                .Select(g => BuildAggregatedKline(g.ToList(), g.Key, period))
                .ToList();
        }

        return daily
            .GroupBy(k => new DateTime(k.Timestamp.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            .OrderBy(g => g.Key)
            .Select(g => BuildAggregatedKline(g.ToList(), g.Key, period))
            .ToList();
    }

    private static DateTime GetWeekStart(DateTime timestamp)
    {
        var date = NormalizeToUtc(timestamp).Date;
        var diff = ((int)date.DayOfWeek + 6) % 7;
        return DateTime.SpecifyKind(date.AddDays(-diff), DateTimeKind.Utc);
    }

    private static StockKline BuildAggregatedKline(List<StockKline> bucket, DateTime timestamp, string period)
    {
        var ordered = bucket.OrderBy(k => k.Timestamp).ToList();
        var open = ordered.First().Open;
        var close = ordered.Last().Close;
        var high = ordered.Max(k => k.High);
        var low = ordered.Min(k => k.Low);
        var volume = ordered.Sum(k => k.Volume);
        var turnover = ordered.Sum(k => k.Turnover);

        return new StockKline
        {
            Symbol = ordered.First().Symbol,
            Open = open,
            High = high,
            Low = low,
            Close = close,
            Volume = volume,
            Turnover = turnover,
            Timestamp = timestamp,
            Period = period
        };
    }

    private static decimal ParseDecimalWithCurrency(string? value)
    {
        var normalized = (value ?? string.Empty)
            .Replace("$", string.Empty, StringComparison.Ordinal)
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Trim();

        if (decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return 0;
    }

    private static long ParseLongWithSeparators(string? value)
    {
        var normalized = (value ?? string.Empty)
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Trim();

        if (long.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return 0;
    }

    private static string ToStooqSymbol(string symbol)
    {
        return NormalizeSymbol(symbol).ToLowerInvariant();
    }

    private static string ToYahooSymbol(string symbol)
    {
        var normalized = NormalizeSymbol(symbol);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var parts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var ticker = parts.FirstOrDefault() ?? string.Empty;
        var market = parts.Length >= 2 ? parts[^1] : "US";

        if (string.IsNullOrWhiteSpace(ticker))
        {
            return string.Empty;
        }

        return market switch
        {
            "US" => ticker,
            "HK" => $"{ticker}.HK",
            "SG" => $"{ticker}.SI",
            "SH" => $"{ticker}.SS",
            "SZ" => $"{ticker}.SZ",
            "CN" => ticker.StartsWith("6", StringComparison.Ordinal) ? $"{ticker}.SS" : $"{ticker}.SZ",
            _ => ticker
        };
    }

    private static string MapPeriodToYahooInterval(string period)
    {
        var raw = (period ?? string.Empty).Trim();
        var lower = raw.ToLowerInvariant();

        if (raw == "1" || raw == "1m")
        {
            return "1m";
        }

        if (raw == "5" || lower == "5m")
        {
            return "5m";
        }

        if (raw == "15" || lower == "15m")
        {
            return "15m";
        }

        if (raw == "30" || lower == "30m")
        {
            return "30m";
        }

        if (raw == "60" || lower == "60m" || lower == "1h")
        {
            return "60m";
        }

        if (raw == "W" || lower == "1w")
        {
            return "1wk";
        }

        if (raw == "Y" || lower == "1y")
        {
            return "1mo";
        }

        if (raw == "M" || raw == "1M" || lower == "1mo")
        {
            return "1mo";
        }

        return "1d";
    }

    private static string MapPeriodToYahooRange(string period, int count)
    {
        var interval = MapPeriodToYahooInterval(period);
        return interval switch
        {
            "1m" => "7d",
            "5m" or "15m" or "30m" => "60d",
            "60m" => "730d",
            "1wk" => "10y",
            "1mo" => "max",
            _ => count switch
            {
                > 1000 => "max",
                > 500 => "10y",
                > 250 => "5y",
                _ => "2y"
            }
        };
    }

    private static string MapPeriodToStooqInterval(string period)
    {
        var normalized = (period ?? string.Empty).Trim().ToUpperInvariant();
        return normalized switch
        {
            "W" or "1W" => "w",
            "M" or "1M" or "Y" or "1Y" => "m",
            _ => "d"
        };
    }

    private static decimal ParseDecimalValue(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().Replace(",", string.Empty);
        if (decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return 0;
    }

    private static long ParseLongValue(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().Replace(",", string.Empty);
        if (long.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return 0;
    }

    private static decimal ParseDecimalToken(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null)
        {
            return 0;
        }

        if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
        {
            return token.Value<decimal>();
        }

        return ParseFlexibleDecimalToken(token.ToString());
    }

    private static long ParseLongToken(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null)
        {
            return 0;
        }

        if (token.Type == JTokenType.Integer)
        {
            return token.Value<long>();
        }

        if (token.Type == JTokenType.Float)
        {
            return Convert.ToInt64(Math.Round(token.Value<double>()));
        }

        return ParseLongValue(token.ToString());
    }

    private static DateTime ParseQuoteTimestamp(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null)
        {
            return UnknownQuoteTimestamp;
        }

        if (!long.TryParse(token.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var raw))
        {
            if (DateTime.TryParse(
                token.ToString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
            {
                return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            }

            return UnknownQuoteTimestamp;
        }

        try
        {
            // LongBridge realtime timestamp can be seconds or milliseconds.
            if (raw > 1_000_000_000_000)
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(raw).UtcDateTime;
            }

            if (raw > 0)
            {
                return DateTimeOffset.FromUnixTimeSeconds(raw).UtcDateTime;
            }
        }
        catch
        {
            return UnknownQuoteTimestamp;
        }

        return UnknownQuoteTimestamp;
    }

    private static DateTime ParseStooqDate(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (DateTime.TryParseExact(
            text,
            ["d MMM yyyy", "dd MMM yyyy", "yyyy-MM-dd"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed))
        {
            return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        }

        return DateTime.UtcNow;
    }

    private static DateTime ParseStooqDateTime(string? date, string? time)
    {
        var text = $"{date?.Trim()} {time?.Trim()}".Trim();
        if (DateTime.TryParseExact(
            text,
            "yyyy-MM-dd HH:mm:ss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed))
        {
            return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        }

        return ParseStooqDate(date);
    }

    private async Task<StockSnapshot> GetStockSnapshotFromStooqAsync(string symbol)
    {
        var normalizedSymbol = NormalizeSymbol(symbol);
        if (string.IsNullOrWhiteSpace(normalizedSymbol))
        {
            return new StockSnapshot();
        }

        if (_stockSnapshotCache.TryGetValue(normalizedSymbol, out var cached)
            && DateTime.UtcNow - cached.FetchedAtUtc < StockSnapshotCacheDuration)
        {
            return cached.Snapshot;
        }

        var snapshot = new StockSnapshot();
        try
        {
            var stooqSymbol = ToStooqSymbol(normalizedSymbol);
            var response = await SendRequestAsync(
                $"https://stooq.com/q/g/?s={Uri.EscapeDataString(stooqSymbol)}",
                HttpMethod.Get);

            if (!string.IsNullOrWhiteSpace(response))
            {
                snapshot = ParseStockSnapshotFromSummary(response);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load stock summary from Stooq for {Symbol}", normalizedSymbol);
        }

        _stockSnapshotCache[normalizedSymbol] = new CachedStockSnapshot(DateTime.UtcNow, snapshot);
        return snapshot;
    }

    private static StockSnapshot ParseStockSnapshotFromSummary(string html)
    {
        var snapshot = new StockSnapshot();
        if (string.IsNullOrWhiteSpace(html))
        {
            return snapshot;
        }

        var matches = StooqSummaryRowRegex.Matches(html);
        foreach (Match match in matches)
        {
            if (!match.Success)
            {
                continue;
            }

            var keyRaw = match.Groups["key"].Value;
            var valueRaw = match.Groups["value"].Value;

            var key = NormalizeKey(keyRaw);
            var valueText = StripHtml(valueRaw);
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(valueText))
            {
                continue;
            }

            if ((key.Contains("poprzedni") || key.Contains("prev")) && snapshot.PreviousClose <= 0)
            {
                snapshot.PreviousClose = ParseFirstDecimalValue(valueText);
                continue;
            }

            if ((key.Contains("obrot") || key.Contains("turnover")) && snapshot.Turnover <= 0)
            {
                snapshot.Turnover = ParseScaledDecimalValue(valueText);
                continue;
            }

            if ((key.Contains("kapitalizacja") || key.Contains("marketcap")) && snapshot.MarketCap <= 0)
            {
                snapshot.MarketCap = ParseScaledDecimalValue(valueText);
                continue;
            }

            if ((key == "cz" || key.StartsWith("cz") || key.Contains("pe") || key.Contains("priceearnings")) && snapshot.Pe <= 0)
            {
                snapshot.Pe = ParseFirstDecimalValue(valueText);
                continue;
            }

            if (key.StartsWith("eps") && snapshot.Eps <= 0)
            {
                snapshot.Eps = ParseFirstDecimalValue(valueText);
                continue;
            }

            if ((key.Contains("stopadywidendy") || key.Contains("dividendyield")) && snapshot.DividendYield <= 0)
            {
                snapshot.DividendYield = ParsePercentValue(valueText);
                continue;
            }

            if ((key.Contains("maxmin52t") || key.Contains("52weekhighlow") || key.Contains("52whighlow"))
                && (snapshot.High52Week <= 0 || snapshot.Low52Week <= 0))
            {
                if (TryParsePricePair(valueText, out var high52, out var low52))
                {
                    snapshot.High52Week = high52;
                    snapshot.Low52Week = low52;
                }
                continue;
            }

            if ((key.Contains("sredniwol3m") || key.Contains("averagevolume3m") || key.Contains("avgvol3m"))
                && snapshot.AvgVolume <= 0)
            {
                snapshot.AvgVolume = ParseLongValue(valueText);
            }
        }

        return snapshot;
    }

    private static string StripHtml(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decoded = WebUtility.HtmlDecode(value);
        var noTags = HtmlTagRegex.Replace(decoded, " ");
        return Regex.Replace(noTags, "\\s+", " ").Trim();
    }

    private static string NormalizeKey(string? value)
    {
        var text = StripHtml(value).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    private static decimal ParseScaledDecimalValue(string? value)
    {
        var text = StripHtml(value).ToLowerInvariant().Replace('\u00a0', ' ');
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var numberMatch = NumberTokenRegex.Match(text);
        if (!numberMatch.Success)
        {
            return 0;
        }

        var number = ParseFlexibleDecimalToken(numberMatch.Value);
        var unitText = text[(numberMatch.Index + numberMatch.Length)..].Trim();
        var compactUnit = unitText.Replace(" ", string.Empty);

        decimal multiplier = 1;
        if (compactUnit.StartsWith("bln", StringComparison.OrdinalIgnoreCase)
            || compactUnit.StartsWith("tryl", StringComparison.OrdinalIgnoreCase)
            || compactUnit.StartsWith("trl", StringComparison.OrdinalIgnoreCase)
            || compactUnit.StartsWith("trn", StringComparison.OrdinalIgnoreCase))
        {
            multiplier = 1_000_000_000_000m;
        }
        else if (compactUnit.StartsWith("mld", StringComparison.OrdinalIgnoreCase)
            || compactUnit.StartsWith("bil", StringComparison.OrdinalIgnoreCase)
            || compactUnit == "g")
        {
            multiplier = 1_000_000_000m;
        }
        else if (compactUnit.StartsWith("mln", StringComparison.OrdinalIgnoreCase)
            || compactUnit == "m")
        {
            multiplier = 1_000_000m;
        }
        else if (compactUnit.StartsWith("tys", StringComparison.OrdinalIgnoreCase)
            || compactUnit == "k")
        {
            multiplier = 1_000m;
        }

        return number * multiplier;
    }

    private static decimal ParsePercentValue(string? value)
    {
        return ParseFirstDecimalValue(value);
    }

    private static decimal ParseFirstDecimalValue(string? value)
    {
        var text = StripHtml(value);
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var numberMatch = NumberTokenRegex.Match(text);
        if (!numberMatch.Success)
        {
            return 0;
        }

        return ParseFlexibleDecimalToken(numberMatch.Value);
    }

    private static decimal ParseFlexibleDecimalToken(string token)
    {
        var normalized = (token ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return 0;
        }

        if (normalized.Contains(',') && normalized.Contains('.'))
        {
            normalized = normalized.Replace(",", string.Empty);
        }
        else if (normalized.Contains(','))
        {
            if (Regex.IsMatch(normalized, "^\\d{1,3}(,\\d{3})+$"))
            {
                normalized = normalized.Replace(",", string.Empty);
            }
            else
            {
                normalized = normalized.Replace(',', '.');
            }
        }

        if (decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return 0;
    }

    private static bool TryParsePricePair(string value, out decimal first, out decimal second)
    {
        first = 0;
        second = 0;

        var text = StripHtml(value);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var pairMatch = PairNumberRegex.Match(text);
        if (pairMatch.Success && pairMatch.Groups.Count >= 3)
        {
            first = ParseFlexibleDecimalToken(pairMatch.Groups[1].Value);
            second = ParseFlexibleDecimalToken(pairMatch.Groups[2].Value);
            return first > 0 || second > 0;
        }

        var numberMatches = NumberTokenRegex.Matches(text);
        if (numberMatches.Count >= 2)
        {
            first = ParseFlexibleDecimalToken(numberMatches[0].Value);
            second = ParseFlexibleDecimalToken(numberMatches[1].Value);
            return first > 0 || second > 0;
        }

        return false;
    }

    public async Task<string?> PlaceOrderAsync(string symbol, string side, string orderType, decimal quantity, decimal? price = null)
    {
        var order = new
        {
            symbol = symbol.ToUpper(),
            side = side.ToUpper(),
            order_type = orderType == "market" ? "MO" : "LO",
            submitted_quantity = quantity.ToString("F0"),
            submitted_price = price?.ToString("F2"),
            time_in_force = "Day"
        };
        
        var response = await SendRequestAsync("/v1/trade/order", HttpMethod.Post, order);
        
        if (string.IsNullOrEmpty(response))
            return null;
        
        try
        {
            var json = JObject.Parse(response);
            return json["data"]?["order_id"]?.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing place order response");
            return null;
        }
    }

    public async Task<bool> CancelOrderAsync(string orderId)
    {
        var response = await SendRequestAsync($"/v1/trade/order/{orderId}", HttpMethod.Delete);
        return !string.IsNullOrEmpty(response);
    }

    public async Task<Trade?> GetOrderAsync(string orderId)
    {
        var orders = await GetOrdersAsync();
        return orders.FirstOrDefault(o => o.OrderId == orderId);
    }

    public async Task<List<Trade>> GetOrdersAsync(string? status = null, DateTime? startDate = null, DateTime? endDate = null)
    {
        var result = new List<Trade>();
        
        var url = "/v1/trade/order/history?";
        if (!string.IsNullOrEmpty(status))
            url += $"status={status}&";
        if (startDate.HasValue)
            url += $"start_at={new DateTimeOffset(startDate.Value).ToUnixTimeSeconds()}&";
        if (endDate.HasValue)
            url += $"end_at={new DateTimeOffset(endDate.Value).ToUnixTimeSeconds()}&";
        
        var response = await SendRequestAsync(url.TrimEnd('&', '?'), HttpMethod.Get);
        
        if (string.IsNullOrEmpty(response))
            return result;
        
        try
        {
            var json = JObject.Parse(response);
            var data = json["data"]?["orders"] as JArray;
            
            if (data != null)
            {
                foreach (var item in data)
                {
                    result.Add(ParseTrade(item));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing orders response");
        }
        
        return result;
    }

    public async Task<List<Trade>> GetTodayOrdersAsync()
    {
        var result = new List<Trade>();
        
        var response = await SendRequestAsync("/v1/trade/order/today", HttpMethod.Get);
        
        if (string.IsNullOrEmpty(response))
            return result;
        
        try
        {
            var json = JObject.Parse(response);
            var data = json["data"]?["orders"] as JArray;
            
            if (data != null)
            {
                foreach (var item in data)
                {
                    result.Add(ParseTrade(item));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing today orders response");
        }
        
        return result;
    }

    private Trade ParseTrade(JToken item)
    {
        return new Trade
        {
            OrderId = item["order_id"]?.ToString() ?? "",
            Symbol = item["symbol"]?.ToString() ?? "",
            Side = item["side"]?.ToString()?.ToLower() ?? "",
            OrderType = item["order_type"]?.ToString() == "MO" ? "market" : "limit",
            Quantity = item["quantity"]?.Value<decimal>() ?? 0,
            Price = item["price"]?.Value<decimal>(),
            FilledQuantity = item["executed_quantity"]?.Value<decimal>(),
            FilledPrice = item["executed_price"]?.Value<decimal>(),
            Status = MapOrderStatus(item["status"]?.ToString() ?? ""),
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(item["submitted_at"]?.Value<long>() ?? 0).UtcDateTime
        };
    }

    private string MapOrderStatus(string lbStatus)
    {
        return lbStatus switch
        {
            "NotReported" or "ReplacedNotReported" or "ProtectedNotReported" => "pending",
            "Filled" => "filled",
            "PartialFilled" => "partial",
            "Cancelled" or "Rejected" or "Expired" => "cancelled",
            _ => "pending"
        };
    }

    public async Task<Account?> GetAccountAsync()
    {
        var response = await SendRequestAsync("/v1/asset/account", HttpMethod.Get);
        
        if (string.IsNullOrEmpty(response))
            return null;
        
        try
        {
            var json = JObject.Parse(response);
            var data = json["data"]?["list"]?[0];
            
            if (data != null)
            {
                return new Account
                {
                    TotalAssets = data["total_cash"]?.Value<decimal>() ?? 0,
                    Cash = data["cash_available"]?.Value<decimal>() ?? 0,
                    MarketValue = data["market_value"]?.Value<decimal>() ?? 0,
                    Currency = data["currency"]?.ToString() ?? "USD",
                    LastUpdated = DateTime.UtcNow
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing account response");
        }
        
        return null;
    }

    public async Task<List<Position>> GetPositionsAsync()
    {
        var result = new List<Position>();
        
        var response = await SendRequestAsync("/v1/asset/stock", HttpMethod.Get);
        
        if (string.IsNullOrEmpty(response))
            return result;
        
        try
        {
            var json = JObject.Parse(response);
            var data = json["data"]?["list"] as JArray;
            
            if (data != null)
            {
                foreach (var item in data)
                {
                    var quantity = item["quantity"]?.Value<decimal>() ?? 0;
                    var avgPrice = item["cost_price"]?.Value<decimal>() ?? 0;
                    var currentPrice = item["market_price"]?.Value<decimal>() ?? 0;
                    var marketValue = quantity * currentPrice;
                    var unrealizedPnL = marketValue - (quantity * avgPrice);
                    var unrealizedPnLPercent = avgPrice > 0 ? (currentPrice - avgPrice) / avgPrice * 100 : 0;
                    
                    result.Add(new Position
                    {
                        Symbol = item["symbol"]?.ToString() ?? "",
                        Quantity = quantity,
                        AveragePrice = avgPrice,
                        CurrentPrice = currentPrice,
                        MarketValue = marketValue,
                        UnrealizedPnL = unrealizedPnL,
                        UnrealizedPnLPercent = unrealizedPnLPercent,
                        LastUpdated = DateTime.UtcNow
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing positions response");
        }
        
        return result;
    }

    public async Task<decimal> GetCashAsync()
    {
        var account = await GetAccountAsync();
        return account?.Cash ?? 0;
    }

    public Task<bool> IsMarketOpenAsync(string market = "US")
    {
        var marketCode = NormalizeMarketCode(market);
        var nowUtc = DateTime.UtcNow;

        TimeZoneInfo timezone;
        (TimeSpan Start, TimeSpan End)[] sessions;

        switch (marketCode)
        {
            case "HK":
                timezone = ResolveTimeZone("Asia/Hong_Kong", "Hong Kong Standard Time", "Asia/Shanghai", "China Standard Time");
                sessions =
                [
                    (new TimeSpan(9, 30, 0), new TimeSpan(12, 0, 0)),
                    (new TimeSpan(13, 0, 0), new TimeSpan(16, 0, 0))
                ];
                break;
            case "SH":
            case "SZ":
                timezone = ResolveTimeZone("Asia/Shanghai", "China Standard Time");
                sessions =
                [
                    (new TimeSpan(9, 30, 0), new TimeSpan(11, 30, 0)),
                    (new TimeSpan(13, 0, 0), new TimeSpan(15, 0, 0))
                ];
                break;
            case "SG":
                timezone = ResolveTimeZone("Asia/Singapore", "Singapore Standard Time");
                sessions = [(new TimeSpan(9, 0, 0), new TimeSpan(17, 0, 0))];
                break;
            case "US":
            default:
                timezone = ResolveTimeZone("America/New_York", "Eastern Standard Time");
                sessions = [(new TimeSpan(9, 30, 0), new TimeSpan(16, 0, 0))];
                break;
        }

        var now = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, timezone);
        if (now.DayOfWeek == DayOfWeek.Saturday || now.DayOfWeek == DayOfWeek.Sunday)
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(sessions.Any(session => now.TimeOfDay >= session.Start && now.TimeOfDay <= session.End));
    }

    public Task<DateTime?> GetNextMarketOpenAsync(string market = "US")
    {
        var et = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, et);
        
        var nextOpen = now.Date.AddHours(9).AddMinutes(30);
        
        if (now.TimeOfDay > new TimeSpan(16, 0, 0))
        {
            nextOpen = nextOpen.AddDays(1);
        }
        
        while (nextOpen.DayOfWeek == DayOfWeek.Saturday || nextOpen.DayOfWeek == DayOfWeek.Sunday)
        {
            nextOpen = nextOpen.AddDays(1);
        }
        
        return Task.FromResult<DateTime?>(TimeZoneInfo.ConvertTimeToUtc(nextOpen, et));
    }

    private static string NormalizeMarketCode(string? market)
    {
        var normalized = (market ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "US";
        }

        if (normalized.StartsWith("SH", StringComparison.OrdinalIgnoreCase))
        {
            return "SH";
        }

        if (normalized.StartsWith("SZ", StringComparison.OrdinalIgnoreCase))
        {
            return "SZ";
        }

        return normalized;
    }

    private static TimeZoneInfo ResolveTimeZone(params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(candidate);
            }
            catch
            {
                // Try next timezone candidate.
            }
        }

        return TimeZoneInfo.Utc;
    }

    private sealed record LongBridgeRuntimeConfig
    {
        public string BaseUrl { get; init; } = string.Empty;
        public string AppKey { get; init; } = string.Empty;
        public string AppSecret { get; init; } = string.Empty;
        public string AccessToken { get; init; } = string.Empty;
        public bool ProxyEnabled { get; init; }
        public string ProxyHost { get; init; } = string.Empty;
        public int ProxyPort { get; init; }
        public string ProxyUsername { get; init; } = string.Empty;
        public string ProxyPassword { get; init; } = string.Empty;
    }

    private sealed record CachedSecurityList(DateTime FetchedAtUtc, List<Stock> Securities);
    private sealed class StockSnapshot
    {
        public decimal PreviousClose { get; set; }
        public decimal Turnover { get; set; }
        public decimal MarketCap { get; set; }
        public decimal Pe { get; set; }
        public decimal Eps { get; set; }
        public decimal DividendYield { get; set; }
        public decimal High52Week { get; set; }
        public decimal Low52Week { get; set; }
        public long AvgVolume { get; set; }
    }

    private sealed class StaticSecurityInfo
    {
        public string Symbol { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Market { get; set; } = string.Empty;
        public string Currency { get; set; } = string.Empty;
        public long TotalShares { get; set; }
        public long CirculatingShares { get; set; }
        public decimal Eps { get; set; }
        public decimal EpsTtm { get; set; }
        public decimal DividendYield { get; set; }
    }

    private sealed class EastMoneyQuoteSnapshot
    {
        public string Symbol { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal PreviousClose { get; set; }
        public decimal Change { get; set; }
        public decimal ChangePercent { get; set; }
        public long Volume { get; set; }
        public decimal Turnover { get; set; }
        public decimal MarketCap { get; set; }
        public decimal Pe { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    private sealed class MarkdownCompanyProfile
    {
        public string Title { get; init; } = string.Empty;
        public string Overview { get; init; } = string.Empty;
        public string SourceUrl { get; init; } = string.Empty;
        public List<KeyValuePair<string, string>> Fields { get; init; } = new();
    }

    private sealed record CachedStockSnapshot(DateTime FetchedAtUtc, StockSnapshot Snapshot);
    private sealed record CachedCompanyProfile(DateTime FetchedAtUtc, LongBridgeCompanyProfile Profile);
    private sealed record LongBridgeApiCallResult(bool Success, string? Content, string? ErrorMessage);
}
