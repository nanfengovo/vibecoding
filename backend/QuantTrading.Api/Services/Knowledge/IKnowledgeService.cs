using QuantTrading.Api.Models;
using QuantTrading.Api.Services.AI;

namespace QuantTrading.Api.Services.Knowledge;

public interface IKnowledgeService
{
    Task<List<KnowledgeBase>> ListKnowledgeBasesAsync(int userId, CancellationToken cancellationToken = default);
    Task<KnowledgeBase> CreateKnowledgeBaseAsync(int userId, string name, string description, CancellationToken cancellationToken = default);
    Task<KnowledgeBase?> UpdateKnowledgeBaseAsync(int userId, long id, string name, string description, CancellationToken cancellationToken = default);
    Task<List<KnowledgeDocument>> ListDocumentsAsync(int userId, long knowledgeBaseId, CancellationToken cancellationToken = default);
    Task<KnowledgeDocument> ImportMarkdownAsync(int userId, long knowledgeBaseId, string title, string markdown, string sourceUrl, string sourceType, CancellationToken cancellationToken = default);
    Task<KnowledgeDocument?> GetDocumentAsync(int userId, long knowledgeBaseId, long documentId, CancellationToken cancellationToken = default);
    Task<List<AiKnowledgeReference>> SearchAsync(int userId, long knowledgeBaseId, string query, int limit = 6, CancellationToken cancellationToken = default);
}
