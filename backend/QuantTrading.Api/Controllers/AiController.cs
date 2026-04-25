using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using QuantTrading.Api.Data;
using QuantTrading.Api.Models;
using QuantTrading.Api.Services.AI;
using QuantTrading.Api.Services.Auth;
using QuantTrading.Api.Services.Knowledge;
using QuantTrading.Api.Services.LongBridge;

namespace QuantTrading.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class AiController : ControllerBase
{
    private readonly IAiAnalysisService _aiAnalysisService;
    private readonly ILongBridgeService _longBridgeService;
    private readonly QuantTradingDbContext _dbContext;
    private readonly ICurrentUserService _currentUser;
    private readonly IKnowledgeService _knowledgeService;

    public AiController(
        IAiAnalysisService aiAnalysisService,
        ILongBridgeService longBridgeService,
        QuantTradingDbContext dbContext,
        ICurrentUserService currentUser,
        IKnowledgeService knowledgeService)
    {
        _aiAnalysisService = aiAnalysisService;
        _longBridgeService = longBridgeService;
        _dbContext = dbContext;
        _currentUser = currentUser;
        _knowledgeService = knowledgeService;
    }

    [HttpPost("analyze/stock/{symbol}")]
    public async Task<ActionResult<StockAnalysisResult>> AnalyzeStock(
        string symbol,
        [FromBody] AnalyzeStockRequest? request,
        CancellationToken cancellationToken)
    {
        var normalizedSymbol = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedSymbol))
        {
            return BadRequest("股票代码不能为空。");
        }

        var stock = await _longBridgeService.GetStockInfoAsync(normalizedSymbol);
        if (stock == null)
        {
            return NotFound("股票不存在。");
        }

        var quote = await _longBridgeService.GetQuoteAsync(normalizedSymbol);
        var period = string.IsNullOrWhiteSpace(request?.Period) ? "D" : request!.Period.Trim().ToUpperInvariant();
        var maxCount = request?.Count is > 0 and <= 1000 ? request.Count.Value : 260;
        var klines = await _longBridgeService.GetKlineAsync(
            normalizedSymbol,
            period,
            request?.Start,
            request?.End,
            maxCount);

        try
        {
            var result = await _aiAnalysisService.AnalyzeStockAsync(
                new StockAnalysisInput
                {
                    Symbol = normalizedSymbol,
                    Focus = request?.Focus ?? string.Empty,
                    ProviderId = request?.ProviderId ?? string.Empty,
                    Model = request?.Model ?? string.Empty,
                    Stock = stock,
                    Quote = quote,
                    Klines = klines
                },
                cancellationToken);

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("chat")]
    public async Task<ActionResult<AiChatResult>> Chat(
        [FromBody] AiChatRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.Question))
        {
            return BadRequest(new { message = "问题不能为空。" });
        }

        try
        {
            var userId = await _currentUser.GetEffectiveUserIdAsync(cancellationToken);
            var session = await ResolveChatSessionAsync(userId, request, cancellationToken);
            var memoryContext = request.UseMemory == false
                ? new List<string>()
                : await LoadMemoryContextAsync(userId, request.Question, request.Symbol, cancellationToken);
            var conversationContext = await LoadConversationContextAsync(userId, session.Id, cancellationToken);
            var knowledgeContext = request.KnowledgeBaseId.HasValue
                ? await _knowledgeService.SearchAsync(userId, request.KnowledgeBaseId.Value, request.Question, 8, cancellationToken)
                : new List<AiKnowledgeReference>();

            _dbContext.AiChatMessages.Add(new AiChatMessageRecord
            {
                UserId = userId,
                SessionId = session.Id,
                Role = "user",
                Content = request.Question.Trim(),
                CreatedAt = DateTime.UtcNow
            });
            session.UpdatedAt = DateTime.UtcNow;
            if (session.Title == "新会话")
            {
                session.Title = NormalizeTitle(request.Question);
            }

            var result = await _aiAnalysisService.ChatAsync(
                new AiChatInput
                {
                    Question = request.Question,
                    Symbol = request.Symbol ?? string.Empty,
                    Focus = request.Focus ?? string.Empty,
                    SkillId = request.SkillId ?? string.Empty,
                    ProviderId = request.ProviderId ?? string.Empty,
                    Model = request.Model ?? string.Empty,
                    ConversationContext = conversationContext,
                    MemoryContext = memoryContext,
                    KnowledgeContext = knowledgeContext
                },
                cancellationToken);

            session.Symbol = request.Symbol ?? session.Symbol;
            session.SkillId = request.SkillId ?? session.SkillId;
            session.ProviderId = request.ProviderId ?? session.ProviderId;
            session.Model = string.IsNullOrWhiteSpace(result.Model) ? request.Model ?? session.Model : result.Model;
            session.LastMarketContextSymbol = result.MarketContext?.Symbol ?? session.LastMarketContextSymbol;
            session.UpdatedAt = DateTime.UtcNow;

            _dbContext.AiChatMessages.Add(new AiChatMessageRecord
            {
                UserId = userId,
                SessionId = session.Id,
                Role = "assistant",
                Content = result.Content,
                Model = result.Model,
                MarketContextJson = result.MarketContext == null ? string.Empty : JsonConvert.SerializeObject(result.MarketContext),
                CreatedAt = DateTime.UtcNow
            });

            await UpsertHeuristicMemoryAsync(userId, request.Question, result.Content, request.Symbol, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Ok(new AiChatResult
            {
                Model = result.Model,
                Content = result.Content,
                GeneratedAt = result.GeneratedAt,
                MarketContext = result.MarketContext,
                SessionId = session.Id,
                References = result.References
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("optimize-prompt")]
    public async Task<ActionResult<AiPromptOptimizeResult>> OptimizePrompt(
        [FromBody] AiPromptOptimizeRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.Question))
        {
            return BadRequest(new { message = "待优化问题不能为空。" });
        }

        try
        {
            var result = await _aiAnalysisService.OptimizePromptAsync(
                new AiPromptOptimizeInput
                {
                    Question = request.Question,
                    Symbol = request.Symbol ?? string.Empty,
                    ProviderId = request.ProviderId ?? string.Empty,
                    Model = request.Model ?? string.Empty
                },
                cancellationToken);

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("models")]
    public async Task<ActionResult<AiModelsResult>> GetModels(
        [FromBody] AiModelsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _aiAnalysisService.GetModelsAsync(
                new AiModelsInput
                {
                    ProviderId = request?.ProviderId ?? string.Empty,
                    BaseUrl = request?.BaseUrl ?? string.Empty,
                    ApiKey = request?.ApiKey ?? string.Empty
                },
                cancellationToken);

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("sessions")]
    public async Task<ActionResult<List<AiChatSessionDto>>> ListSessions(CancellationToken cancellationToken)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync(cancellationToken);
        var sessions = await _dbContext.AiChatSessions
            .Where(item => item.UserId == userId && !item.IsArchived)
            .OrderByDescending(item => item.UpdatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);
        return Ok(sessions.Select(AiChatSessionDto.From).ToList());
    }

    [HttpGet("sessions/{id:long}")]
    public async Task<ActionResult<AiChatSessionDetailDto>> GetSession(long id, CancellationToken cancellationToken)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync(cancellationToken);
        var session = await _dbContext.AiChatSessions
            .FirstOrDefaultAsync(item => item.UserId == userId && item.Id == id && !item.IsArchived, cancellationToken);
        if (session == null)
        {
            return NotFound();
        }

        var messages = await _dbContext.AiChatMessages
            .Where(item => item.UserId == userId && item.SessionId == id)
            .OrderBy(item => item.CreatedAt)
            .ToListAsync(cancellationToken);
        return Ok(new AiChatSessionDetailDto
        {
            Session = AiChatSessionDto.From(session),
            Messages = messages.Select(AiChatMessageDto.From).ToList()
        });
    }

    [HttpPost("sessions")]
    public async Task<ActionResult<AiChatSessionDto>> CreateSession([FromBody] AiChatSessionCreateRequest request, CancellationToken cancellationToken)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync(cancellationToken);
        var session = new AiChatSessionRecord
        {
            UserId = userId,
            Title = string.IsNullOrWhiteSpace(request.Title) ? "新会话" : NormalizeTitle(request.Title),
            Symbol = request.Symbol?.Trim() ?? string.Empty,
            SkillId = request.SkillId?.Trim() ?? string.Empty,
            ProviderId = request.ProviderId?.Trim() ?? string.Empty,
            Model = request.Model?.Trim() ?? string.Empty,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _dbContext.AiChatSessions.Add(session);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(AiChatSessionDto.From(session));
    }

    [HttpDelete("sessions/{id:long}")]
    public async Task<ActionResult> DeleteSession(long id, CancellationToken cancellationToken)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync(cancellationToken);
        var session = await _dbContext.AiChatSessions.FirstOrDefaultAsync(item => item.UserId == userId && item.Id == id, cancellationToken);
        if (session == null)
        {
            return NotFound();
        }

        session.IsArchived = true;
        session.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("memories")]
    public async Task<ActionResult<List<AiMemoryRecord>>> ListMemories(CancellationToken cancellationToken)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync(cancellationToken);
        return Ok(await _dbContext.AiMemories
            .Where(item => item.UserId == userId && !item.IsArchived)
            .OrderByDescending(item => item.Priority)
            .ThenByDescending(item => item.UpdatedAt)
            .ToListAsync(cancellationToken));
    }

    [HttpPost("memories")]
    public async Task<ActionResult<AiMemoryRecord>> CreateMemory([FromBody] AiMemoryRecord request, CancellationToken cancellationToken)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return BadRequest(new { message = "记忆内容不能为空。" });
        }

        var memory = new AiMemoryRecord
        {
            UserId = userId,
            Type = string.IsNullOrWhiteSpace(request.Type) ? "manual" : request.Type.Trim(),
            Title = request.Title?.Trim() ?? string.Empty,
            Content = request.Content.Trim(),
            Symbol = request.Symbol?.Trim().ToUpperInvariant() ?? string.Empty,
            Tags = request.Tags?.Trim() ?? string.Empty,
            Priority = request.Priority <= 0 ? 1 : request.Priority,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _dbContext.AiMemories.Add(memory);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(memory);
    }

    [HttpDelete("memories/{id:long}")]
    public async Task<ActionResult> DeleteMemory(long id, CancellationToken cancellationToken)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync(cancellationToken);
        var memory = await _dbContext.AiMemories.FirstOrDefaultAsync(item => item.UserId == userId && item.Id == id, cancellationToken);
        if (memory == null)
        {
            return NotFound();
        }

        memory.IsArchived = true;
        memory.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<AiChatSessionRecord> ResolveChatSessionAsync(int userId, AiChatRequest request, CancellationToken cancellationToken)
    {
        if (request.SessionId.HasValue)
        {
            var existing = await _dbContext.AiChatSessions
                .FirstOrDefaultAsync(item => item.UserId == userId && item.Id == request.SessionId.Value && !item.IsArchived, cancellationToken);
            if (existing != null)
            {
                return existing;
            }
        }

        var session = new AiChatSessionRecord
        {
            UserId = userId,
            Title = NormalizeTitle(request.Question),
            Symbol = request.Symbol ?? string.Empty,
            SkillId = request.SkillId ?? string.Empty,
            ProviderId = request.ProviderId ?? string.Empty,
            Model = request.Model ?? string.Empty,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _dbContext.AiChatSessions.Add(session);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return session;
    }

    private async Task<List<string>> LoadConversationContextAsync(int userId, long sessionId, CancellationToken cancellationToken)
    {
        var messages = await _dbContext.AiChatMessages
            .Where(item => item.UserId == userId && item.SessionId == sessionId)
            .OrderByDescending(item => item.CreatedAt)
            .Take(12)
            .ToListAsync(cancellationToken);

        return messages
            .OrderBy(item => item.CreatedAt)
            .Select(item => $"{(item.Role == "user" ? "用户" : "AI")}：{TrimContext(item.Content, 360)}")
            .ToList();
    }

    private async Task<List<string>> LoadMemoryContextAsync(int userId, string question, string? symbol, CancellationToken cancellationToken)
    {
        var normalizedSymbol = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        var memories = await _dbContext.AiMemories
            .Where(item => item.UserId == userId && !item.IsArchived)
            .OrderByDescending(item => item.Priority)
            .ThenByDescending(item => item.UpdatedAt)
            .Take(20)
            .ToListAsync(cancellationToken);

        return memories
            .Where(item => string.IsNullOrWhiteSpace(item.Symbol)
                || string.IsNullOrWhiteSpace(normalizedSymbol)
                || item.Symbol.Equals(normalizedSymbol, StringComparison.OrdinalIgnoreCase)
                || question.Contains(item.Symbol, StringComparison.OrdinalIgnoreCase))
            .Take(8)
            .Select(item => $"{item.Title} {item.Content}".Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();
    }

    private async Task UpsertHeuristicMemoryAsync(int userId, string question, string answer, string? symbol, CancellationToken cancellationToken)
    {
        var text = $"{question}\n{answer}";
        var keywords = new[] { "我偏好", "我喜欢", "我的风格", "风险偏好", "仓位", "止损", "只做", "关注" };
        if (!keywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var content = TrimContext(question, 500);
        var exists = await _dbContext.AiMemories.AnyAsync(
            item => item.UserId == userId && item.Content == content && !item.IsArchived,
            cancellationToken);
        if (exists)
        {
            return;
        }

        _dbContext.AiMemories.Add(new AiMemoryRecord
        {
            UserId = userId,
            Type = "preference",
            Title = "用户偏好",
            Content = content,
            Symbol = symbol?.Trim().ToUpperInvariant() ?? string.Empty,
            Tags = "auto",
            Priority = 2,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
    }

    private static string NormalizeTitle(string value)
    {
        var clean = (value ?? string.Empty).Trim().Replace("\n", " ");
        if (string.IsNullOrWhiteSpace(clean))
        {
            return "新会话";
        }

        return clean.Length <= 36 ? clean : $"{clean[..36]}...";
    }

    private static string TrimContext(string value, int length)
    {
        var clean = (value ?? string.Empty).Trim();
        return clean.Length <= length ? clean : $"{clean[..length]}...";
    }
}

public sealed class AnalyzeStockRequest
{
    public string Period { get; set; } = "D";
    public DateTime? Start { get; set; }
    public DateTime? End { get; set; }
    public int? Count { get; set; }
    public string Focus { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
}

public sealed class AiChatRequest
{
    public string Question { get; set; } = string.Empty;
    public string? Symbol { get; set; }
    public string? Focus { get; set; }
    public string? SkillId { get; set; }
    public string? ProviderId { get; set; }
    public string? Model { get; set; }
    public long? SessionId { get; set; }
    public long? KnowledgeBaseId { get; set; }
    public bool? UseMemory { get; set; }
}

public sealed class AiChatSessionCreateRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Symbol { get; set; }
    public string? SkillId { get; set; }
    public string? ProviderId { get; set; }
    public string? Model { get; set; }
}

public sealed class AiChatSessionDto
{
    public long Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string SkillId { get; init; } = string.Empty;
    public string ProviderId { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    public static AiChatSessionDto From(AiChatSessionRecord row)
    {
        return new AiChatSessionDto
        {
            Id = row.Id,
            Title = row.Title,
            Symbol = row.Symbol,
            SkillId = row.SkillId,
            ProviderId = row.ProviderId,
            Model = row.Model,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt
        };
    }
}

public sealed class AiChatSessionDetailDto
{
    public AiChatSessionDto Session { get; init; } = new();
    public List<AiChatMessageDto> Messages { get; init; } = new();
}

public sealed class AiChatMessageDto
{
    public long Id { get; init; }
    public string Role { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public AiChatMarketContext? MarketContext { get; init; }
    public bool IsError { get; init; }
    public DateTime CreatedAt { get; init; }

    public static AiChatMessageDto From(AiChatMessageRecord row)
    {
        AiChatMarketContext? marketContext = null;
        if (!string.IsNullOrWhiteSpace(row.MarketContextJson))
        {
            try
            {
                marketContext = JsonConvert.DeserializeObject<AiChatMarketContext>(row.MarketContextJson);
            }
            catch
            {
                marketContext = null;
            }
        }

        return new AiChatMessageDto
        {
            Id = row.Id,
            Role = row.Role,
            Content = row.Content,
            Model = row.Model,
            MarketContext = marketContext,
            IsError = row.IsError,
            CreatedAt = row.CreatedAt
        };
    }
}

public sealed class AiPromptOptimizeRequest
{
    public string Question { get; set; } = string.Empty;
    public string? Symbol { get; set; }
    public string? ProviderId { get; set; }
    public string? Model { get; set; }
}

public sealed class AiModelsRequest
{
    public string? ProviderId { get; set; }
    public string? BaseUrl { get; set; }
    public string? ApiKey { get; set; }
}
