namespace PhotoOrganizer.Data;

/// <summary>Связь фотографии с другой фотографией.</summary>
public class LinkInfo
{
    public Guid IdF { get; init; }
    public Guid IdB { get; init; }
    public string? Comment { get; init; }
}