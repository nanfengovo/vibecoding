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
    private const string MaskedValue = "******";
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

        var provider = ResolveProvider(runtime, runtime.ActiveProviderId);
        if (string.IsNullOrWhiteSpace(provider.ApiKey))
        {
            return new AiConnectionTestResult
            {
                Success = false,
                Message = "默认模型源未配置 API Key。"
            };
        }

        var (success, content, error, _) = await SendChatCompletionAsync(
            provider,
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
            Message = string.IsNullOrWhiteSpace(content)
                ? $"连接成功（{provider.Name}）"
                : $"{provider.Name} 连接成功：{content.Trim()}"
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

        var provider = ResolveProvider(runtime, input.ProviderId);
        if (string.IsNullOrWhiteSpace(provider.ApiKey))
        {
            throw new InvalidOperationException($"模型源“{provider.Name}”未配置 API Key。");
        }

        var systemPrompt = "你是专业股票分析助手。输出必须简洁、结构化、可执行，使用中文。不要提供确定性收益承诺。";
        var userPrompt = BuildStockPrompt(input);

        var modelOverride = string.IsNullOrWhiteSpace(input.Model) ? null : input.Model.Trim();
        var (success, content, error, modelUsed) = await SendChatCompletionAsync(
            provider,
            systemPrompt,
            userPrompt,
            cancellationToken,
            modelOverride);

        if (!success || string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error) ? "OpenAI 分析失败，请稍后重试。" : error);
        }

        return new StockAnalysisResult
        {
            Symbol = input.Symbol,
            Model = string.IsNullOrWhiteSpace(modelUsed) ? provider.Model : modelUsed,
            Analysis = content.Trim(),
            GeneratedAt = DateTime.UtcNow
        };
    }

    public async Task<AiChatResult> ChatAsync(
        AiChatInput input,
        CancellationToken cancellationToken = default)
    {
        var runtime = await GetRuntimeConfigAsync(cancellationToken);
        if (!runtime.Enabled)
        {
            throw new InvalidOperationException("OpenAI 分析未启用，请先在设置中开启。");
        }

        var provider = ResolveProvider(runtime, input.ProviderId);
        if (string.IsNullOrWhiteSpace(provider.ApiKey))
        {
            throw new InvalidOperationException($"模型源“{provider.Name}”未配置 API Key。");
        }

        var question = (input.Question ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(question))
        {
            throw new InvalidOperationException("问题不能为空。");
        }

        var systemPrompt = "你是专业交易助手。请给出结构化、简洁、可执行的中文回答，并包含风险提示。";
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(input.Symbol))
        {
            sb.AppendLine($"标的：{input.Symbol.Trim().ToUpperInvariant()}");
        }
        if (!string.IsNullOrWhiteSpace(input.Focus))
        {
            sb.AppendLine($"关注点：{input.Focus.Trim()}");
        }
        sb.AppendLine($"用户问题：{question}");
        sb.AppendLine();
        sb.AppendLine("请按“结论 / 依据 / 风险提示”结构回复。");
        sb.AppendLine("注意：不构成投资建议。");

        var modelOverride = (input.Model ?? string.Empty).Trim();
        var (success, content, error, modelUsed) = await SendChatCompletionAsync(
            provider,
            systemPrompt,
            sb.ToString(),
            cancellationToken,
            modelOverride);

        if (!success || string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error) ? "AI 聊天调用失败，请稍后重试。" : error);
        }

        return new AiChatResult
        {
            Model = string.IsNullOrWhiteSpace(modelUsed)
                ? (string.IsNullOrWhiteSpace(modelOverride) ? provider.Model : modelOverride)
                : modelUsed,
            Content = content.Trim(),
            GeneratedAt = DateTime.UtcNow
        };
    }

    public async Task<AiPromptOptimizeResult> OptimizePromptAsync(
        AiPromptOptimizeInput input,
        CancellationToken cancellationToken = default)
    {
        var runtime = await GetRuntimeConfigAsync(cancellationToken);
        if (!runtime.Enabled)
        {
            throw new InvalidOperationException("OpenAI 分析未启用，请先在设置中开启。");
        }

        var provider = ResolveProvider(runtime, input.ProviderId);
        if (string.IsNullOrWhiteSpace(provider.ApiKey))
        {
            throw new InvalidOperationException($"模型源“{provider.Name}”未配置 API Key。");
        }

        var question = (input.Question ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(question))
        {
            throw new InvalidOperationException("待优化问题不能为空。");
        }

        var systemPrompt = "你是专业交易提示词优化助手。你的任务是重写用户问题，让模型能更精准输出结构化交易分析。";
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(input.Symbol))
        {
            sb.AppendLine($"标的：{input.Symbol.Trim().ToUpperInvariant()}");
        }
        sb.AppendLine($"原始问题：{question}");
        sb.AppendLine();
        sb.AppendLine("请输出“优化后的最终提问”一段文本，不要解释，不要加标题，不要使用 markdown 代码块。");
        sb.AppendLine("要求：");
        sb.AppendLine("1) 保留原问题意图，不改变交易方向。");
        sb.AppendLine("2) 补充分析维度：趋势、关键价位、交易计划、风险控制。");
        sb.AppendLine("3) 语气专业、简洁、可执行。");

        var modelOverride = (input.Model ?? string.Empty).Trim();
        var (success, content, error, modelUsed) = await SendChatCompletionAsync(
            provider,
            systemPrompt,
            sb.ToString(),
            cancellationToken,
            modelOverride);

        if (!success || string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error) ? "提示词优化失败，请稍后重试。" : error);
        }

        return new AiPromptOptimizeResult
        {
            Model = string.IsNullOrWhiteSpace(modelUsed)
                ? (string.IsNullOrWhiteSpace(modelOverride) ? provider.Model : modelOverride)
                : modelUsed,
            OptimizedPrompt = NormalizeOptimizedPrompt(content),
            GeneratedAt = DateTime.UtcNow
        };
    }

    public async Task<AiModelsResult> GetModelsAsync(
        AiModelsInput input,
        CancellationToken cancellationToken = default)
    {
        var runtime = await GetRuntimeConfigAsync(cancellationToken);
        var provider = ResolveProvider(runtime, input.ProviderId);
        var providerId = string.IsNullOrWhiteSpace(input.ProviderId)
            ? provider.Id
            : input.ProviderId.Trim();
        var baseUrl = string.IsNullOrWhiteSpace(input.BaseUrl) ? provider.BaseUrl : input.BaseUrl.Trim();
        var apiKey = string.IsNullOrWhiteSpace(input.ApiKey) ? provider.ApiKey : input.ApiKey.Trim();

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("Base URL 不能为空。");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("API Key 不能为空。");
        }

        List<string> models;
        try
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30),
                BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/")
            };

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await client.GetAsync("models", cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorText = TryReadOpenAiError(raw);
                throw new InvalidOperationException($"接口错误({(int)response.StatusCode}) {errorText}");
            }

            models = ParseModels(raw);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            throw new InvalidOperationException("拉取模型列表超时，请稍后重试。");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch models from provider");
            throw new InvalidOperationException($"拉取模型列表失败：{ex.Message}");
        }

        if (models.Count == 0)
        {
            throw new InvalidOperationException("未获取到模型列表，请检查 Base URL 或 API Key。");
        }

        return new AiModelsResult
        {
            ProviderId = providerId,
            Models = models,
            FetchedAt = DateTime.UtcNow
        };
    }

    private async Task<(bool Success, string Content, string Error, string Model)> SendChatCompletionAsync(
        AiProviderRuntimeConfig provider,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken,
        string? modelOverride = null)
    {
        var models = ParseModelCandidates(modelOverride)
            .Concat(ParseModelCandidates(provider.Model))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!models.Any())
        {
            return (false, string.Empty, "请先配置至少一个可用模型。", string.Empty);
        }

        var errors = new List<string>();
        foreach (var model in models)
        {
            var completion = await TrySendSingleModelCompletionAsync(
                provider,
                systemPrompt,
                userPrompt,
                cancellationToken,
                model);

            if (completion.Success)
            {
                return completion;
            }

            if (!string.IsNullOrWhiteSpace(completion.Error))
            {
                errors.Add($"{model}: {completion.Error}");
            }
        }

        return (
            false,
            string.Empty,
            errors.Count == 0 ? "AI 调用失败，请稍后重试。" : $"所有模型调用失败：{string.Join(" | ", errors)}",
            string.Empty);
    }

    private async Task<(bool Success, string Content, string Error, string Model)> TrySendSingleModelCompletionAsync(
        AiProviderRuntimeConfig provider,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken,
        string model)
    {
        var disableMaxTokens = false;
        try
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(45),
                BaseAddress = new Uri(provider.BaseUrl.TrimEnd('/') + "/")
            };

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            while (true)
            {
                var payload = BuildChatPayload(model, systemPrompt, userPrompt, disableMaxTokens);
                var request = new StringContent(
                    JsonConvert.SerializeObject(payload),
                    Encoding.UTF8,
                    "application/json");

                var response = await client.PostAsync("chat/completions", request, cancellationToken);
                var raw = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var errorText = TryReadOpenAiError(raw);
                    if (!disableMaxTokens && ShouldRetryWithoutMaxTokens(errorText))
                    {
                        disableMaxTokens = true;
                        continue;
                    }

                    _logger.LogWarning("OpenAI API error: {StatusCode} - {Error}", response.StatusCode, errorText);
                    return (false, string.Empty, $"接口错误({(int)response.StatusCode}) {errorText}", model);
                }

                var json = JObject.Parse(raw);
                var content = json["choices"]?.FirstOrDefault()?["message"]?["content"]?.ToString() ?? string.Empty;
                var modelUsed = json["model"]?.ToString() ?? model;
                return (true, content, string.Empty, modelUsed);
            }
        }
        catch (TaskCanceledException)
        {
            return (false, string.Empty, "请求超时", model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI analysis request failed");
            return (false, string.Empty, $"调用异常：{ex.Message}", model);
        }
    }

    private static object BuildChatPayload(
        string model,
        string systemPrompt,
        string userPrompt,
        bool disableMaxTokens)
    {
        var payload = new JObject
        {
            ["model"] = model,
            ["temperature"] = 0.3,
            ["messages"] = new JArray
            {
                new JObject
                {
                    ["role"] = "system",
                    ["content"] = systemPrompt
                },
                new JObject
                {
                    ["role"] = "user",
                    ["content"] = userPrompt
                }
            }
        };

        if (!disableMaxTokens)
        {
            payload["max_tokens"] = 1600;
        }

        return payload;
    }

    private static bool ShouldRetryWithoutMaxTokens(string errorText)
    {
        if (string.IsNullOrWhiteSpace(errorText))
        {
            return false;
        }

        var normalized = errorText.ToLowerInvariant();
        return normalized.Contains("max_tokens")
            && normalized.Contains("max_completion_tokens")
            && normalized.Contains("cannot both be set");
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

    private static string NormalizeOptimizedPrompt(string raw)
    {
        var content = (raw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        if (content.StartsWith("```", StringComparison.Ordinal))
        {
            var lines = content.Split('\n').ToList();
            if (lines.Count >= 2)
            {
                lines.RemoveAt(0);
                if (lines.Last().Trim() == "```")
                {
                    lines.RemoveAt(lines.Count - 1);
                }
                content = string.Join('\n', lines).Trim();
            }
        }

        content = content
            .Replace("优化后的最终提问：", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("优化后的提问：", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        return content;
    }

    private static List<string> ParseModels(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new List<string>();
        }

        try
        {
            var token = JToken.Parse(raw);
            var candidates = new List<string>();

            if (token is JObject obj)
            {
                if (obj["data"] is JArray dataArray)
                {
                    candidates.AddRange(ExtractModelIds(dataArray));
                }
                else if (obj["models"] is JArray modelArray)
                {
                    candidates.AddRange(ExtractModelIds(modelArray));
                }
                else if (obj["id"] != null || obj["model"] != null || obj["name"] != null)
                {
                    candidates.Add(ExtractModelId(obj));
                }
            }
            else if (token is JArray rootArray)
            {
                candidates.AddRange(ExtractModelIds(rootArray));
            }

            return candidates
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static IEnumerable<string> ExtractModelIds(JArray rows)
    {
        foreach (var row in rows)
        {
            if (row is JObject obj)
            {
                yield return ExtractModelId(obj);
                continue;
            }

            if (row.Type == JTokenType.String)
            {
                yield return row.ToString();
            }
        }
    }

    private static string ExtractModelId(JObject row)
    {
        return row["id"]?.ToString()
            ?? row["model"]?.ToString()
            ?? row["name"]?.ToString()
            ?? string.Empty;
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
        var providers = ParseProviders(map);
        var activeProviderId = GetValue(map, "ActiveProviderId");

        if (!providers.Any())
        {
            providers.Add(new AiProviderRuntimeConfig
            {
                Id = "default",
                Name = "默认模型源",
                ApiKey = GetValue(map, "ApiKey", _configuration["OpenAI:ApiKey"]),
                BaseUrl = GetValue(map, "BaseUrl", _configuration["OpenAI:BaseUrl"], "https://api.openai.com/v1"),
                Model = GetValue(map, "Model", _configuration["OpenAI:Model"], "gpt-4o-mini")
            });
        }

        if (!providers.Any(p => string.Equals(p.Id, activeProviderId, StringComparison.OrdinalIgnoreCase)))
        {
            activeProviderId = providers[0].Id;
        }

        return new OpenAiRuntimeConfig
        {
            Enabled = ParseBool(GetValue(map, "Enabled", _configuration["OpenAI:Enabled"]), false),
            Providers = providers,
            ActiveProviderId = activeProviderId
        };
    }

    private static List<AiProviderRuntimeConfig> ParseProviders(IReadOnlyDictionary<string, string> map)
    {
        var providersRaw = GetValue(map, "Providers");
        if (string.IsNullOrWhiteSpace(providersRaw))
        {
            return new List<AiProviderRuntimeConfig>();
        }

        try
        {
            var array = JArray.Parse(providersRaw);
            var providers = new List<AiProviderRuntimeConfig>();
            foreach (var row in array)
            {
                if (row is not JObject providerObj)
                {
                    continue;
                }

                var id = ReadObjectValue(providerObj, "id", "Id")?.Trim();
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                var apiKey = ReadObjectValue(providerObj, "apiKey", "ApiKey")?.Trim() ?? string.Empty;
                if (string.Equals(apiKey, MaskedValue, StringComparison.Ordinal))
                {
                    apiKey = string.Empty;
                }

                providers.Add(new AiProviderRuntimeConfig
                {
                    Id = id,
                    Name = ReadObjectValue(providerObj, "name", "Name")?.Trim() ?? id,
                    ApiKey = apiKey,
                    BaseUrl = ReadObjectValue(providerObj, "baseUrl", "BaseUrl")?.Trim() ?? "https://api.openai.com/v1",
                    Model = ReadObjectValue(providerObj, "model", "Model")?.Trim() ?? "gpt-4o-mini"
                });
            }

            return providers;
        }
        catch
        {
            return new List<AiProviderRuntimeConfig>();
        }
    }

    private static string? ReadObjectValue(JObject obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            var direct = obj[key];
            if (direct != null && direct.Type != JTokenType.Null)
            {
                var value = direct.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            var matched = obj.Properties()
                .FirstOrDefault(item => item.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (matched?.Value is { Type: not JTokenType.Null } token)
            {
                var value = token.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static AiProviderRuntimeConfig ResolveProvider(OpenAiRuntimeConfig runtime, string? preferredProviderId)
    {
        var preferredId = string.IsNullOrWhiteSpace(preferredProviderId)
            ? runtime.ActiveProviderId
            : preferredProviderId.Trim();

        var provider = runtime.Providers
            .FirstOrDefault(item => item.Id.Equals(preferredId, StringComparison.OrdinalIgnoreCase));

        return provider ?? runtime.Providers.First();
    }

    private static List<string> ParseModelCandidates(string? raw)
    {
        return (raw ?? string.Empty)
            .Split(new[] { ',', ';', '\n', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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
        public List<AiProviderRuntimeConfig> Providers { get; init; } = new();
        public string ActiveProviderId { get; init; } = string.Empty;
    }

    private sealed class AiProviderRuntimeConfig
    {
        public string Id { get; init; } = "default";
        public string Name { get; init; } = "默认模型源";
        public string ApiKey { get; init; } = string.Empty;
        public string BaseUrl { get; init; } = "https://api.openai.com/v1";
        public string Model { get; init; } = "gpt-4o-mini";
    }
}
