using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuantTrading.Api.Models;
using QuantTrading.Api.Services.Auth;
using QuantTrading.Api.Services.Reader;
using Microsoft.AspNetCore.Http.Features;

namespace QuantTrading.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/reader")]
public sealed class ReaderController : ControllerBase
{
    private readonly ICurrentUserService _currentUser;
    private readonly IReaderService _readerService;
    private readonly ILogger<ReaderController> _logger;

    public ReaderController(
        ICurrentUserService currentUser,
        IReaderService readerService,
        ILogger<ReaderController> logger)
    {
        _currentUser = currentUser;
        _readerService = readerService;
        _logger = logger;
    }

    [HttpPost("books/upload")]
    public async Task<ActionResult<ReaderBook>> UploadBook(
        [FromForm] ReaderUploadRequest request,
        CancellationToken cancellationToken)
    {
        var file = request.File;
        if (file == null && Request.HasFormContentType)
        {
            try
            {
                var form = await Request.ReadFormAsync(cancellationToken);
                file = form.Files.FirstOrDefault();
            }
            catch (InvalidDataException ex)
            {
                _logger.LogWarning(ex, "Reader upload form parsing failed. ContentType={ContentType}, ContentLength={ContentLength}", Request.ContentType, Request.ContentLength);
                return BadRequest(new { message = "上传表单解析失败，请重试。" });
            }
        }

        if (file == null)
        {
            _logger.LogWarning("Reader upload request missing file. ContentType={ContentType}, ContentLength={ContentLength}", Request.ContentType, Request.ContentLength);
            return BadRequest(new { message = "请选择要上传的 EPUB 或 PDF 文件。" });
        }

        var userId = await _currentUser.GetEffectiveUserIdAsync(cancellationToken);
        try
        {
            var book = await _readerService.UploadBookAsync(userId, file, cancellationToken);
            return Ok(book);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("books/import-crawler/{crawlerDocumentId:long}")]
    public async Task<ActionResult<ReaderBook>> ImportCrawlerDocument(long crawlerDocumentId, CancellationToken cancellationToken)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync(cancellationToken);
        try
        {
            var book = await _readerService.ImportCrawlerDocumentAsync(userId, crawlerDocumentId, cancellationToken);
            return Ok(book);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("books")]
    public async Task<ActionResult<List<ReaderBook>>> ListBooks(CancellationToken cancellationToken)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync(cancellationToken);
        return Ok(await _readerService.ListBooksAsync(userId, cancellationToken));
    }

    [HttpGet("books/{id:long}")]
    public async Task<ActionResult<ReaderBook>> GetBook(long id, CancellationToken cancellationToken)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync(cancellationToken);
        var book = await _readerService.GetBookAsync(userId, id, cancellationToken);
        return book == null ? NotFound() : Ok(book);
    }

    [HttpDelete("books/{id:long}")]
    public async Task<ActionResult> DeleteBook(long id, CancellationToken cancellationToken)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync(cancellationToken);
        var deleted = await _readerService.DeleteBookAsync(userId, id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    [HttpGet("books/{id:long}/content")]
    public async Task<ActionResult> GetBookContent(long id, CancellationToken cancellationToken)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync(cancellationToken);
        var book = await _readerService.GetBookAsync(userId, id, cancellationToken);
        if (book == null)
        {
            return NotFound(new { message = "图书不存在或无权限访问。" });
        }

        var content = await _readerService.GetBookContentAsync(userId, id, cancellationToken);
        if (content == null)
        {
            if (string.Equals(book.SourceType, "upload", StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(StatusCodes.Status410Gone, new
                {
                    message = "图书文件不存在，可能因容器重建被清理。请删除后重新上传。"
                });
            }

            return NotFound(new { message = "图书内容不存在。" });
        }

        return File(content.Data, content.ContentType);
    }

    [HttpGet("books/{id:long}/progress")]
    public async Task<ActionResult<ReaderProgress>> GetProgress(long id, CancellationToken cancellationToken)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync(cancellationToken);
        var book = await _readerService.GetBookAsync(userId, id, cancellationToken);
        if (book == null)
        {
            return NotFound();
        }

        var progress = await _readerService.GetProgressAsync(userId, id, cancellationToken);
        return Ok(progress);
    }

    [HttpPut("books/{id:long}/progress")]
    public async Task<ActionResult<ReaderProgress>> UpsertProgress(
        long id,
        [FromBody] ReaderProgressUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync(cancellationToken);
        var progress = await _readerService.UpsertProgressAsync(userId, id, request, cancellationToken);
        return progress == null ? NotFound() : Ok(progress);
    }

    [HttpGet("books/{id:long}/highlights")]
    public async Task<ActionResult<List<ReaderHighlight>>> ListHighlights(long id, CancellationToken cancellationToken)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync(cancellationToken);
        var book = await _readerService.GetBookAsync(userId, id, cancellationToken);
        if (book == null)
        {
            return NotFound();
        }

        return Ok(await _readerService.ListHighlightsAsync(userId, id, cancellationToken));
    }

    [HttpPost("books/{id:long}/highlights")]
    public async Task<ActionResult<ReaderHighlight>> CreateHighlight(
        long id,
        [FromBody] ReaderHighlightUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync(cancellationToken);
        try
        {
            var highlight = await _readerService.CreateHighlightAsync(userId, id, request, cancellationToken);
            return highlight == null ? NotFound() : Ok(highlight);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("books/{id:long}/highlights/{highlightId:long}")]
    public async Task<ActionResult<ReaderHighlight>> UpdateHighlight(
        long id,
        long highlightId,
        [FromBody] ReaderHighlightUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync(cancellationToken);
        var highlight = await _readerService.UpdateHighlightAsync(userId, id, highlightId, request, cancellationToken);
        return highlight == null ? NotFound() : Ok(highlight);
    }

    [HttpDelete("books/{id:long}/highlights/{highlightId:long}")]
    public async Task<ActionResult> DeleteHighlight(long id, long highlightId, CancellationToken cancellationToken)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync(cancellationToken);
        var deleted = await _readerService.DeleteHighlightAsync(userId, id, highlightId, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}

public sealed class ReaderUploadRequest
{
    public IFormFile? File { get; set; }
}
