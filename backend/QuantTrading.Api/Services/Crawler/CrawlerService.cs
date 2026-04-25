using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using QuantTrading.Api.Data;
using QuantTrading.Api.Models;

namespace QuantTrading.Api.Services.Crawler;

public sealed class CrawlerService : ICrawlerService
{
    private readonly QuantTradingDbContext _dbContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CrawlerService> _logger;

    public CrawlerService(
        QuantTradingDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        ILogger<CrawlerService> logger)
    {
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public Task<List<CrawlerSource>> ListSourcesAsync(int userId, CancellationToken cancellationToken = default)
    {
        return _dbContext.CrawlerSources
            .Where(item => item.UserId == userId)
            .OrderByDescending(item => item.UpdatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<CrawlerSource> CreateSourceAsync(int userId, CrawlerSource source, CancellationToken cancellationToken = default)
    {
        var entity = NormalizeSource(userId, source);
        _dbContext.CrawlerSources.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<CrawlerSource?> UpdateSourceAsync(int userId, long id, CrawlerSource source, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.CrawlerSources.FirstOrDefaultAsync(item => item.UserId == userId && item.Id == id, cancellationToken);
        if (entity == null)
        {
            return null;
        }

        entity.Name = string.IsNullOrWhiteSpace(source.Name) ? entity.Name : source.Name.Trim();
        entity.Type = NormalizeSourceType(source.Type);
        entity.Url = NormalizeSourceUrl(source);
        entity.Symbol = NormalizeSymbol(source.Symbol);
        entity.Tags = source.Tags?.Trim() ?? string.Empty;
        entity.IsEnabled = source.IsEnabled;
        entity.CrawlIntervalMinutes = Math.Clamp(source.CrawlIntervalMinutes <= 0 ? 360 : source.CrawlIntervalMinutes, 15, 10080);
        entity.MaxPages = Math.Clamp(source.MaxPages <= 0 ? 5 : source.MaxPages, 1, 30);
        entity.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<bool> DeleteSourceAsync(int userId, long id, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.CrawlerSources.FirstOrDefaultAsync(item => item.UserId == userId && item.Id == id, cancellationToken);
        if (entity == null)
        {
            return false;
        }

        _dbContext.CrawlerSources.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<CrawlerJob> RunSourceAsync(int userId, long sourceId, CancellationToken cancellationToken = default)
    {
        var source = await _dbContext.CrawlerSources
            .FirstOrDefaultAsync(item => item.UserId == userId && item.Id == sourceId, cancellationToken);
        if (source == null)
        {
            throw new InvalidOperationException("采集源不存在。");
        }

        var job = new CrawlerJob
        {
            UserId = userId,
            SourceId = source.Id,
            Status = "running",
            StartedAt = DateTime.UtcNow
        };
        _dbContext.CrawlerJobs.Add(job);
        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var docs = await FetchDocumentsAsync(source, cancellationToken);
            job.DocumentsFound = docs.Count;

            foreach (var doc in docs)
            {
                var hash = ComputeHash(doc.Markdown);
                var exists = await _dbContext.CrawlerDocuments.AnyAsync(
                    item => item.UserId == userId && item.ContentHash == hash,
                    cancellationToken);
                if (exists)
                {
                    continue;
                }

                doc.UserId = userId;
                doc.SourceId = source.Id;
                doc.ContentHash = hash;
                doc.Tags = source.Tags;
                doc.Symbol = string.IsNullOrWhiteSpace(doc.Symbol) ? source.Symbol : doc.Symbol;
                doc.CreatedAt = DateTime.UtcNow;
                _dbContext.CrawlerDocuments.Add(doc);
                job.DocumentsSaved++;
            }

            source.LastRunAt = DateTime.UtcNow;
            source.UpdatedAt = DateTime.UtcNow;
            job.Status = "completed";
            job.FinishedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Crawler source failed: {SourceId}", sourceId);
            job.Status = "failed";
            job.ErrorMessage = ex.Message;
            job.FinishedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return job;
    }

    public async Task<int> RunDueSourcesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var dueSources = await _dbContext.CrawlerSources
            .Where(source => source.IsEnabled
                && (source.LastRunAt == null
                    || source.LastRunAt.Value.AddMinutes(source.CrawlIntervalMinutes) <= now))
            .OrderBy(source => source.LastRunAt ?? DateTime.MinValue)
            .Take(20)
            .ToListAsync(cancellationToken);

        var ran = 0;
        foreach (var source in dueSources)
        {
            await RunSourceAsync(source.UserId, source.Id, cancellationToken);
            ran++;
        }

        return ran;
    }

    public Task<List<CrawlerDocument>> ListDocumentsAsync(
        int userId,
        long? sourceId = null,
        string? symbol = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.CrawlerDocuments.Where(item => item.UserId == userId);
        if (sourceId.HasValue)
        {
            query = query.Where(item => item.SourceId == sourceId.Value);
        }

        var normalizedSymbol = NormalizeSymbol(symbol);
        if (!string.IsNullOrWhiteSpace(normalizedSymbol))
        {
            query = query.Where(item => item.Symbol == normalizedSymbol);
        }

        return query
            .OrderByDescending(item => item.PublishedAt ?? item.CreatedAt)
            .Take(200)
            .ToListAsync(cancellationToken);
    }

    private async Task<List<CrawlerDocument>> FetchDocumentsAsync(CrawlerSource source, CancellationToken cancellationToken)
    {
        var url = NormalizeSourceUrl(source);
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException("采集 URL 不能为空。");
        }

        var markdown = await FetchTextAsync(url, cancellationToken);
        if (source.Type.Equals("rss", StringComparison.OrdinalIgnoreCase))
        {
            return ParseRss(markdown, source.MaxPages);
        }

        return new List<CrawlerDocument>
        {
            new()
            {
                Title = ExtractTitle(markdown, source.Name),
                Url = url,
                Markdown = markdown,
                Summary = BuildSummary(markdown),
                Symbol = source.Symbol
            }
        };
    }

    private async Task<string> FetchTextAsync(string url, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("Crawler");
        using var response = await client.GetAsync(url, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"采集失败：HTTP {(int)response.StatusCode}");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("采集内容为空。");
        }

        return content;
    }

    private static List<CrawlerDocument> ParseRss(string xml, int maxPages)
    {
        var doc = XDocument.Parse(xml);
        return doc.Descendants("item")
            .Take(Math.Clamp(maxPages, 1, 30))
            .Select(item =>
            {
                var title = item.Element("title")?.Value?.Trim() ?? "RSS 条目";
                var link = item.Element("link")?.Value?.Trim() ?? string.Empty;
                var description = item.Element("description")?.Value?.Trim() ?? string.Empty;
                var publishedRaw = item.Element("pubDate")?.Value;
                DateTime? publishedAt = DateTime.TryParse(publishedRaw, out var parsed) ? parsed.ToUniversalTime() : null;
                return new CrawlerDocument
                {
                    Title = title,
                    Url = link,
                    Markdown = $"# {title}\n\n{StripHtml(description)}\n\n来源：{link}",
                    Summary = StripHtml(description),
                    PublishedAt = publishedAt
                };
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Markdown))
            .ToList();
    }

    private static CrawlerSource NormalizeSource(int userId, CrawlerSource source)
    {
        return new CrawlerSource
        {
            UserId = userId,
            Name = string.IsNullOrWhiteSpace(source.Name) ? "长桥资讯" : source.Name.Trim(),
            Type = NormalizeSourceType(source.Type),
            Url = NormalizeSourceUrl(source),
            Symbol = NormalizeSymbol(source.Symbol),
            Tags = source.Tags?.Trim() ?? string.Empty,
            IsEnabled = source.IsEnabled,
            CrawlIntervalMinutes = Math.Clamp(source.CrawlIntervalMinutes <= 0 ? 360 : source.CrawlIntervalMinutes, 15, 10080),
            MaxPages = Math.Clamp(source.MaxPages <= 0 ? 5 : source.MaxPages, 1, 30),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static string NormalizeSourceType(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "longbridge_quote" or "longbridge_news" or "rss" or "markdown" or "web"
            ? normalized
            : "longbridge_news";
    }

    private static string NormalizeSourceUrl(CrawlerSource source)
    {
        var type = NormalizeSourceType(source.Type);
        var symbol = NormalizeSymbol(source.Symbol);
        if (type == "longbridge_quote" && !string.IsNullOrWhiteSpace(symbol))
        {
            return $"https://longbridge.com/zh-CN/quote/{symbol}.md";
        }

        if (type == "longbridge_news" && string.IsNullOrWhiteSpace(source.Url))
        {
            return "https://longbridge.com/zh-CN/news.md";
        }

        var url = source.Url?.Trim() ?? string.Empty;
        if (url.StartsWith("https://longbridge.com/", StringComparison.OrdinalIgnoreCase)
            && !url.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            && (type == "longbridge_news" || type == "longbridge_quote"))
        {
            return $"{url.TrimEnd('/')}.md";
        }

        return url;
    }

    private static string NormalizeSymbol(string? symbol)
    {
        var normalized = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Contains('.'))
        {
            return normalized;
        }

        if (normalized.All(char.IsDigit) && normalized.Length == 6)
        {
            return normalized.StartsWith('6') ? $"{normalized}.SH" : $"{normalized}.SZ";
        }

        if (normalized.All(char.IsDigit) && normalized.Length == 5)
        {
            return $"{normalized}.HK";
        }

        return $"{normalized}.US";
    }

    private static string ExtractTitle(string markdown, string fallback)
    {
        var heading = markdown.Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("# ", StringComparison.Ordinal));
        return string.IsNullOrWhiteSpace(heading)
            ? fallback
            : heading.TrimStart('#').Trim();
    }

    private static string BuildSummary(string markdown)
    {
        var text = StripHtml(Regex.Replace(markdown, @"[#>*_`\[\]\(\)\|]", " "));
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length <= 500 ? text : $"{text[..500]}...";
    }

    private static string StripHtml(string input)
    {
        return Regex.Replace(input ?? string.Empty, "<.*?>", " ").Trim();
    }

    private static string ComputeHash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
