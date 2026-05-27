namespace DocRag.Core.Documents;

public sealed record IngestionJobRecord(
    Guid Id,
    Guid DocumentId,
    IngestionJobStatus Status,
    int AttemptCount,
    int MaxAttempts,
    string? LockedBy,
    DateTimeOffset? LockedAt,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt);
