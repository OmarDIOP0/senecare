namespace SeneCare.Domain.Common;

/// <summary>A stored file (photo, voice note, signature, PDF). Bytes live in file storage, not the database.</summary>
public sealed class MediaFile : Entity
{
    private MediaFile()
    {
        StorageKey = string.Empty;
        ContentType = string.Empty;
    }

    /// <param name="accountId">Customer account the file belongs to (access control); null for staff-only files.</param>
    public MediaFile(string storageKey, string contentType, long sizeBytes, MediaKind kind, Guid uploadedByUserId, Guid? accountId, DateTimeOffset uploadedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        if (sizeBytes <= 0)
        {
            throw new DomainException("media.empty", "A file cannot be empty.");
        }

        StorageKey = storageKey;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Kind = kind;
        UploadedByUserId = uploadedByUserId;
        AccountId = accountId;
        UploadedAt = uploadedAt;
    }

    public string StorageKey { get; private set; }

    public string ContentType { get; private set; }

    public long SizeBytes { get; private set; }

    public MediaKind Kind { get; private set; }

    public Guid UploadedByUserId { get; private set; }

    public Guid? AccountId { get; private set; }

    public DateTimeOffset UploadedAt { get; private set; }
}
