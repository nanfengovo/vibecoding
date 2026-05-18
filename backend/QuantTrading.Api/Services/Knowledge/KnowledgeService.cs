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

    public async Task<KnowledgeBase> EnsureDefaultKnowledgeBaseAsync(int userId, CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.KnowledgeBases
            .Where(item => item.UserId == userId)
            .OrderByDescending(item => item.IsDefault)
            .ThenByDescending(item => item.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var kb = new KnowledgeBase
        {
            UserId = userId,
            Name = "默认知识库",
            Description = "系统自动创建",
            IsDefault = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _dbContext.KnowledgeBases.Add(kb);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return kb;
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

        await SyncKnowledgeDocumentToMemoryAsync(userId, doc, cancellationToken);
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

    public async Task<AiMemoryRecord> SyncMemoryToKnowledgeAsync(
        int userId,
        AiMemoryRecord memory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(memory);

        if (memory.UserId != userId)
        {
            throw new InvalidOperationException("无权同步该记忆。");
        }

        var now = DateTime.UtcNow;
        var markdown = BuildMemoryMarkdown(memory);

        if (string.Equals(memory.SourceType, "knowledge_document", StringComparison.OrdinalIgnoreCase)
            && memory.KnowledgeDocumentId.HasValue
            && memory.KnowledgeDocumentId.Value > 0)
        {
            var doc = await _dbContext.KnowledgeDocuments
                .FirstOrDefaultAsync(item =>
                    item.UserId == userId
                    && item.Id == memory.KnowledgeDocumentId.Value,
                    cancellationToken);
            if (doc != null)
            {
                doc.Title = ResolveMemoryDocumentTitle(memory);
                doc.Markdown = markdown;
                doc.ContentHash = ComputeHash(markdown);
                doc.UpdatedAt = now;
                _dbContext.KnowledgeChunks.RemoveRange(_dbContext.KnowledgeChunks.Where(item => item.DocumentId == doc.Id));

                var chunksForDoc = BuildChunks(userId, doc.KnowledgeBaseId, doc.Id, doc.Markdown);
                _dbContext.KnowledgeChunks.AddRange(chunksForDoc);

                memory.KnowledgeBaseId = doc.KnowledgeBaseId;
                memory.SyncStatus = "synced";
                memory.LastSyncedAt = now;
                memory.UpdatedAt = now;

                await _dbContext.SaveChangesAsync(cancellationToken);
                return memory;
            }
        }

        var targetKb = await ResolveTargetKnowledgeBaseAsync(userId, memory.KnowledgeBaseId, cancellationToken);
        var sourceUrl = $"memory://{memory.Id}";

        var document = await _dbContext.KnowledgeDocuments
            .FirstOrDefaultAsync(item =>
                item.UserId == userId
                && item.SourceType == "ai_memory"
                && item.SourceUrl == sourceUrl,
                cancellationToken);

        if (document == null)
        {
            document = new KnowledgeDocument
            {
                UserId = userId,
                KnowledgeBaseId = targetKb.Id,
                Title = ResolveMemoryDocumentTitle(memory),
                SourceType = "ai_memory",
                SourceUrl = sourceUrl,
                Markdown = markdown,
                ContentHash = ComputeHash(markdown),
                CreatedAt = now,
                UpdatedAt = now
            };
            _dbContext.KnowledgeDocuments.Add(document);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        else
        {
            document.KnowledgeBaseId = targetKb.Id;
            document.Title = ResolveMemoryDocumentTitle(memory);
            document.Markdown = markdown;
            document.ContentHash = ComputeHash(markdown);
            document.UpdatedAt = now;
            _dbContext.KnowledgeChunks.RemoveRange(_dbContext.KnowledgeChunks.Where(item => item.DocumentId == document.Id));
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var chunks = BuildChunks(userId, document.KnowledgeBaseId, document.Id, document.Markdown);
        _dbContext.KnowledgeChunks.AddRange(chunks);

        targetKb.UpdatedAt = now;
        memory.KnowledgeBaseId = document.KnowledgeBaseId;
        memory.KnowledgeDocumentId = document.Id;
        memory.SyncStatus = "synced";
        memory.LastSyncedAt = now;
        memory.UpdatedAt = now;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return memory;
    }

    public async Task<AiMemoryRecord> SyncKnowledgeDocumentToMemoryAsync(
        int userId,
        KnowledgeDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.UserId != userId)
        {
            throw new InvalidOperationException("无权同步该知识文档。");
        }

        var now = DateTime.UtcNow;
        var sourceRef = $"knowledge:{document.KnowledgeBaseId}:{document.Id}";
        var sourceUrl = string.IsNullOrWhiteSpace(document.SourceUrl)
            ? $"knowledge://base/{document.KnowledgeBaseId}/doc/{document.Id}"
            : document.SourceUrl.Trim();

        var memory = await _dbContext.AiMemories
            .FirstOrDefaultAsync(item =>
                item.UserId == userId
                && item.SourceType == "knowledge_document"
                && item.SourceRef == sourceRef,
                cancellationToken);

        var content = BuildKnowledgeMemoryContent(document.Markdown);
        if (memory == null)
        {
            memory = new AiMemoryRecord
            {
                UserId = userId,
                Type = "knowledge_document",
                Title = $"知识文档：{document.Title}",
                Content = content,
                Symbol = string.Empty,
                Tags = "knowledge,auto",
                Priority = 2,
                SourceType = "knowledge_document",
                SourceUrl = sourceUrl,
                SourceRef = sourceRef,
                KnowledgeBaseId = document.KnowledgeBaseId,
                KnowledgeDocumentId = document.Id,
                ProviderId = string.Empty,
                Model = string.Empty,
                SyncStatus = "synced",
                LastSyncedAt = now,
                IsArchived = false,
                CreatedAt = now,
                UpdatedAt = now
            };
            _dbContext.AiMemories.Add(memory);
        }
        else
        {
            memory.Type = string.IsNullOrWhiteSpace(memory.Type) ? "knowledge_document" : memory.Type;
            memory.Title = $"知识文档：{document.Title}";
            memory.Content = content;
            memory.Tags = MergeTags(memory.Tags, "knowledge,auto");
            memory.SourceUrl = sourceUrl;
            memory.KnowledgeBaseId = document.KnowledgeBaseId;
            memory.KnowledgeDocumentId = document.Id;
            memory.SyncStatus = "synced";
            memory.LastSyncedAt = now;
            memory.IsArchived = false;
            memory.UpdatedAt = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return memory;
    }

    private async Task<KnowledgeBase> ResolveTargetKnowledgeBaseAsync(
        int userId,
        long? knowledgeBaseId,
        CancellationToken cancellationToken)
    {
        if (knowledgeBaseId.HasValue && knowledgeBaseId.Value > 0)
        {
            var target = await _dbContext.KnowledgeBases
                .FirstOrDefaultAsync(item => item.UserId == userId && item.Id == knowledgeBaseId.Value, cancellationToken);
            if (target != null)
            {
                return target;
            }
        }

        return await EnsureDefaultKnowledgeBaseAsync(userId, cancellationToken);
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

    private static string ResolveMemoryDocumentTitle(AiMemoryRecord memory)
    {
        var title = (memory.Title ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(title))
        {
            return title.Length <= 280 ? title : title[..280];
        }

        var type = string.IsNullOrWhiteSpace(memory.Type) ? "memory" : memory.Type.Trim();
        return $"AI 记忆 {type} {DateTime.UtcNow:yyyy-MM-dd HH:mm}";
    }

    private static string BuildMemoryMarkdown(AiMemoryRecord memory)
    {
        var title = ResolveMemoryDocumentTitle(memory);
        var sb = new StringBuilder();
        sb.AppendLine($"# {title}");
        sb.AppendLine();
        sb.AppendLine(memory.Content?.Trim() ?? string.Empty);
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine($"- type: {memory.Type}");
        sb.AppendLine($"- priority: {memory.Priority}");
        sb.AppendLine($"- tags: {memory.Tags}");
        sb.AppendLine($"- sourceType: {memory.SourceType}");
        sb.AppendLine($"- sourceRef: {memory.SourceRef}");
        sb.AppendLine($"- providerId: {memory.ProviderId}");
        sb.AppendLine($"- model: {memory.Model}");
        sb.AppendLine($"- syncedAt: {DateTime.UtcNow:O}");
        return sb.ToString().Trim();
    }

    private static string BuildKnowledgeMemoryContent(string markdown)
    {
        var clean = Regex.Replace(markdown ?? string.Empty, @"\s+", " ").Trim();
        if (clean.Length <= 2000)
        {
            return clean;
        }

        return $"{clean[..2000]}...";
    }

    private static string MergeTags(string existing, string append)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in (existing ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(part))
            {
                set.Add(part);
            }
        }

        foreach (var part in (append ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(part))
            {
                set.Add(part);
            }
        }

        return string.Join(',', set);
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
