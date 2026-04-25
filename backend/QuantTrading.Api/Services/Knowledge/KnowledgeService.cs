using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using QuantTrading.Api.Data;
using QuantTrading.Api.Models;
using QuantTrading.Api.Services.AI;

namespace QuantTrading.Api.Services.Knowledge;

public sealed class KnowledgeService : IKnowledgeService
{
    private readonly QuantTradingDbContext _dbContext;

    public KnowledgeService(QuantTradingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<List<KnowledgeBase>> ListKnowledgeBasesAsync(int userId, CancellationToken cancellationToken = default)
    {
        return _dbContext.KnowledgeBases
            .Where(item => item.UserId == userId)
            .OrderByDescending(item => item.UpdatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<KnowledgeBase> CreateKnowledgeBaseAsync(int userId, string name, string description, CancellationToken cancellationToken = default)
    {
        var trimmedName = string.IsNullOrWhiteSpace(name) ? "我的知识库" : name.Trim();
        var exists = await _dbContext.KnowledgeBases.AnyAsync(item => item.UserId == userId, cancellationToken);
        var kb = new KnowledgeBase
        {
            UserId = userId,
            Name = trimmedName,
            Description = description?.Trim() ?? string.Empty,
            IsDefault = !exists,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _dbContext.KnowledgeBases.Add(kb);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return kb;
    }

    public async Task<KnowledgeBase?> UpdateKnowledgeBaseAsync(
        int userId,
        long id,
        string name,
        string description,
        CancellationToken cancellationToken = default)
    {
        var kb = await _dbContext.KnowledgeBases
            .FirstOrDefaultAsync(item => item.UserId == userId && item.Id == id, cancellationToken);
        if (kb == null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            kb.Name = name.Trim();
        }
        kb.Description = description?.Trim() ?? string.Empty;
        kb.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return kb;
    }

    public Task<List<KnowledgeDocument>> ListDocumentsAsync(
        int userId,
        long knowledgeBaseId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.KnowledgeDocuments
            .Where(item => item.UserId == userId && item.KnowledgeBaseId == knowledgeBaseId)
            .OrderByDescending(item => item.UpdatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<KnowledgeDocument> ImportMarkdownAsync(
        int userId,
        long knowledgeBaseId,
        string title,
        string markdown,
        string sourceUrl,
        string sourceType,
        CancellationToken cancellationToken = default)
    {
        var kb = await _dbContext.KnowledgeBases
            .FirstOrDefaultAsync(item => item.Id == knowledgeBaseId && item.UserId == userId, cancellationToken);
        if (kb == null)
        {
            throw new InvalidOperationException("知识库不存在。");
        }

        var cleanMarkdown = markdown?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(cleanMarkdown))
        {
            throw new InvalidOperationException("Markdown 内容不能为空。");
        }

        var contentHash = ComputeHash(cleanMarkdown);
        var doc = await _dbContext.KnowledgeDocuments
            .FirstOrDefaultAsync(item => item.UserId == userId && item.KnowledgeBaseId == knowledgeBaseId && item.ContentHash == contentHash, cancellationToken);
        if (doc == null)
        {
            doc = new KnowledgeDocument
            {
                UserId = userId,
                KnowledgeBaseId = knowledgeBaseId,
                Title = string.IsNullOrWhiteSpace(title) ? ExtractTitle(cleanMarkdown) : title.Trim(),
                SourceUrl = sourceUrl?.Trim() ?? string.Empty,
                SourceType = string.IsNullOrWhiteSpace(sourceType) ? "manual_markdown" : sourceType.Trim(),
                Markdown = cleanMarkdown,
                ContentHash = contentHash,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _dbContext.KnowledgeDocuments.Add(doc);
        }
        else
        {
            doc.Title = string.IsNullOrWhiteSpace(title) ? doc.Title : title.Trim();
            doc.SourceUrl = sourceUrl?.Trim() ?? doc.SourceUrl;
            doc.Markdown = cleanMarkdown;
            doc.UpdatedAt = DateTime.UtcNow;
            _dbContext.KnowledgeChunks.RemoveRange(_dbContext.KnowledgeChunks.Where(item => item.DocumentId == doc.Id));
        }

        kb.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        var chunks = BuildChunks(userId, knowledgeBaseId, doc.Id, cleanMarkdown);
        _dbContext.KnowledgeChunks.AddRange(chunks);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return doc;
    }

    public Task<KnowledgeDocument?> GetDocumentAsync(
        int userId,
        long knowledgeBaseId,
        long documentId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.KnowledgeDocuments
            .FirstOrDefaultAsync(item => item.UserId == userId && item.KnowledgeBaseId == knowledgeBaseId && item.Id == documentId, cancellationToken);
    }

    public async Task<List<AiKnowledgeReference>> SearchAsync(
        int userId,
        long knowledgeBaseId,
        string query,
        int limit = 6,
        CancellationToken cancellationToken = default)
    {
        var tokens = ExtractTokens(query).Take(12).ToList();
        if (tokens.Count == 0)
        {
            return new List<AiKnowledgeReference>();
        }

        var chunks = await _dbContext.KnowledgeChunks
            .Where(item => item.UserId == userId && item.KnowledgeBaseId == knowledgeBaseId)
            .OrderByDescending(item => item.CreatedAt)
            .Take(1000)
            .ToListAsync(cancellationToken);

        var documentIds = chunks.Select(item => item.DocumentId).Distinct().ToList();
        var docs = await _dbContext.KnowledgeDocuments
            .Where(item => documentIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);

        return chunks
            .Select(chunk => new
            {
                Chunk = chunk,
                Score = ScoreChunk(chunk, tokens)
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Chunk.ChunkIndex)
            .Take(limit)
            .Select(item =>
            {
                docs.TryGetValue(item.Chunk.DocumentId, out var doc);
                return new AiKnowledgeReference
                {
                    DocumentId = item.Chunk.DocumentId,
                    ChunkId = item.Chunk.Id,
                    Title = doc?.Title ?? item.Chunk.Heading,
                    SourceUrl = doc?.SourceUrl ?? string.Empty,
                    Snippet = TrimSnippet(item.Chunk.Content)
                };
            })
            .ToList();
    }

    private static List<KnowledgeChunk> BuildChunks(int userId, long knowledgeBaseId, long documentId, string markdown)
    {
        var chunks = new List<KnowledgeChunk>();
        var currentHeading = string.Empty;
        var buffer = new StringBuilder();
        var index = 0;

        foreach (var rawLine in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line.StartsWith('#'))
            {
                Flush();
                currentHeading = line.TrimStart('#').Trim();
            }

            buffer.AppendLine(line);
            if (buffer.Length >= 1800)
            {
                Flush();
            }
        }

        Flush();
        return chunks;

        void Flush()
        {
            var content = buffer.ToString().Trim();
            if (string.IsNullOrWhiteSpace(content))
            {
                buffer.Clear();
                return;
            }

            chunks.Add(new KnowledgeChunk
            {
                UserId = userId,
                KnowledgeBaseId = knowledgeBaseId,
                DocumentId = documentId,
                ChunkIndex = index++,
                Heading = currentHeading,
                Content = content,
                Keywords = string.Join(',', ExtractTokens($"{currentHeading} {content}").Take(40)),
                CreatedAt = DateTime.UtcNow
            });
            buffer.Clear();
        }
    }

    private static int ScoreChunk(KnowledgeChunk chunk, IReadOnlyList<string> tokens)
    {
        var haystack = $"{chunk.Heading}\n{chunk.Keywords}\n{chunk.Content}".ToLowerInvariant();
        var score = 0;
        foreach (var token in tokens)
        {
            var normalized = token.ToLowerInvariant();
            if (haystack.Contains(normalized, StringComparison.Ordinal))
            {
                score += normalized.Length >= 4 ? 3 : 1;
            }
        }

        return score;
    }

    private static IEnumerable<string> ExtractTokens(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Enumerable.Empty<string>();
        }

        return Regex.Matches(input, @"[\u4e00-\u9fff]{2,}|[A-Za-z0-9_.-]{2,}")
            .Select(match => match.Value.Trim().ToLowerInvariant())
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string ExtractTitle(string markdown)
    {
        var firstHeading = markdown.Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("# ", StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(firstHeading))
        {
            return firstHeading.TrimStart('#').Trim();
        }

        return "Markdown 文档";
    }

    private static string TrimSnippet(string value)
    {
        var clean = Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
        return clean.Length <= 800 ? clean : $"{clean[..800]}...";
    }

    private static string ComputeHash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
