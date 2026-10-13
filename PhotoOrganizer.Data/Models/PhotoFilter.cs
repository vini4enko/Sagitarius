namespace PhotoOrganizer.Data;

public class PhotoFilter
{
    public DateTime? UploadedFrom { get; set; }
    public DateTime? UploadedTo { get; set; }
    public DateTime? TakenFrom { get; set; }
    public DateTime? TakenTo { get; set; }
    public string? AuthorName { get; set; }
    public string? Album { get; set; }

    public bool IsEmpty =>
        UploadedFrom is null && UploadedTo is null
        && TakenFrom is null && TakenTo is null
        && string.IsNullOrWhiteSpace(AuthorName)
        && string.IsNullOrWhiteSpace(Album);
}

public class PhotoPageResult
{
    public List<PhotoListItem> Photos { get; init; } = new();
    public int TotalCount { get; init; }
    public int Offset { get; init; }
    public int Limit { get; init; }
}

public class PhotoListItem
{
    public Guid Id { get; init; }
    public string? Title { get; init; }
    public string Bucket { get; init; } = "";
    public string ObjectKey { get; init; } = "";
    public bool IsPublic { get; init; }
    public DateTime UploadedAt { get; init; }
    public DateTime? TakenAt { get; init; }
    public string? AuthorName { get; init; }
    public string? Address { get; init; }
    public double? Lat { get; init; }
    public double? Lon { get; init; }
    public string? ExtraJson { get; init; }
    public byte[]? ThumbnailData { get; init; }
    public string? ThumbnailMime { get; init; }

    public IReadOnlyList<string> Albums
    {
        get
        {
            if (string.IsNullOrEmpty(ExtraJson)) return Array.Empty<string>();
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(ExtraJson);
                if (doc.RootElement.TryGetProperty("albums", out var arr)
                    && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var list = new List<string>();
                    foreach (var item in arr.EnumerateArray())
                        list.Add(item.GetString() ?? "");
                    return list;
                }
            }
            catch { }
            return Array.Empty<string>();
        }
    }
}