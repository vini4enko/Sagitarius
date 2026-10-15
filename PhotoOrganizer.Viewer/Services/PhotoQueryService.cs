using PhotoOrganizer.Data;
using PhotoOrganizer.Viewer.Services;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoOrganizer.Viewer.Services;

public class PhotoQueryService
{
    private readonly UserRepository _repo = new();
    private readonly SessionService _session;

    public PhotoQueryService(SessionService session)
    {
        _session = session;
    }

    public Task<PhotoPageResult> GetPageAsync(
        PhotoFilter filter, int offset, int limit,
        CancellationToken ct = default)
    {
        return _repo.GetPhotosPageAsync(
            _session.UserId, _session.RoleId, filter, offset, limit, ct);
    }

    public Task<(long UserId, long? RoleId)?> AuthenticateAsync(
        string login, string password)
        => _repo.AuthenticateAsync(login, password);

    public async Task<List<PhotoInfo>> GetAllAsync(PhotoFilter filter)
    {
        return await _repo.GetVisiblePhotosAsync((long)_session.UserId, (long)_session.RoleId);
    }
}