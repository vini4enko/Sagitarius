namespace PhotoOrganizer.Uploader;

public class UploaderOptions
{
    public string AdminLogin { get; set; } = "admin";
    public string AdminPassword { get; set; } = "";
    public string PhotosDirectory { get; set; } = "photos";
    public string PhotosManifest { get; set; } = "photos.json";
    public ThumbnailOptions Thumbnail { get; set; } = new();
}

public class PhotoAccessDefaults
{
    public bool Public { get; set; } = true;
    public List<string> Users { get; set; } = new();
    public List<string> Roles { get; set; } = new();
}

public class PhotoAccess
{
    public bool Public { get; set; } = true;
    public List<string> Users { get; set; } = new();
    public List<string> Roles { get; set; } = new();
}

public class PhotoDefaults
{
    public string? AuthorName { get; set; }
    public PhotoAccessDefaults? Access { get; set; }
}

public class PhotoEntry
{
    public string File { get; set; } = "";
    public string ObjectKey { get; set; } = "";
    public string? Title { get; set; }
    public List<string>? Albums { get; set; }
    public string? AuthorName { get; set; }
    public string? Address { get; set; }
    public double? Lon { get; set; }
    public double? Lat { get; set; }
    public PhotoAccess? Access { get; set; }
}

public class PhotosManifest
{
    public PhotoDefaults Defaults { get; set; } = new();
    public List<PhotoEntry> Photos { get; set; } = new();
}