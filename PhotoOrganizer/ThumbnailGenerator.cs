using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using System.Security.Cryptography;
using static System.Net.Mime.MediaTypeNames;

namespace PhotoOrganizer;

public class ThumbnailOptions
{
    public int MaxWidth { get; set; } = 320;
    public int MaxHeight { get; set; } = 240;
    public string MimeType { get; set; } = "image/webp";
    public int Quality { get; set; } = 80;
    public bool KeepOriginalIfSmaller { get; set; } = true;
}

public class ThumbnailResult
{
    public byte[] Data { get; init; } = Array.Empty<byte>();
    public string Mime { get; init; } = string.Empty;
    public int Width { get; init; }
    public int Height { get; init; }
    public int Bytes { get; init; }
    public string Sha256 { get; init; } = string.Empty;
}

public static class ThumbnailGenerator
{
    public static ThumbnailResult Generate(string sourcePath, ThumbnailOptions options)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"Исходный файл не найден: {sourcePath}", sourcePath);

        if (options.MaxWidth <= 0 || options.MaxHeight <= 0)
            throw new ArgumentException("MaxWidth и MaxHeight должны быть положительными.");
        if (options.Quality < 1 || options.Quality > 100)
            throw new ArgumentException("Quality должен быть в диапазоне 1..100.");

        using var image = SixLabors.ImageSharp.Image.Load(sourcePath);

        int sourceWidth = image.Width;
        int sourceHeight = image.Height;

        double ratio = Math.Min(
            (double)options.MaxWidth / sourceWidth,
            (double)options.MaxHeight / sourceHeight);

        if (options.KeepOriginalIfSmaller && ratio > 1.0)
            ratio = 1.0;

        int targetWidth = Math.Max(1, (int)Math.Round(sourceWidth * ratio));
        int targetHeight = Math.Max(1, (int)Math.Round(sourceHeight * ratio));

        if (targetWidth != sourceWidth || targetHeight != sourceHeight)
        {
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(targetWidth, targetHeight),
                Mode = ResizeMode.Max,
                Sampler = KnownResamplers.Lanczos3
            }));
        }

        using var ms = new MemoryStream();
        switch (options.MimeType)
        {
            case "image/jpeg":
                image.Save(ms, new JpegEncoder { Quality = options.Quality });
                break;
            case "image/png":
                image.Save(ms, new PngEncoder());
                break;
            case "image/webp":
                image.Save(ms, new WebpEncoder { Quality = options.Quality });
                break;
            default:
                throw new ArgumentException(
                    $"Неподдерживаемый MimeType: {options.MimeType}.");
        }

        byte[] data = ms.ToArray();
        string sha256 = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

        return new ThumbnailResult
        {
            Data = data,
            Mime = options.MimeType,
            Width = targetWidth,
            Height = targetHeight,
            Bytes = data.Length,
            Sha256 = sha256
        };
    }
}