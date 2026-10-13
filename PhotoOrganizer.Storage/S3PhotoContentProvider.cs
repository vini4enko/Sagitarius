using PhotoOrganizer.Data;
using PhotoOrganizer.Data.Repositories;

namespace PhotoOrganizer.Storage;

public class S3PhotoContentProvider : IPhotoContentProvider
{
    private readonly S3StorageService _s3;
    private readonly S3Options _options;
    private readonly AdminRepository _adminRepo;
    private readonly UserRepository _userRepo;

    public S3PhotoContentProvider(
        S3StorageService s3,
        S3Options options,
        AdminRepository adminRepo,
        UserRepository userRepo)
    {
        _s3 = s3;
        _options = options;
        _adminRepo = adminRepo;
        _userRepo = userRepo;
    }

    public async Task<Stream?> GetThumbnailAsync(
        Guid photoId, long userId, long roleId, CancellationToken ct = default)
    {
        var thumb = await _userRepo.GetThumbnailAsync(photoId, userId, roleId);
        if (thumb is null) return null;
        return new MemoryStream(thumb.Data);
    }

    public async Task<Stream?> GetFullImageAsync(
    Guid photoId, long userId, long roleId, CancellationToken ct = default)
    {
        var s3Info = await _userRepo.GetVisiblePhotoS3InfoAsync(photoId, userId, roleId);
        if (s3Info is null) return null;

        return await _s3.DownloadAsync(s3Info.Value.ObjectKey, ct);
    }
}