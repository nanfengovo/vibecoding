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

    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LongBridgeService> _logger;
    private readonly SemaphoreSlim _securityListCacheLock = new(1, 1);
    private readonly Dictionary<string, CachedSecurityList> _securityListCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CachedStockSnapshot> _stockSnapshotCache = new(StringComparer.OrdinalIgnoreCase);

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
            BaseAddress = new Uri(config.BaseUrl)
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
        _logger.LogError("LongBridge API error: {StatusCode} - {Content}", response.StatusCode, content);
        return new LongBridgeApiCallResult(false, null, upstreamMessage);
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

        var quotes = await GetQuotesInternalAsync(new List<string> { normalizedSymbol }, allowFallback: true);
        var quote = quotes.FirstOrDefault();
        if (quote == null)
        {
            return null;
        }

        if (quote.PreviousClose <= 0 || quote.Turnover <= 0)
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

        var quotes = await GetQuotesInternalAsync(new List<string> { normalizedSymbol }, allowFallback: false);
        var quote = quotes.FirstOrDefault();
        if (quote == null || quote.Price <= 0 || quote.Timestamp <= DateTime.UnixEpoch)
        {
            return null;
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
        var response = await SendRequestAsync($"/v1/quote/realtime?{symbolsQuery}", HttpMethod.Get);

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

        if (!allowFallback)
        {
            return result;
        }

        var missingSymbols = normalizedSymbols
            .Where(s => !result.Any(q => SymbolEquals(q.Symbol, s)))
            .ToList();

        foreach (var missing in missingSymbols)
        {
            var fallbackQuote = await GetQuoteFromPublicFallbacksAsync(missing);
            if (fallbackQuote != null)
            {
                result.Add(fallbackQuote);
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
                Name = normalizedSymbol.Split('.').FirstOrDefault() ?? normalizedSymbol,
                Market = InferMarketFromSymbol(normalizedSymbol)
            };
        }

        var resolvedSymbol = NormalizeSymbol(stockMeta.Symbol);

        var quote = await GetQuoteAsync(resolvedSymbol);
        var dailyKlines = await GetKlineAsync(resolvedSymbol, "D", count: 260);
        var snapshot = await GetStockSnapshotFromStooqAsync(resolvedSymbol);

        var orderedKlines = dailyKlines
            .Where(k => k.Timestamp != default)
            .OrderBy(k => k.Timestamp)
            .ToList();

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
        var open = quote?.Open ?? latestKline?.Open ?? 0;
        var high = quote?.High ?? latestKline?.High ?? 0;
        var low = quote?.Low ?? latestKline?.Low ?? 0;
        var volume = quote?.Volume ?? latestKline?.Volume ?? 0;

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

        var hasValidMarketData = currentPrice > 0
            || previousClose > 0
            || open > 0
            || high > 0
            || low > 0
            || volume > 0
            || high52Week > 0
            || low52Week > 0
            || avgVolume > 0
            || snapshot.MarketCap > 0;

        if (!hasValidMarketData)
        {
            _logger.LogWarning("No valid market data found for symbol {Symbol}", resolvedSymbol);
            return null;
        }

        return new Stock
        {
            Symbol = resolvedSymbol,
            Name = string.IsNullOrWhiteSpace(stockMeta.Name) ? resolvedSymbol : stockMeta.Name,
            Market = string.IsNullOrWhiteSpace(stockMeta.Market) ? InferMarketFromSymbol(resolvedSymbol) : stockMeta.Market,
            CurrentPrice = currentPrice,
            PreviousClose = previousClose,
            Open = open,
            High = high,
            Low = low,
            Volume = volume,
            Change = change,
            ChangePercent = changePercent,
            MarketCap = snapshot.MarketCap,
            High52Week = high52Week,
            Low52Week = low52Week,
            AvgVolume = avgVolume,
            Pe = snapshot.Pe,
            Eps = snapshot.Eps,
            Dividend = snapshot.DividendYield,
            LastUpdated = DateTime.UtcNow
        };
    }

    public async Task<List<Stock>> SearchStocksAsync(string keyword)
    {
        var normalizedKeyword = (keyword ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedKeyword))
        {
            return new List<Stock>();
        }

        var allCandidates = new List<Stock>();
        foreach (var market in GetCandidateMarkets(normalizedKeyword))
        {
            var securities = await GetMarketSecuritiesAsync(market);
            if (securities.Count > 0)
            {
                allCandidates.AddRange(securities);
            }
        }

        var results = allCandidates
            .Where(s => IsKeywordMatch(s, normalizedKeyword))
            .GroupBy(s => s.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(s => GetMatchScore(s, normalizedKeyword))
            .ThenBy(s => s.Symbol)
            .Take(20)
            .ToList();

        if (results.Count > 0)
        {
            return results;
        }

        return new List<Stock>();
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
        try
        {
            var response = await SendRequestAsync($"/v1/quote/get_security_list?market={market}&category=Overnight", HttpMethod.Get);
            if (string.IsNullOrWhiteSpace(response))
            {
                return result;
            }

            var json = JObject.Parse(response);
            var list = json["data"]?["list"] as JArray
                ?? json["data"] as JArray
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
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load security list for market {Market}", market);
        }

        return result;
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

    private sealed record CachedStockSnapshot(DateTime FetchedAtUtc, StockSnapshot Snapshot);
    private sealed record LongBridgeApiCallResult(bool Success, string? Content, string? ErrorMessage);
}
