using PhotoOrganizer.Data;
using PhotoOrganizer.Data.Repositories;

namespace PhotoOrganizer.Storage;

public class CdnPhotoContentProvider : IPhotoContentProvider
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;   // например, "https://cdn.example.com"
    private readonly AdminRepository _adminRepo;
    private readonly UserRepository _userRepo;

    public CdnPhotoContentProvider(
        HttpClient http,
        string baseUrl,
        AdminRepository adminRepo,
        UserRepository userRepo)
    {
        _http = http;
        _baseUrl = baseUrl.TrimEnd('/');
        _adminRepo = adminRepo;
        _userRepo = userRepo;
    }

    public Task<Stream?> GetThumbnailAsync(
        Guid photoId, long userId, long roleId, CancellationToken ct = default)
        => GetAsync(photoId, userId, roleId, ct);

    public Task<Stream?> GetFullImageAsync(
        Guid photoId, long userId, long roleId, CancellationToken ct = default)
        => GetAsync(photoId, userId, roleId, ct);

    private async Task<Stream?> GetAsync(
        Guid photoId, long userId, long roleId, CancellationToken ct)
    {
        var visible = await _userRepo.GetVisiblePhotosAsync(userId, roleId);
        var info = visible.FirstOrDefault(p => p.Id == photoId);
        if (info is null) return null;

        var url = $"{_baseUrl}/{info.Bucket}/{info.ObjectKey}";
        var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) return null;

        var ms = new MemoryStream();
        await response.Content.CopyToAsync(ms, ct);
        ms.Position = 0;
        return ms;
    }
}