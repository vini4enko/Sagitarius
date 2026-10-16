namespace PhotoOrganizer.Data;

public class ShowListItem
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public long AuthorId { get; init; }
    public string? AuthorLogin { get; init; }
    public DateTime CreatedAt { get; init; }
    public int PhotoCount { get; init; }
    public int IntervalSeconds { get; init; }
}

public class ShowDetail
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public long AuthorId { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public int IntervalSeconds { get; init; }
    public bool Loop { get; init; }
    public bool RandomOrder { get; init; }
    public string Transition { get; init; } = "cut";
    public bool ShowCaptions { get; init; }
    public int PhotoCount { get; init; }
}

public class ShowPhotoItem
{
    public Guid PhotoId { get; init; }
    public string? Title { get; init; }
    public string? AuthorName { get; init; }
    public string ObjectKey { get; init; } = "";
    public int Position { get; init; }
}