namespace PhotoOrganizer.Data;

/// <summary>Краткая информация о фотографии, видимой пользователю.</summary>
public class PhotoInfo
{
    public Guid Id { get; init; }
    public string? Title { get; init; }
    public string Bucket { get; init; } = string.Empty;
    public string ObjectKey { get; init; } = string.Empty;
    public string? AuthorName { get; init; }
    public string? Address { get; init; }
    public bool IsPublic { get; init; }
    public DateTime UploadedAt { get; init; }
}
