namespace DocRag.Core.Documents;

public enum DocumentStatus
{
    Queued,
    Processing,
    Indexed,
    Failed,
    Deleted
}

public enum IngestionJobStatus
{
    Queued,
    Processing,
    Succeeded,
    Failed
}

public enum DocumentSourceType
{
    Upload,
    Import
}
