using Microsoft.AspNetCore.Http;
using QuantTrading.Api.Models;

namespace QuantTrading.Api.Services.Reader;

public interface IReaderService
{
    Task<List<ReaderBook>> ListBooksAsync(int userId, CancellationToken cancellationToken = default);
    Task<ReaderBook?> GetBookAsync(int userId, long bookId, CancellationToken cancellationToken = default);
    Task<ReaderBook> UploadBookAsync(int userId, IFormFile file, CancellationToken cancellationToken = default);
    Task<ReaderBook> ImportCrawlerDocumentAsync(int userId, long crawlerDocumentId, CancellationToken cancellationToken = default);
    Task<bool> DeleteBookAsync(int userId, long bookId, CancellationToken cancellationToken = default);
    Task<ReaderBookContentResult?> GetBookContentAsync(int userId, long bookId, CancellationToken cancellationToken = default);
    Task<ReaderProgress?> GetProgressAsync(int userId, long bookId, CancellationToken cancellationToken = default);
    Task<ReaderProgress?> UpsertProgressAsync(int userId, long bookId, ReaderProgressUpsertRequest request, CancellationToken cancellationToken = default);
    Task<List<ReaderHighlight>> ListHighlightsAsync(int userId, long bookId, CancellationToken cancellationToken = default);
    Task<ReaderHighlight?> CreateHighlightAsync(int userId, long bookId, ReaderHighlightUpsertRequest request, CancellationToken cancellationToken = default);
    Task<ReaderHighlight?> UpdateHighlightAsync(int userId, long bookId, long highlightId, ReaderHighlightUpsertRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteHighlightAsync(int userId, long bookId, long highlightId, CancellationToken cancellationToken = default);
}

public sealed class ReaderBookContentResult
{
    public byte[] Data { get; init; } = Array.Empty<byte>();
    public string ContentType { get; init; } = "application/octet-stream";
    public string FileName { get; init; } = "book";
    public string Format { get; init; } = string.Empty;
}

public sealed class ReaderProgressUpsertRequest
{
    public string Locator { get; set; } = string.Empty;
    public string ChapterTitle { get; set; } = string.Empty;
    public int? PageNumber { get; set; }
    public decimal? Percentage { get; set; }
}

public sealed class ReaderHighlightUpsertRequest
{
    public string Locator { get; set; } = string.Empty;
    public string ChapterTitle { get; set; } = string.Empty;
    public string SelectedText { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string Color { get; set; } = "yellow";
}
