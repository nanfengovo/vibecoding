using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using QuantTrading.Api.Data;
using QuantTrading.Api.Models;

namespace QuantTrading.Api.Services.Reader;

public sealed class ReaderService : IReaderService
{
    private const long DefaultMaxUploadBytes = 100L * 1024L * 1024L; // 100MB
    private static readonly HashSet<string> ValidColors = new(StringComparer.OrdinalIgnoreCase)
    {
        "yellow", "green", "blue", "pink", "orange", "purple", "gray"
    };

    private readonly QuantTradingDbContext _dbContext;
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ReaderService> _logger;

    public ReaderService(
        QuantTradingDbContext dbContext,
        IWebHostEnvironment environment,
        IConfiguration configuration,
        ILogger<ReaderService> logger)
    {
        _dbContext = dbContext;
        _environment = environment;
        _configuration = configuration;
        _logger = logger;
    }

    public Task<List<ReaderBook>> ListBooksAsync(int userId, CancellationToken cancellationToken = default)
    {
        return _dbContext.ReaderBooks
            .Where(item => item.UserId == userId)
            .OrderByDescending(item => item.UpdatedAt)
            .Take(500)
            .ToListAsync(cancellationToken);
    }

    public Task<ReaderBook?> GetBookAsync(int userId, long bookId, CancellationToken cancellationToken = default)
    {
        return _dbContext.ReaderBooks
            .FirstOrDefaultAsync(item => item.UserId == userId && item.Id == bookId, cancellationToken);
    }

    public async Task<ReaderBook> UploadBookAsync(int userId, IFormFile file, CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length <= 0)
        {
            throw new InvalidOperationException("请上传 EPUB 或 PDF 文件。");
        }

        var extension = Path.GetExtension(file.FileName ?? string.Empty).Trim().ToLowerInvariant();
        var format = extension switch
        {
            ".epub" => "EPUB",
            ".pdf" => "PDF",
            _ => string.Empty
        };
        if (string.IsNullOrWhiteSpace(format))
        {
            throw new InvalidOperationException("当前仅支持 EPUB 和 PDF 文件。");
        }

        var maxUploadBytes = ResolveMaxUploadBytes();
        if (file.Length > maxUploadBytes)
        {
            throw new InvalidOperationException($"文件过大，限制为 {maxUploadBytes / (1024 * 1024)}MB。");
        }

        var relativePath = BuildStoragePath(userId, extension);
        var absolutePath = ResolveAbsolutePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        long totalBytes = 0;
        string contentHash;
        await using (var input = file.OpenReadStream())
        await using (var output = File.Create(absolutePath))
        using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            var buffer = ArrayPool<byte>.Shared.Rent(1024 * 64);
            try
            {
                while (true)
                {
                    var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                    if (read <= 0)
                    {
                        break;
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    hasher.AppendData(buffer, 0, read);
                    totalBytes += read;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            contentHash = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
        }

        var existing = await _dbContext.ReaderBooks
            .FirstOrDefaultAsync(item => item.UserId == userId && item.ContentHash == contentHash, cancellationToken);
        if (existing != null)
        {
            try
            {
                if (File.Exists(absolutePath))
                {
                    File.Delete(absolutePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanup duplicate reader upload file for user {UserId}", userId);
            }

            return existing;
        }

        var originalName = SanitizeFileName(Path.GetFileName(file.FileName));
        var book = new ReaderBook
        {
            UserId = userId,
            Title = string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(originalName))
                ? "未命名图书"
                : Path.GetFileNameWithoutExtension(originalName),
            Format = format,
            SourceType = "upload",
            SourceRef = string.Empty,
            ContentHash = contentHash,
            FileName = string.IsNullOrWhiteSpace(originalName) ? $"{Guid.NewGuid():N}{extension}" : originalName,
            StoragePath = relativePath,
            FileSize = totalBytes,
            Markdown = string.Empty,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.ReaderBooks.Add(book);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return book;
    }

    public async Task<ReaderBook> ImportCrawlerDocumentAsync(int userId, long crawlerDocumentId, CancellationToken cancellationToken = default)
    {
        var crawlerDoc = await _dbContext.CrawlerDocuments
            .FirstOrDefaultAsync(item => item.UserId == userId && item.Id == crawlerDocumentId, cancellationToken);
        if (crawlerDoc == null)
        {
            throw new InvalidOperationException("采集文档不存在。");
        }

        var existing = await _dbContext.ReaderBooks
            .FirstOrDefaultAsync(item =>
                item.UserId == userId
                && item.SourceType == "crawler"
                && item.SourceRef == crawlerDocumentId.ToString(),
                cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var markdown = crawlerDoc.Markdown?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(markdown))
        {
            markdown = $"# {crawlerDoc.Title}\n\n{crawlerDoc.Summary}";
        }

        var bytes = Encoding.UTF8.GetBytes(markdown);
        var contentHash = ComputeHash(bytes);
        var relativePath = BuildStoragePath(userId, ".md");
        var absolutePath = ResolveAbsolutePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        await File.WriteAllBytesAsync(absolutePath, bytes, cancellationToken);

        var title = string.IsNullOrWhiteSpace(crawlerDoc.Title) ? "采集文档" : crawlerDoc.Title.Trim();
        var fileName = SanitizeFileName($"{title}.md");
        var book = new ReaderBook
        {
            UserId = userId,
            Title = title,
            Author = string.Empty,
            Format = "MD",
            SourceType = "crawler",
            SourceRef = crawlerDocumentId.ToString(),
            ContentHash = contentHash,
            FileName = string.IsNullOrWhiteSpace(fileName) ? $"{Guid.NewGuid():N}.md" : fileName,
            StoragePath = relativePath,
            FileSize = bytes.LongLength,
            Markdown = markdown,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.ReaderBooks.Add(book);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return book;
    }

    public async Task<bool> DeleteBookAsync(int userId, long bookId, CancellationToken cancellationToken = default)
    {
        var book = await _dbContext.ReaderBooks
            .FirstOrDefaultAsync(item => item.UserId == userId && item.Id == bookId, cancellationToken);
        if (book == null)
        {
            return false;
        }

        var storagePath = book.StoragePath;
        _dbContext.ReaderHighlights.RemoveRange(_dbContext.ReaderHighlights.Where(item => item.UserId == userId && item.BookId == bookId));
        _dbContext.ReaderProgresses.RemoveRange(_dbContext.ReaderProgresses.Where(item => item.UserId == userId && item.BookId == bookId));
        _dbContext.ReaderBooks.Remove(book);
        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var hasReference = await _dbContext.ReaderBooks.AnyAsync(item => item.StoragePath == storagePath, cancellationToken);
            if (!hasReference && !string.IsNullOrWhiteSpace(storagePath))
            {
                var absolutePath = ResolveAbsolutePath(storagePath);
                if (File.Exists(absolutePath))
                {
                    File.Delete(absolutePath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup reader file for book {BookId}", bookId);
        }

        return true;
    }

    public async Task<ReaderBookContentResult?> GetBookContentAsync(int userId, long bookId, CancellationToken cancellationToken = default)
    {
        var book = await GetBookAsync(userId, bookId, cancellationToken);
        if (book == null)
        {
            return null;
        }

        byte[] bytes;
        if (!string.IsNullOrWhiteSpace(book.StoragePath))
        {
            var absolutePath = ResolveAbsolutePath(book.StoragePath);
            if (File.Exists(absolutePath))
            {
                bytes = await File.ReadAllBytesAsync(absolutePath, cancellationToken);
            }
            else if (!string.IsNullOrWhiteSpace(book.Markdown))
            {
                bytes = Encoding.UTF8.GetBytes(book.Markdown);
            }
            else
            {
                return null;
            }
        }
        else if (!string.IsNullOrWhiteSpace(book.Markdown))
        {
            bytes = Encoding.UTF8.GetBytes(book.Markdown);
        }
        else
        {
            return null;
        }

        return new ReaderBookContentResult
        {
            Data = bytes,
            ContentType = ResolveContentType(book.Format),
            FileName = string.IsNullOrWhiteSpace(book.FileName) ? $"{book.Id}.{book.Format.ToLowerInvariant()}" : book.FileName,
            Format = book.Format
        };
    }

    public async Task<ReaderProgress?> GetProgressAsync(int userId, long bookId, CancellationToken cancellationToken = default)
    {
        var exists = await _dbContext.ReaderBooks.AnyAsync(item => item.UserId == userId && item.Id == bookId, cancellationToken);
        if (!exists)
        {
            return null;
        }

        return await _dbContext.ReaderProgresses
            .FirstOrDefaultAsync(item => item.UserId == userId && item.BookId == bookId, cancellationToken);
    }

    public async Task<ReaderProgress?> UpsertProgressAsync(
        int userId,
        long bookId,
        ReaderProgressUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        var book = await _dbContext.ReaderBooks
            .FirstOrDefaultAsync(item => item.UserId == userId && item.Id == bookId, cancellationToken);
        if (book == null)
        {
            return null;
        }

        var progress = await _dbContext.ReaderProgresses
            .FirstOrDefaultAsync(item => item.UserId == userId && item.BookId == bookId, cancellationToken);

        if (progress == null)
        {
            progress = new ReaderProgress
            {
                UserId = userId,
                BookId = bookId,
                Locator = request.Locator?.Trim() ?? string.Empty,
                ChapterTitle = request.ChapterTitle?.Trim() ?? string.Empty,
                PageNumber = request.PageNumber,
                Percentage = request.Percentage,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _dbContext.ReaderProgresses.Add(progress);
        }
        else
        {
            progress.Locator = request.Locator?.Trim() ?? string.Empty;
            progress.ChapterTitle = request.ChapterTitle?.Trim() ?? string.Empty;
            progress.PageNumber = request.PageNumber;
            progress.Percentage = request.Percentage;
            progress.UpdatedAt = DateTime.UtcNow;
        }

        book.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return progress;
    }

    public async Task<List<ReaderHighlight>> ListHighlightsAsync(int userId, long bookId, CancellationToken cancellationToken = default)
    {
        var exists = await _dbContext.ReaderBooks.AnyAsync(item => item.UserId == userId && item.Id == bookId, cancellationToken);
        if (!exists)
        {
            return new List<ReaderHighlight>();
        }

        return await _dbContext.ReaderHighlights
            .Where(item => item.UserId == userId && item.BookId == bookId)
            .OrderByDescending(item => item.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<ReaderHighlight?> CreateHighlightAsync(
        int userId,
        long bookId,
        ReaderHighlightUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        var book = await _dbContext.ReaderBooks
            .FirstOrDefaultAsync(item => item.UserId == userId && item.Id == bookId, cancellationToken);
        if (book == null)
        {
            return null;
        }

        var text = request.SelectedText?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("划线内容不能为空。");
        }

        var highlight = new ReaderHighlight
        {
            UserId = userId,
            BookId = bookId,
            Locator = request.Locator?.Trim() ?? string.Empty,
            ChapterTitle = request.ChapterTitle?.Trim() ?? string.Empty,
            SelectedText = text,
            Note = request.Note?.Trim() ?? string.Empty,
            Color = NormalizeColor(request.Color),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.ReaderHighlights.Add(highlight);
        book.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return highlight;
    }

    public async Task<ReaderHighlight?> UpdateHighlightAsync(
        int userId,
        long bookId,
        long highlightId,
        ReaderHighlightUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        var highlight = await _dbContext.ReaderHighlights
            .FirstOrDefaultAsync(item =>
                item.UserId == userId
                && item.BookId == bookId
                && item.Id == highlightId,
                cancellationToken);
        if (highlight == null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(request.SelectedText))
        {
            highlight.SelectedText = request.SelectedText.Trim();
        }

        highlight.Locator = request.Locator?.Trim() ?? highlight.Locator;
        highlight.ChapterTitle = request.ChapterTitle?.Trim() ?? highlight.ChapterTitle;
        highlight.Note = request.Note?.Trim() ?? string.Empty;
        highlight.Color = NormalizeColor(request.Color);
        highlight.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return highlight;
    }

    public async Task<bool> DeleteHighlightAsync(
        int userId,
        long bookId,
        long highlightId,
        CancellationToken cancellationToken = default)
    {
        var highlight = await _dbContext.ReaderHighlights
            .FirstOrDefaultAsync(item =>
                item.UserId == userId
                && item.BookId == bookId
                && item.Id == highlightId,
                cancellationToken);
        if (highlight == null)
        {
            return false;
        }

        _dbContext.ReaderHighlights.Remove(highlight);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private long ResolveMaxUploadBytes()
    {
        var raw = _configuration["Reader:MaxUploadBytes"];
        if (long.TryParse(raw, out var parsed) && parsed > 0)
        {
            return parsed;
        }

        return DefaultMaxUploadBytes;
    }

    private string BuildStoragePath(int userId, string extension)
    {
        var ext = extension.StartsWith('.') ? extension : $".{extension}";
        var folder = Path.Combine(userId.ToString(), DateTime.UtcNow.ToString("yyyyMM"));
        var fileName = $"{Guid.NewGuid():N}{ext}";
        return Path.Combine(folder, fileName).Replace('\\', '/');
    }

    private string ResolveAbsolutePath(string relativePath)
    {
        var root = ResolveStorageRoot();
        var normalized = (relativePath ?? string.Empty).Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(root, normalized);
    }

    private string ResolveStorageRoot()
    {
        var configured = (_configuration["Reader:StorageRoot"] ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(configured))
        {
            return Path.Combine(_environment.ContentRootPath, "storage", "reader-books");
        }

        return Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(_environment.ContentRootPath, configured);
    }

    private static string ResolveContentType(string format)
    {
        return (format ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "EPUB" => "application/epub+zip",
            "PDF" => "application/pdf",
            "MD" => "text/markdown; charset=utf-8",
            _ => "application/octet-stream"
        };
    }

    private static string ComputeHash(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string NormalizeColor(string? color)
    {
        var normalized = (color ?? string.Empty).Trim().ToLowerInvariant();
        return ValidColors.Contains(normalized) ? normalized : "yellow";
    }

    private static string SanitizeFileName(string value)
    {
        var clean = string.Join("_", (value ?? string.Empty).Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (string.IsNullOrWhiteSpace(clean))
        {
            return string.Empty;
        }

        return clean.Length <= 180 ? clean : clean[..180];
    }
}
