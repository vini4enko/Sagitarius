using PhotoOrganizer.Data;

namespace PhotoOrganizer.Viewer.Services;

public enum SessionMode { Anonymous, Registered }

public class SessionService
{
    public SessionMode Mode { get; private set; } = SessionMode.Anonymous;
    public long? UserId { get; private set; }
    public long? RoleId { get; private set; }
    public string? Login { get; private set; }

    public void SetAnonymous()
    {
        Mode = SessionMode.Anonymous;
        UserId = null;
        RoleId = null;
        Login = null;
    }

    public void SetRegistered(long userId, long? roleId, string login)
    {
        Mode = SessionMode.Registered;
        UserId = userId;
        RoleId = roleId;
        Login = login;
    }
}