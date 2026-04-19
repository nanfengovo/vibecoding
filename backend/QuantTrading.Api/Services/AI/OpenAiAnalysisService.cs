using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using QuantTrading.Api.Data;

namespace QuantTrading.Api.Services.AI;

public sealed class OpenAiAnalysisService : IAiAnalysisService
{
    private readonly QuantTradingDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OpenAiAnalysisService> _logger;

    public OpenAiAnalysisService(
        QuantTradingDbContext dbContext,
        IConfiguration configuration,
        ILogger<OpenAiAnalysisService> logger)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<AiConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var runtime = await GetRuntimeConfigAsync(cancellationToken);
        if (!runtime.Enabled)
        {
            return new AiConnectionTestResult
            {
                Success = false,
                Message = "OpenAI 分析未启用，请先在系统设置中开启。"
            };
        }

        if (string.IsNullOrWhiteSpace(runtime.ApiKey))
        {
            return new AiConnectionTestResult
            {
                Success = false,
                Message = "OpenAI API Key 未配置。"
            };
        }

        var (success, content, error) = await SendChatCompletionAsync(
            runtime,
            "你是交易分析助手。请只回复“连接成功”。",
            "回复连接成功",
            cancellationToken);

        if (!success)
        {
            return new AiConnectionTestResult
            {
                Success = false,
                Message = string.IsNullOrWhiteSpace(error) ? "OpenAI 连接测试失败。" : error
            };
        }

        return new AiConnectionTestResult
        {
            Success = true,
            Message = string.IsNullOrWhiteSpace(content) ? "OpenAI 连接成功。" : $"OpenAI 连接成功：{content.Trim()}"
        };
    }

    public async Task<StockAnalysisResult> AnalyzeStockAsync(
        StockAnalysisInput input,
        CancellationToken cancellationToken = default)
    {
        var runtime = await GetRuntimeConfigAsync(cancellationToken);
        if (!runtime.Enabled)
        {
            throw new InvalidOperationException("OpenAI 分析未启用，请先在设置中开启。");
        }

        if (string.IsNullOrWhiteSpace(runtime.ApiKey))
        {
            throw new InvalidOperationException("OpenAI API Key 未配置。");
        }

        var systemPrompt = "你是专业股票分析助手。输出必须简洁、结构化、可执行，使用中文。不要提供确定性收益承诺。";
        var userPrompt = BuildStockPrompt(input);

        var (success, content, error) = await SendChatCompletionAsync(
            runtime,
            systemPrompt,
            userPrompt,
            cancellationToken);

        if (!success || string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error) ? "OpenAI 分析失败，请稍后重试。" : error);
        }

        return new StockAnalysisResult
        {
            Symbol = input.Symbol,
            Model = runtime.Model,
            Analysis = content.Trim(),
            GeneratedAt = DateTime.UtcNow
        };
    }

    private async Task<(bool Success, string Content, string Error)> SendChatCompletionAsync(
        OpenAiRuntimeConfig runtime,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(45),
                BaseAddress = new Uri(runtime.BaseUrl.TrimEnd('/') + "/")
            };

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", runtime.ApiKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var payload = new
            {
                model = runtime.Model,
                temperature = 0.3,
                max_tokens = 900,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                }
            };

            var request = new StringContent(
                JsonConvert.SerializeObject(payload),
                Encoding.UTF8,
                "application/json");

            var response = await client.PostAsync("chat/completions", request, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorText = TryReadOpenAiError(raw);
                _logger.LogWarning("OpenAI API error: {StatusCode} - {Error}", response.StatusCode, errorText);
                return (false, string.Empty, $"OpenAI 请求失败：{errorText}");
            }

            var json = JObject.Parse(raw);
            var content = json["choices"]?.FirstOrDefault()?["message"]?["content"]?.ToString() ?? string.Empty;
            return (true, content, string.Empty);
        }
        catch (TaskCanceledException)
        {
            return (false, string.Empty, "OpenAI 请求超时，请稍后重试。");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI analysis request failed");
            return (false, string.Empty, $"OpenAI 调用异常：{ex.Message}");
        }
    }

    private static string TryReadOpenAiError(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "未知错误";
        }

        try
        {
            var json = JObject.Parse(raw);
            var msg = json["error"]?["message"]?.ToString();
            if (!string.IsNullOrWhiteSpace(msg))
            {
                return msg;
            }
        }
        catch
        {
            // ignore parse error and fallback to raw text
        }

        return raw;
    }

    private static string BuildStockPrompt(StockAnalysisInput input)
    {
        var stock = input.Stock;
        var quote = input.Quote;
        var klines = input.Klines
            .OrderBy(k => k.Timestamp)
            .TakeLast(90)
            .ToList();

        var latest = klines.LastOrDefault();
        var first = klines.FirstOrDefault();
        var upDays = 0;
        var downDays = 0;
        for (var i = 1; i < klines.Count; i++)
        {
            if (klines[i].Close >= klines[i - 1].Close)
            {
                upDays++;
            }
            else
            {
                downDays++;
            }
        }

        decimal momentum = 0;
        if (first != null && first.Close > 0 && latest != null)
        {
            momentum = (latest.Close - first.Close) / first.Close * 100;
        }

        var recentPoints = string.Join(
            ", ",
            klines.TakeLast(12).Select(k =>
                $"{k.Timestamp:MM-dd}:{k.Close.ToString("F2", CultureInfo.InvariantCulture)}"));

        var focus = string.IsNullOrWhiteSpace(input.Focus) ? "短中期趋势与风险控制" : input.Focus.Trim();

        var sb = new StringBuilder();
        sb.AppendLine($"标的：{input.Symbol}");
        sb.AppendLine($"分析关注点：{focus}");
        sb.AppendLine($"当前价：{quote?.Price:F2}，涨跌幅：{quote?.ChangePercent:F2}%");
        sb.AppendLine($"开高低收：{quote?.Open:F2}/{quote?.High:F2}/{quote?.Low:F2}/{quote?.Price:F2}");
        sb.AppendLine($"成交量：{quote?.Volume:N0}，成交额：{quote?.Turnover:F2}");
        sb.AppendLine($"市值：{stock?.MarketCap:F2}，PE：{stock?.Pe:F2}，EPS：{stock?.Eps:F2}");
        sb.AppendLine($"52周高低：{stock?.High52Week:F2}/{stock?.Low52Week:F2}");
        sb.AppendLine($"近90根方向统计：上涨{upDays}，下跌{downDays}，区间动量{momentum:F2}%");
        sb.AppendLine($"最近12个收盘点：{recentPoints}");
        sb.AppendLine();
        sb.AppendLine("请按以下结构输出：");
        sb.AppendLine("1) 趋势判断（多/空/震荡 + 依据）");
        sb.AppendLine("2) 关键价位（支撑/压力）");
        sb.AppendLine("3) 风险提示（至少2条）");
        sb.AppendLine("4) 交易计划示例（保守/激进各1条，含止损与仓位建议）");
        sb.AppendLine("5) 结论（一句话）");
        sb.AppendLine("注意：不构成投资建议。");
        return sb.ToString();
    }

    private async Task<OpenAiRuntimeConfig> GetRuntimeConfigAsync(CancellationToken cancellationToken)
    {
        var openAiConfigs = await _dbContext.SystemConfigs
            .Where(c => c.Category == "openai")
            .ToListAsync(cancellationToken);

        var map = openAiConfigs.ToDictionary(c => c.Key, c => c.Value, StringComparer.OrdinalIgnoreCase);
        return new OpenAiRuntimeConfig
        {
            Enabled = ParseBool(GetValue(map, "Enabled", _configuration["OpenAI:Enabled"]), false),
            ApiKey = GetValue(map, "ApiKey", _configuration["OpenAI:ApiKey"]),
            BaseUrl = GetValue(map, "BaseUrl", _configuration["OpenAI:BaseUrl"], "https://api.openai.com/v1"),
            Model = GetValue(map, "Model", _configuration["OpenAI:Model"], "gpt-4o-mini")
        };
    }

    private static string GetValue(
        IReadOnlyDictionary<string, string> values,
        string key,
        string? fallback = null,
        string defaultValue = "")
    {
        if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return fallback.Trim();
        }

        return defaultValue;
    }

    private static bool ParseBool(string? value, bool fallback)
    {
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private sealed class OpenAiRuntimeConfig
    {
        public bool Enabled { get; init; }
        public string ApiKey { get; init; } = string.Empty;
        public string BaseUrl { get; init; } = "https://api.openai.com/v1";
        public string Model { get; init; } = "gpt-4o-mini";
    }
}
