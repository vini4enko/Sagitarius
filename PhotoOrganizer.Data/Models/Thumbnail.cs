namespace PhotoOrganizer.Data;

/// <summary>Миниатюра фотографии.</summary>
public class Thumbnail
{
    public string Mime { get; init; } = string.Empty;
    public byte[] Data { get; init; } = Array.Empty<byte>();
    public int Width { get; init; }
    public int Height { get; init; }
    public string Sha256 { get; init; } = string.Empty;
}
