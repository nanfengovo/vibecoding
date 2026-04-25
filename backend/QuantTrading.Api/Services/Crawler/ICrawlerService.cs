using QuantTrading.Api.Models;

namespace QuantTrading.Api.Services.Crawler;

public interface ICrawlerService
{
    Task<List<CrawlerSource>> ListSourcesAsync(int userId, CancellationToken cancellationToken = default);
    Task<CrawlerSource> CreateSourceAsync(int userId, CrawlerSource source, CancellationToken cancellationToken = default);
    Task<CrawlerSource?> UpdateSourceAsync(int userId, long id, CrawlerSource source, CancellationToken cancellationToken = default);
    Task<bool> DeleteSourceAsync(int userId, long id, CancellationToken cancellationToken = default);
    Task<CrawlerJob> RunSourceAsync(int userId, long sourceId, CancellationToken cancellationToken = default);
    Task<int> RunDueSourcesAsync(CancellationToken cancellationToken = default);
    Task<List<CrawlerDocument>> ListDocumentsAsync(int userId, long? sourceId = null, string? symbol = null, CancellationToken cancellationToken = default);
}
