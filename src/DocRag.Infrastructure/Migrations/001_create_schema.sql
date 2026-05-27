CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS schema_migrations (
    name text PRIMARY KEY,
    applied_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS documents (
    id uuid PRIMARY KEY,
    original_file_name text NOT NULL,
    stored_file_name text NOT NULL,
    extension text NOT NULL,
    content_type text NULL,
    source_type text NOT NULL CHECK (source_type IN ('upload', 'import')),
    source_path text NULL,
    size_bytes bigint NOT NULL CHECK (size_bytes >= 0),
    content_sha256 text NOT NULL,
    status text NOT NULL CHECK (status IN ('queued', 'processing', 'indexed', 'failed', 'deleted')),
    error_message text NULL,
    chunk_count integer NOT NULL DEFAULT 0 CHECK (chunk_count >= 0),
    embedding_provider text NULL,
    embedding_model text NULL,
    embedding_dimensions integer NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    indexed_at timestamptz NULL,
    deleted_at timestamptz NULL,
    CONSTRAINT ck_documents_embedding_dimensions_v1
        CHECK (embedding_dimensions IS NULL OR embedding_dimensions = 1536)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_documents_content_sha256_active
ON documents (content_sha256)
WHERE deleted_at IS NULL;

CREATE INDEX IF NOT EXISTS ix_documents_status
ON documents (status);

CREATE INDEX IF NOT EXISTS ix_documents_created_at
ON documents (created_at DESC);

CREATE TABLE IF NOT EXISTS ingestion_jobs (
    id uuid PRIMARY KEY,
    document_id uuid NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
    status text NOT NULL CHECK (status IN ('queued', 'processing', 'succeeded', 'failed')),
    attempt_count integer NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
    max_attempts integer NOT NULL DEFAULT 3 CHECK (max_attempts > 0),
    locked_by text NULL,
    locked_at timestamptz NULL,
    error_message text NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    completed_at timestamptz NULL
);

CREATE INDEX IF NOT EXISTS ix_ingestion_jobs_status_created_at
ON ingestion_jobs (status, created_at);

CREATE TABLE IF NOT EXISTS document_chunks (
    id uuid PRIMARY KEY,
    document_id uuid NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
    chunk_index integer NOT NULL CHECK (chunk_index >= 0),
    content text NOT NULL,
    content_sha256 text NOT NULL,
    token_count integer NOT NULL CHECK (token_count > 0),
    page_start integer NULL,
    page_end integer NULL,
    heading text NULL,
    embedding vector(1536) NOT NULL,
    embedding_provider text NOT NULL,
    embedding_model text NOT NULL,
    embedding_dimensions integer NOT NULL DEFAULT 1536,
    metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
    search_vector tsvector GENERATED ALWAYS AS (to_tsvector('simple', coalesce(content, ''))) STORED,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_document_chunks_embedding_model_v1
        CHECK (embedding_model = 'text-embedding-3-small'),
    CONSTRAINT ck_document_chunks_embedding_dimensions_v1
        CHECK (embedding_dimensions = 1536)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_document_chunks_document_chunk_index
ON document_chunks (document_id, chunk_index);

CREATE INDEX IF NOT EXISTS ix_document_chunks_document_id
ON document_chunks (document_id);

CREATE INDEX IF NOT EXISTS ix_document_chunks_content_sha256
ON document_chunks (content_sha256);

CREATE INDEX IF NOT EXISTS ix_document_chunks_search_vector
ON document_chunks USING gin (search_vector);

CREATE INDEX IF NOT EXISTS ix_document_chunks_embedding_hnsw
ON document_chunks
USING hnsw (embedding vector_cosine_ops)
WITH (m = 16, ef_construction = 64);
