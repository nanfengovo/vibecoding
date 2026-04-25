using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuantTrading.Api.Data;
using QuantTrading.Api.Models;
using QuantTrading.Api.Services.AI;
using QuantTrading.Api.Services.Auth;
using QuantTrading.Api.Services.Knowledge;

namespace QuantTrading.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/knowledge-bases")]
public sealed class KnowledgeBasesController : ControllerBase
{
    private readonly QuantTradingDbContext _dbContext;
    private readonly ICurrentUserService _currentUser;
    private readonly IKnowledgeService _knowledgeService;
    private readonly IAiAnalysisService _aiAnalysisService;

    public KnowledgeBasesController(
        QuantTradingDbContext dbContext,
        ICurrentUserService currentUser,
        IKnowledgeService knowledgeService,
        IAiAnalysisService aiAnalysisService)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _knowledgeService = knowledgeService;
        _aiAnalysisService = aiAnalysisService;
    }

    [HttpGet]
    public async Task<ActionResult<List<KnowledgeBase>>> List(CancellationToken cancellationToken)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync(cancellationToken);
        return Ok(await _knowledgeService.ListKnowledgeBasesAsync(userId, cancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult<KnowledgeBase>> Create([FromBody] KnowledgeBaseUpsertRequest request, CancellationToken cancellationToken)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync(cancellationToken);
        var kb = await _knowledgeService.CreateKnowledgeBaseAsync(userId, request.Name, request.Description, cancellationToken);
        return CreatedAtAction(nameof(List), new { id = kb.Id }, kb);
    }

    [HttpPut("{id:long}")]
    public async Task<ActionResult<KnowledgeBase>> Update(long id, [FromBody] KnowledgeBaseUpsertRequest request, CancellationToken cancellationToken)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync(cancellationToken);
        var kb = await _knowledgeService.UpdateKnowledgeBaseAsync(userId, id, request.Name, request.Description, cancellationToken);
        return kb == null ? NotFound() : Ok(kb);
    }

    [HttpDelete("{id:long}")]
    public async Task<ActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync(cancellationToken);
        var kb = await _dbContext.KnowledgeBases.FirstOrDefaultAsync(item => item.UserId == userId && item.Id == id, cancellationToken);
        if (kb == null)
        {
            return NotFound();
        }

        _dbContext.KnowledgeChunks.RemoveRange(_dbContext.KnowledgeChunks.Where(item => item.UserId == userId && item.KnowledgeBaseId == id));
        _dbContext.KnowledgeDocuments.RemoveRange(_dbContext.KnowledgeDocuments.Where(item => item.UserId == userId && item.KnowledgeBaseId == id));
        _dbContext.KnowledgeBases.Remove(kb);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:long}/documents/import-markdown")]
    public async Task<ActionResult<KnowledgeDocument>> ImportMarkdown(
        long id,
        [FromBody] ImportMarkdownRequest request,
        CancellationToken cancellationToken)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync(cancellationToken);
        try
        {
            var doc = await _knowledgeService.ImportMarkdownAsync(
                userId,
                id,
                request.Title,
                request.Markdown,
                request.SourceUrl,
                string.IsNullOrWhiteSpace(request.SourceType) ? "manual_markdown" : request.SourceType,
                cancellationToken);
            return Ok(doc);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{id:long}/documents")]
    public async Task<ActionResult<List<KnowledgeDocument>>> ListDocuments(long id, CancellationToken cancellationToken)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync(cancellationToken);
        var exists = await _dbContext.KnowledgeBases.AnyAsync(item => item.UserId == userId && item.Id == id, cancellationToken);
        if (!exists)
        {
            return NotFound();
        }

        return Ok(await _knowledgeService.ListDocumentsAsync(userId, id, cancellationToken));
    }

    [HttpGet("{id:long}/documents/{documentId:long}")]
    public async Task<ActionResult<KnowledgeDocument>> GetDocument(long id, long documentId, CancellationToken cancellationToken)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync(cancellationToken);
        var doc = await _knowledgeService.GetDocumentAsync(userId, id, documentId, cancellationToken);
        return doc == null ? NotFound() : Ok(doc);
    }

    [HttpPost("{id:long}/documents/import-crawler/{crawlerDocumentId:long}")]
    public async Task<ActionResult<KnowledgeDocument>> ImportCrawlerDocument(
        long id,
        long crawlerDocumentId,
        CancellationToken cancellationToken)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync(cancellationToken);
        var crawlerDoc = await _dbContext.CrawlerDocuments
            .FirstOrDefaultAsync(item => item.UserId == userId && item.Id == crawlerDocumentId, cancellationToken);
        if (crawlerDoc == null)
        {
            return NotFound();
        }

        var doc = await _knowledgeService.ImportMarkdownAsync(
            userId,
            id,
            crawlerDoc.Title,
            crawlerDoc.Markdown,
            crawlerDoc.Url,
            "crawler",
            cancellationToken);
        return Ok(doc);
    }

    [HttpGet("{id:long}/documents/{documentId:long}/export")]
    public async Task<ActionResult> ExportMarkdown(long id, long documentId, CancellationToken cancellationToken)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync(cancellationToken);
        var doc = await _knowledgeService.GetDocumentAsync(userId, id, documentId, cancellationToken);
        if (doc == null)
        {
            return NotFound();
        }

        return File(System.Text.Encoding.UTF8.GetBytes(doc.Markdown), "text/markdown", $"{SanitizeFileName(doc.Title)}.md");
    }

    [HttpPost("{id:long}/chat")]
    public async Task<ActionResult<AiChatResult>> Chat(long id, [FromBody] KnowledgeChatRequest request, CancellationToken cancellationToken)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync(cancellationToken);
        var refs = await _knowledgeService.SearchAsync(userId, id, request.Question, 8, cancellationToken);
        if (refs.Count == 0)
        {
            return BadRequest(new { message = "知识库中没有找到相关内容，请先导入 Markdown 或采集文档。" });
        }

        try
        {
            var result = await _aiAnalysisService.ChatAsync(
                new AiChatInput
                {
                    Question = request.Question,
                    ProviderId = request.ProviderId,
                    Model = request.Model,
                    KnowledgeContext = refs
                },
                cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private static string SanitizeFileName(string value)
    {
        var clean = string.Join("_", (value ?? "document").Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        return string.IsNullOrWhiteSpace(clean) ? "document" : clean;
    }
}

public sealed class KnowledgeBaseUpsertRequest
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class ImportMarkdownRequest
{
    public string Title { get; set; } = string.Empty;
    public string Markdown { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
}

public sealed class KnowledgeChatRequest
{
    public string Question { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
}
