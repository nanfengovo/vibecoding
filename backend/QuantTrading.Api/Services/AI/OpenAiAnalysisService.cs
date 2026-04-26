using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using QuantTrading.Api.Data;
using QuantTrading.Api.Services.LongBridge;

namespace QuantTrading.Api.Services.AI;

public sealed class OpenAiAnalysisService : IAiAnalysisService
{
    private const string MaskedValue = "******";
    private const int RealtimeStaleThresholdSeconds = 25 * 60;
    private static readonly Regex SymbolPattern = new(
        @"\b(?:[A-Z]{1,5}\.(?:US|HK|SG)|\d{5}\.HK|\d{6}\.(?:SH|SZ)|(?:SH|SZ)\d{6}|[A-Z]{1,5}|\d{5}|\d{6})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> TickerStopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "AI", "A", "AN", "THE", "AND", "OR", "ETF", "USD", "CNY", "HKD", "RMB", "CNH",
        "NOW", "TODAY", "LIVE", "REALTIME", "PRICE", "MARKET", "STOCK"
    };
    private static readonly string[] RealtimeKeywords =
    {
        "最新", "实时", "现价", "股价", "价格", "涨跌", "开盘", "收盘", "行情", "报价", "多少", "最新价",
        "last price", "live", "latest", "latest price", "current price", "quote"
    };
    private static readonly string[] DirectQuoteKeywords =
    {
        "最新股价", "最新价格", "最新价", "现价", "现在多少钱", "多少钱", "报价", "股价",
        "latest price", "current price", "last price", "price now", "quote"
    };
    private static readonly string[] ChineseNoiseWords =
    {
        "股票", "股价", "价格", "行情", "走势", "分析", "最新", "实时", "现在", "获取", "帮我", "请问", "一下",
        "看看", "查询", "多少", "的", "股", "一只", "标的", "美股", "港股", "a股", "A股", "今日", "今天", "当前"
    };
    private static readonly Dictionary<string, string> SkillPrompts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cross-market-selection"] = "Skill: 跨市场选股。优先按估值、趋势和流动性筛选，结果请给出可执行的候选清单。",
        ["technical-diagnosis"] = "Skill: 技术面诊断。请从趋势、结构、量价、关键位与失效条件给出结论。",
        ["financial-research"] = "Skill: 财报研究。请拆解收入、利润、现金流和估值变化，标注核心变量。",
        ["smart-money-tracking"] = "Skill: 聪明钱追踪。重点分析成交异动、资金流向与板块联动。",
        ["advanced-order"] = "Skill: 进阶下单。输出分批入场、止损、止盈与仓位控制建议。",
        ["position-review"] = "Skill: 持仓复盘。总结得失、风险暴露和下一步调整动作。"
    };
    private readonly QuantTradingDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly ILongBridgeService _longBridgeService;
    private readonly ILogger<OpenAiAnalysisService> _logger;

    public OpenAiAnalysisService(
        QuantTradingDbContext dbContext,
        IConfiguration configuration,
        ILongBridgeService longBridgeService,
        ILogger<OpenAiAnalysisService> logger)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _longBridgeService = longBridgeService;
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

        var question = (input.Question ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(question))
        {
            throw new InvalidOperationException("问题不能为空。");
        }

        var marketSensitive = IsRealtimeSensitiveQuestion(question);
        var directQuoteQuestion = IsDirectQuoteQuestion(question);
        var resolvedSymbol = await ResolveChatSymbolAsync(input.Symbol, question, cancellationToken);
        if (marketSensitive && string.IsNullOrWhiteSpace(resolvedSymbol))
        {
            throw new InvalidOperationException("这是实时行情问题，但未识别到标的。请补充证券代码或公司名称（如 601899.SH / 紫金矿业）。");
        }

        AiChatMarketContext? marketContext = null;
        string stockDisplayName = string.Empty;
        if (!string.IsNullOrWhiteSpace(resolvedSymbol))
        {
            marketContext = await BuildMarketContextAsync(resolvedSymbol, cancellationToken);
            if (marketContext == null && marketSensitive)
            {
                throw new InvalidOperationException("未获取到有效实时行情，请检查证券代码格式与长桥权限。");
            }

            try
            {
                stockDisplayName = await ResolveStockDisplayNameAsync(resolvedSymbol, cancellationToken);
            }
            catch
            {
                // Ignore stock name enrichment errors.
            }

            if (marketSensitive && marketContext?.Freshness == "stale")
            {
                throw new InvalidOperationException(
                    $"行情时间已过期（{marketContext.LagSeconds} 秒），请稍后重试或检查长桥连接状态。");
            }
        }

        if (marketSensitive && marketContext != null && directQuoteQuestion)
        {
            return new AiChatResult
            {
                Model = "longbridge-realtime",
                Content = BuildStrictRealtimeAnswer(question, marketContext, stockDisplayName),
                GeneratedAt = DateTime.UtcNow,
                MarketContext = marketContext,
                References = input.KnowledgeContext.ToList()
            };
        }

        if (!runtime.Enabled)
        {
            if (marketSensitive && marketContext != null)
            {
                return new AiChatResult
                {
                    Model = "longbridge-realtime-fallback",
                    Content = BuildStrictRealtimeAnswer(
                        question,
                        marketContext,
                        stockDisplayName,
                        "AI 模型未启用，已返回严格实时行情结果。"),
                    GeneratedAt = DateTime.UtcNow,
                    MarketContext = marketContext,
                    References = input.KnowledgeContext.ToList()
                };
            }

            throw new InvalidOperationException("OpenAI 分析未启用，请先在设置中开启。");
        }

        var provider = ResolveProvider(runtime, input.ProviderId);
        if (string.IsNullOrWhiteSpace(provider.ApiKey))
        {
            if (marketSensitive && marketContext != null)
            {
                return new AiChatResult
                {
                    Model = "longbridge-realtime-fallback",
                    Content = BuildStrictRealtimeAnswer(
                        question,
                        marketContext,
                        stockDisplayName,
                        $"模型源“{provider.Name}”未配置 API Key，已返回严格实时行情结果。"),
                    GeneratedAt = DateTime.UtcNow,
                    MarketContext = marketContext,
                    References = input.KnowledgeContext.ToList()
                };
            }

            throw new InvalidOperationException($"模型源“{provider.Name}”未配置 API Key。");
        }

        var systemPrompt = marketSensitive
            ? "你是专业交易助手。必须仅基于用户给定的实时行情上下文回答，不得引用训练记忆中的历史价格。若上下文不足，明确说明无法判断。"
            : "你是专业交易助手。请给出结构化、简洁、可执行的中文回答，并包含风险提示。";
        var userPrompt = BuildChatPrompt(input, question, marketContext);

        var modelOverride = (input.Model ?? string.Empty).Trim();
        var (success, content, error, modelUsed) = await SendChatCompletionAsync(
            provider,
            systemPrompt,
            userPrompt,
            cancellationToken,
            modelOverride);

        if (!success || string.IsNullOrWhiteSpace(content))
        {
            if (marketSensitive && marketContext != null)
            {
                return new AiChatResult
                {
                    Model = "longbridge-realtime-fallback",
                    Content = BuildStrictRealtimeAnswer(question, marketContext, stockDisplayName, error),
                    GeneratedAt = DateTime.UtcNow,
                    MarketContext = marketContext,
                    References = input.KnowledgeContext.ToList()
                };
            }

            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error) ? "AI 聊天调用失败，请稍后重试。" : error);
        }

        return new AiChatResult
        {
            Model = string.IsNullOrWhiteSpace(modelUsed)
                ? (string.IsNullOrWhiteSpace(modelOverride) ? provider.Model : modelOverride)
                : modelUsed,
            Content = content.Trim(),
            GeneratedAt = DateTime.UtcNow,
            MarketContext = marketContext,
            References = input.KnowledgeContext.ToList()
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

        var scene = (input.Scene ?? string.Empty).Trim().ToLowerInvariant();
        var sceneGuidance = BuildPromptSceneGuidance(scene);
        var systemPrompt = "你是专业提示词优化助手。你的任务是重写用户问题，让模型在对应场景下输出更精准、结构化、可执行的回答。";
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(input.Symbol))
        {
            sb.AppendLine($"标的：{input.Symbol.Trim().ToUpperInvariant()}");
        }
        if (!string.IsNullOrWhiteSpace(sceneGuidance))
        {
            sb.AppendLine($"场景：{sceneGuidance}");
        }
        if (input.KnowledgeBaseId.HasValue && input.KnowledgeBaseId.Value > 0)
        {
            sb.AppendLine($"知识库：{input.KnowledgeBaseId.Value}");
        }
        if (input.ReaderContext != null)
        {
            sb.AppendLine("阅读器上下文：");
            sb.AppendLine($"- 书籍：{input.ReaderContext.Title}");
            sb.AppendLine($"- 格式：{input.ReaderContext.Format}");
            sb.AppendLine($"- 定位：{input.ReaderContext.Locator}");
            sb.AppendLine($"- 选中内容：{TrimForPrompt(input.ReaderContext.SelectedText, 1200)}");
        }
        if (!string.IsNullOrWhiteSpace(input.ContextText))
        {
            sb.AppendLine("补充上下文：");
            sb.AppendLine(TrimForPrompt(input.ContextText, 1800));
        }
        sb.AppendLine($"原始问题：{question}");
        sb.AppendLine();
        sb.AppendLine("请输出“优化后的最终提问”一段文本，不要解释，不要加标题，不要使用 markdown 代码块。");
        sb.AppendLine("要求：");
        sb.AppendLine("1) 保留原问题意图，不改变用户目标。");
        sb.AppendLine("2) 结合给定场景补充分析维度、输出结构和约束。");
        sb.AppendLine("3) 语气专业、简洁、可执行，避免空泛表述。");

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
                Timeout = TimeSpan.FromSeconds(90),
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

    private static string BuildPromptSceneGuidance(string scene)
    {
        return scene switch
        {
            "reader" => "阅读器问答：结合选中段落，生成可直接用于解读与追问的提问语句，强调上下文引用与理解深度。",
            "knowledge" => "知识库问答：优先要求基于知识库内容作答，必要时要求引用依据并标注不确定点。",
            "stock_analysis" => "个股分析：强调趋势、关键价位、交易计划、风险控制与仓位管理。",
            "ai_chat" => "通用 AI 聊天：在保持意图的基础上提升结构化和可执行性。",
            _ => "通用场景：保持问题意图，增强结构化与可执行性。"
        };
    }

    private static string TrimForPrompt(string value, int maxLength)
    {
        var clean = (value ?? string.Empty).Trim();
        if (clean.Length <= maxLength)
        {
            return clean;
        }

        return $"{clean[..maxLength]}...";
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

    private async Task<string> ResolveChatSymbolAsync(
        string? inputSymbol,
        string question,
        CancellationToken cancellationToken)
    {
        var direct = NormalizeSymbol(inputSymbol ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(direct))
        {
            var resolved = await TryResolveSymbolCandidateAsync(direct, cancellationToken);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }
        }

        foreach (var candidate in ExtractSymbolCandidatesFromQuestion(question))
        {
            var resolved = await TryResolveSymbolCandidateAsync(candidate, cancellationToken);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }
        }

        var triedKeywords = 0;
        foreach (var keyword in ExtractChineseKeywordCandidates(question))
        {
            var rows = await _longBridgeService.SearchStocksAsync(keyword);
            var matched = rows.FirstOrDefault();
            if (matched != null)
            {
                return NormalizeSymbol(matched.Symbol);
            }

            triedKeywords++;
            if (triedKeywords >= 2)
            {
                break;
            }
        }

        return string.Empty;
    }

    private async Task<string> ResolveStockDisplayNameAsync(
        string symbol,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        var normalized = NormalizeSymbol(symbol);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var rows = await _longBridgeService.SearchStocksAsync(normalized);
        var matched = rows.FirstOrDefault(item => SymbolEquals(item.Symbol, normalized))
            ?? rows.FirstOrDefault(item => NormalizeSymbol(item.Symbol).StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
            ?? rows.FirstOrDefault();
        var name = (matched?.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var baseSymbol = normalized.Split('.').FirstOrDefault() ?? normalized;
        if (name.Equals(normalized, StringComparison.OrdinalIgnoreCase)
            || name.Equals(baseSymbol, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return name;
    }

    private async Task<string> TryResolveSymbolCandidateAsync(
        string candidate,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return string.Empty;
        }

        var normalized = NormalizeSymbol(candidate);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var rows = await _longBridgeService.SearchStocksAsync(normalized);
        var matched = rows.FirstOrDefault(item => SymbolEquals(item.Symbol, normalized))
            ?? rows.FirstOrDefault(item => NormalizeSymbol(item.Symbol).StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
            ?? rows.FirstOrDefault();
        if (matched != null)
        {
            return NormalizeSymbol(matched.Symbol);
        }

        var stock = await _longBridgeService.GetStockInfoAsync(normalized);
        if (stock == null)
        {
            return string.Empty;
        }

        return NormalizeSymbol(stock.Symbol);
    }

    private async Task<AiChatMarketContext?> BuildMarketContextAsync(
        string symbol,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        var normalized = NormalizeSymbol(symbol);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var quote = await _longBridgeService.GetQuoteStrictAsync(normalized);
        if (quote == null || quote.Price <= 0)
        {
            return null;
        }

        var quoteTime = EnsureUtc(quote.Timestamp);
        if (quoteTime <= DateTime.UnixEpoch)
        {
            return null;
        }

        var market = InferMarketFromSymbol(normalized);
        bool marketOpen;
        try
        {
            marketOpen = await _longBridgeService.IsMarketOpenAsync(market);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get market status for {Symbol}", normalized);
            marketOpen = false;
        }

        var lagSeconds = Math.Max(0, (int)Math.Round((DateTime.UtcNow - quoteTime).TotalSeconds));
        var freshness = DetermineFreshness(lagSeconds, marketOpen);

        return new AiChatMarketContext
        {
            Symbol = normalized,
            Market = market,
            Price = quote.Price,
            ChangePercent = quote.ChangePercent,
            QuoteTime = quoteTime,
            LagSeconds = lagSeconds,
            MarketOpen = marketOpen,
            Freshness = freshness,
            Source = "longbridge"
        };
    }

    private static string BuildChatPrompt(
        AiChatInput input,
        string question,
        AiChatMarketContext? marketContext)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(input.SkillId)
            && SkillPrompts.TryGetValue(input.SkillId.Trim(), out var skillPrompt)
            && !string.IsNullOrWhiteSpace(skillPrompt))
        {
            sb.AppendLine(skillPrompt);
            sb.AppendLine();
        }

        if (input.MemoryContext.Count > 0)
        {
            sb.AppendLine("用户长期记忆（仅用于个性化，不得泄露）：");
            foreach (var memory in input.MemoryContext.Take(8))
            {
                sb.AppendLine($"- {memory}");
            }
            sb.AppendLine();
        }

        if (input.ConversationContext.Count > 0)
        {
            sb.AppendLine("最近会话上下文：");
            foreach (var message in input.ConversationContext.TakeLast(12))
            {
                sb.AppendLine($"- {message}");
            }
            sb.AppendLine();
        }

        if (input.KnowledgeContext.Count > 0)
        {
            sb.AppendLine("知识库检索片段（回答相关问题时优先引用）：");
            var index = 1;
            foreach (var reference in input.KnowledgeContext.Take(8))
            {
                sb.AppendLine($"[{index}] {reference.Title}");
                if (!string.IsNullOrWhiteSpace(reference.SourceUrl))
                {
                    sb.AppendLine($"来源：{reference.SourceUrl}");
                }
                sb.AppendLine(reference.Snippet);
                sb.AppendLine();
                index++;
            }
            sb.AppendLine("若使用知识库片段，请在回答末尾列出“引用”。");
            sb.AppendLine();
        }

        if (input.ReaderContext != null)
        {
            sb.AppendLine("阅读上下文（来自用户当前阅读器）：");
            sb.AppendLine($"- 图书ID：{input.ReaderContext.BookId}");
            if (!string.IsNullOrWhiteSpace(input.ReaderContext.Title))
            {
                sb.AppendLine($"- 标题：{input.ReaderContext.Title.Trim()}");
            }
            if (!string.IsNullOrWhiteSpace(input.ReaderContext.Format))
            {
                sb.AppendLine($"- 格式：{input.ReaderContext.Format.Trim().ToUpperInvariant()}");
            }
            if (!string.IsNullOrWhiteSpace(input.ReaderContext.Locator))
            {
                sb.AppendLine($"- 定位：{TrimContext(input.ReaderContext.Locator, 500)}");
            }
            if (!string.IsNullOrWhiteSpace(input.ReaderContext.SelectedText))
            {
                sb.AppendLine("- 当前选中文本：");
                sb.AppendLine(TrimContext(input.ReaderContext.SelectedText, 1800));
            }
            sb.AppendLine();
            sb.AppendLine("要求：优先结合上述选中文本与定位回答问题。");
            sb.AppendLine();
        }

        if (marketContext != null)
        {
            sb.AppendLine("实时行情上下文（可信数据源）：");
            sb.AppendLine($"- 标的：{marketContext.Symbol}");
            sb.AppendLine($"- 市场：{marketContext.Market}");
            sb.AppendLine($"- 最新价：{marketContext.Price:F4}");
            sb.AppendLine($"- 涨跌幅：{marketContext.ChangePercent:F2}%");
            sb.AppendLine($"- 行情时间(UTC)：{marketContext.QuoteTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"- 距今时延：{marketContext.LagSeconds} 秒");
            sb.AppendLine($"- 市场状态：{(marketContext.MarketOpen ? "开市" : "闭市")}");
            sb.AppendLine($"- 新鲜度：{marketContext.Freshness}");
            sb.AppendLine($"- 来源：{marketContext.Source}");
            sb.AppendLine();
            sb.AppendLine("要求：涉及价格的判断必须引用上述行情，不得虚构或替换为训练记忆中的历史价格。");
            sb.AppendLine();
        }
        else if (!string.IsNullOrWhiteSpace(input.Symbol))
        {
            sb.AppendLine($"标的：{NormalizeSymbol(input.Symbol)}");
        }

        if (!string.IsNullOrWhiteSpace(input.Focus))
        {
            sb.AppendLine($"关注点：{input.Focus.Trim()}");
        }

        sb.AppendLine($"用户问题：{question}");
        sb.AppendLine();
        sb.AppendLine("请按“结论 / 依据 / 风险提示”结构回复。");
        sb.AppendLine("注意：不构成投资建议。");
        return sb.ToString();
    }

    private static bool IsRealtimeSensitiveQuestion(string question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return false;
        }

        return RealtimeKeywords.Any(keyword => question.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsDirectQuoteQuestion(string question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return false;
        }

        return DirectQuoteKeywords.Any(keyword => question.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildStrictRealtimeAnswer(
        string question,
        AiChatMarketContext marketContext,
        string stockDisplayName,
        string? modelError = null)
    {
        var displayName = string.IsNullOrWhiteSpace(stockDisplayName)
            ? marketContext.Symbol
            : $"{stockDisplayName}（{marketContext.Symbol}）";
        var quoteTimeLocal = marketContext.QuoteTime.ToLocalTime();
        var lagText = marketContext.LagSeconds <= 0 ? "0 秒" : $"{marketContext.LagSeconds} 秒";
        var freshnessText = marketContext.Freshness switch
        {
            "realtime" => "实时",
            "delayed_close" => "闭市延迟",
            _ => "过期"
        };

        var lines = new List<string>
        {
            $"结论：{displayName} 当前最新可用价格为 {marketContext.Price:F4}，涨跌幅 {marketContext.ChangePercent:+0.00;-0.00;0.00}%。",
            string.Empty,
            "依据：",
            $"- 数据源：{marketContext.Source}",
            $"- 行情时间（本地）：{quoteTimeLocal:yyyy-MM-dd HH:mm:ss}",
            $"- 时延：{lagText}",
            $"- 新鲜度：{freshnessText}",
            string.Empty
        };

        if (!string.IsNullOrWhiteSpace(modelError))
        {
            lines.Add($"说明：本次大模型解析失败（{modelError}），已直接返回 Longbridge 行情数据快照。");
            lines.Add(string.Empty);
        }

        lines.Add("风险提示：行情会随交易波动变化，以上仅为数据与分析辅助，不构成投资建议。");
        lines.Add($"问题原文：{question}");
        return string.Join('\n', lines);
    }

    private static IEnumerable<string> ExtractSymbolCandidatesFromQuestion(string question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return [];
        }

        var candidates = new List<string>();
        foreach (Match match in SymbolPattern.Matches(question.ToUpperInvariant()))
        {
            var token = match.Value.Trim();
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            if (token.All(char.IsLetter) && (token.Length <= 1 || TickerStopwords.Contains(token)))
            {
                continue;
            }

            candidates.Add(token);
        }

        return candidates
            .Select(NormalizeSymbol)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
    }

    private static IEnumerable<string> ExtractChineseKeywordCandidates(string question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return [];
        }

        var rows = new List<string>();
        var matches = Regex.Matches(question, @"[\u4e00-\u9fff]{2,18}");
        foreach (Match match in matches)
        {
            var value = match.Value.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var normalized = value;
            foreach (var noise in ChineseNoiseWords)
            {
                normalized = normalized.Replace(noise, string.Empty, StringComparison.OrdinalIgnoreCase);
            }

            normalized = normalized.Trim();
            if (normalized.Length is < 2 or > 10)
            {
                continue;
            }

            rows.Add(normalized);
        }

        return rows
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(value => value.Length)
            .Take(4)
            .ToList();
    }

    private static string DetermineFreshness(int lagSeconds, bool marketOpen)
    {
        if (!marketOpen)
        {
            return "delayed_close";
        }

        return lagSeconds <= RealtimeStaleThresholdSeconds ? "realtime" : "stale";
    }

    private static DateTime EnsureUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    private static string InferMarketFromSymbol(string symbol)
    {
        var normalized = NormalizeSymbol(symbol);
        if (string.IsNullOrWhiteSpace(normalized) || !normalized.Contains('.'))
        {
            return "US";
        }

        return normalized.Split('.').LastOrDefault()?.ToUpperInvariant() ?? "US";
    }

    private static string NormalizeSymbol(string symbol)
    {
        var normalized = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        if (normalized.StartsWith("SH", StringComparison.OrdinalIgnoreCase)
            && normalized.Length == 8
            && normalized.Skip(2).All(char.IsDigit))
        {
            return $"{normalized[2..]}.SH";
        }

        if (normalized.StartsWith("SZ", StringComparison.OrdinalIgnoreCase)
            && normalized.Length == 8
            && normalized.Skip(2).All(char.IsDigit))
        {
            return $"{normalized[2..]}.SZ";
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

    private static string TrimContext(string value, int length)
    {
        var clean = (value ?? string.Empty).Trim();
        return clean.Length <= length ? clean : $"{clean[..length]}...";
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
