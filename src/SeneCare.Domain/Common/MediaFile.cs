namespace SeneCare.Domain.Common;

/// <summary>A stored file (photo, voice note, signature, PDF). Bytes live in file storage, not the database.</summary>
public sealed class MediaFile : Entity
{
    private MediaFile()
    {
        StorageKey = string.Empty;
        ContentType = string.Empty;
    }

    public MediaFile(string storageKey, string contentType, long sizeBytes, MediaKind kind, Guid uploadedByUserId, DateTimeOffset uploadedAt)
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
        UploadedAt = uploadedAt;
    }

    public string StorageKey { get; private set; }

    public string ContentType { get; private set; }

    public long SizeBytes { get; private set; }

    public MediaKind Kind { get; private set; }

    public Guid UploadedByUserId { get; private set; }

    public DateTimeOffset UploadedAt { get; private set; }
}
