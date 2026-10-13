using System.IO;

namespace PhotoOrganizer.Storage;

public interface IPhotoContentProvider
{
    /// <summary>Миниатюра фотографии (для превью в списке).</summary>
    Task<Stream?> GetThumbnailAsync(
        Guid photoId, long userId, long roleId, CancellationToken ct = default);

    /// <summary>Полное изображение фотографии (для ZoomableImage).</summary>
    Task<Stream?> GetFullImageAsync(
        Guid photoId, long userId, long roleId, CancellationToken ct = default);
}