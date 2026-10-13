using Microsoft.Extensions.Options;
using PhotoOrganizer.Data;
using PhotoOrganizer.Data.Repositories;

namespace PhotoOrganizer.Storage;

public class PhotoService
{
    private readonly S3StorageService _s3;
    private readonly AdminRepository _adminRepo;
    private readonly string _bucketName;

    public PhotoService(
        S3StorageService s3,
        AdminRepository adminRepo,
        IOptions<S3Options> options)
    {
        _s3 = s3;
        _adminRepo = adminRepo;
        _bucketName = options.Value.BucketName;
    }

    /// <summary>
    /// Загружает фотографию: сначала в S3, затем записывает метаданные в БД.
    /// При ошибке на шаге БД выполняет компенсацию — удаляет объект из S3.
    /// Если компенсация не удалась, ставит задачу в cleanup_queue.
    /// </summary>
    public async Task<Guid> UploadPhotoAsync(
    long ownerAdminId,
    Stream fileStream,
    string objectKey,
    string contentType,
    string? title = null,
    byte[]? thumbnailData = null,
    string? thumbnailMime = null,
    int? thumbnailWidth = null,
    int? thumbnailHeight = null,
    double? lon = null,
    double? lat = null,
    string? address = null,
    string? authorName = null,
    string? extraJson = null,                 // <<< НОВЫЙ ПАРАМЕТР
    CancellationToken ct = default)
    {
        await _s3.UploadAsync(fileStream, objectKey, contentType, ct);

        Guid photoId;
        try
        {
            photoId = await _adminRepo.AddPhotoAsync(
                ownerAdminId, _bucketName, objectKey, title);

            bool hasAttributes =
                thumbnailData != null
                || !string.IsNullOrWhiteSpace(address)
                || !string.IsNullOrWhiteSpace(authorName)
                || lon.HasValue || lat.HasValue
                || !string.IsNullOrWhiteSpace(extraJson);

            if (hasAttributes)
            {
                await _adminRepo.UpsertAttributesAsync(
                    photoId,
                    thumbnailData,
                    thumbnailMime,
                    thumbnailWidth,
                    thumbnailHeight,
                    lon, lat, address, authorName,
                    extraJson);
            }
        }
        catch (Exception ex)
        {
            try
            {
                await _s3.DeleteAsync(objectKey, ct);
            }
            catch (Exception s3Ex)
            {
                await _adminRepo.EnqueueCleanupAsync(
                    "delete_s3_object",
                    $"{{\"bucket\":\"{_bucketName}\",\"key\":\"{objectKey}\"}}",
                    s3Ex.Message);
            }

            throw new InvalidOperationException(
                "Не удалось сохранить метаданные фотографии. Загрузка в S3 отменена.", ex);
        }

        return photoId;
    }

    /// <summary>
    /// Удаляет фотографию: сначала из БД, затем из S3.
    /// Если удаление из S3 не удалось, ставит задачу в cleanup_queue.
    /// </summary>

    public async Task DeletePhotoAsync(Guid photoId, CancellationToken ct = default)
    {
        // 1. Читаем S3-ключ ДО удаления записи
        var s3Info = await _adminRepo.GetS3InfoAsync(photoId);
        if (s3Info is null)
            return;   // уже удалено — ничего не делаем

        // 2. Удаляем из БД
        await _adminRepo.DeletePhotoAsync(photoId);

        // 3. Удаляем из S3
        try
        {
            await _s3.DeleteAsync(s3Info.Value.ObjectKey, ct);
        }
        catch (Exception s3Ex)
        {
            // Компенсация: ставим задачу в cleanup_queue
            await _adminRepo.EnqueueCleanupAsync(
                "delete_s3_object",
                $"{{\"bucket\":\"{s3Info.Value.Bucket}\",\"key\":\"{s3Info.Value.ObjectKey}\"}}",
                s3Ex.Message);

            // Не пробрасываем дальше: запись в БД уже удалена,
            // а задача на удаление файла сохранена.
        }
    }

    /// <summary>
    /// Скачивает фотографию из S3, проверяя права через БД.
    /// </summary>

    public async Task<Stream?> DownloadPhotoAsync(
    Guid photoId,
    long userId,
    long roleId,
    CancellationToken ct = default)
    {
        // 1. Проверяем, что фотография видна пользователю (RLS через UserRepository)
        using var userRepo = new UserRepository();
        var visible = await userRepo.GetVisiblePhotosAsync(userId, roleId);
        if (!visible.Any(p => p.Id == photoId))
            return null;   // нет доступа или фотографии нет

        // 2. Получаем S3-ключ (от имени администратора, у которого есть доступ к photos)
        using var adminRepo = new AdminRepository();
        var s3Info = await adminRepo.GetS3InfoAsync(photoId);
        if (s3Info is null)
            return null;

        // 3. Скачиваем из S3
        return await _s3.DownloadAsync(s3Info.Value.ObjectKey, ct);
    }

    private async Task EnqueueCleanupAsync(
        string bucket, string key, string errorMessage)
    {
        // Запись в таблицу cleanup_queue через AdminRepository
        // или напрямую через Npgsql
        await _adminRepo.EnqueueCleanupAsync(
            "delete_s3_object",
            $"{{\"bucket\":\"{bucket}\",\"key\":\"{key}\"}}",
            errorMessage);
    }
}