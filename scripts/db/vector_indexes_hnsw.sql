-- IMPORTANT:
-- 1) Run in off-peak window on Railway.
-- 2) Do NOT wrap these statements in an explicit transaction.
-- 3) Run one index at a time for small instances.

CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_KnowledgeChunkEmbeddings_Embedding_HNSW"
ON "KnowledgeChunkEmbeddings"
USING hnsw ("Embedding" vector_cosine_ops)
WITH (m = 16, ef_construction = 64);
