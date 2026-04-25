using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuantTrading.Api.Models;
using QuantTrading.Api.Services.Auth;
using QuantTrading.Api.Services.Crawler;

namespace QuantTrading.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/crawler")]
public sealed class CrawlerController : ControllerBase
{
    private readonly ICurrentUserService _currentUser;
    private readonly ICrawlerService _crawlerService;

    public CrawlerController(ICurrentUserService currentUser, ICrawlerService crawlerService)
    {
        _currentUser = currentUser;
        _crawlerService = crawlerService;
    }

    [HttpGet("sources")]
    public async Task<ActionResult<List<CrawlerSource>>> ListSources(CancellationToken cancellationToken)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync(cancellationToken);
        return Ok(await _crawlerService.ListSourcesAsync(userId, cancellationToken));
    }

    [HttpPost("sources")]
    public async Task<ActionResult<CrawlerSource>> CreateSource([FromBody] CrawlerSource source, CancellationToken cancellationToken)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync(cancellationToken);
        var created = await _crawlerService.CreateSourceAsync(userId, source, cancellationToken);
        return CreatedAtAction(nameof(ListSources), new { id = created.Id }, created);
    }

    [HttpPut("sources/{id:long}")]
    public async Task<ActionResult<CrawlerSource>> UpdateSource(long id, [FromBody] CrawlerSource source, CancellationToken cancellationToken)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync(cancellationToken);
        var updated = await _crawlerService.UpdateSourceAsync(userId, id, source, cancellationToken);
        return updated == null ? NotFound() : Ok(updated);
    }

    [HttpDelete("sources/{id:long}")]
    public async Task<ActionResult> DeleteSource(long id, CancellationToken cancellationToken)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync(cancellationToken);
        var deleted = await _crawlerService.DeleteSourceAsync(userId, id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    [HttpPost("sources/{id:long}/run")]
    public async Task<ActionResult<CrawlerJob>> RunSource(long id, CancellationToken cancellationToken)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync(cancellationToken);
        try
        {
            return Ok(await _crawlerService.RunSourceAsync(userId, id, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("documents")]
    public async Task<ActionResult<List<CrawlerDocument>>> ListDocuments(
        [FromQuery] long? sourceId,
        [FromQuery] string? symbol,
        CancellationToken cancellationToken)
    {
        var userId = await _currentUser.GetEffectiveUserIdAsync(cancellationToken);
        return Ok(await _crawlerService.ListDocumentsAsync(userId, sourceId, symbol, cancellationToken));
    }
}
