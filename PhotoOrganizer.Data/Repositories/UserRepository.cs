using Npgsql;

namespace PhotoOrganizer.Data;

public class UserRepository : IDisposable
{
    private readonly NpgsqlConnection _connection;

    public UserRepository()
    {
        _connection = new NpgsqlConnection(DatabaseConfig.UserConnectionString);
        _connection.Open();
    }

    // ========================================================================
    // Аутентификация / пароль
    // ========================================================================

    public async Task ChangePasswordAsync(
        string login,
        string oldPassword,
        string newPassword)
    {
        if (string.IsNullOrWhiteSpace(login))
            throw new ArgumentException("login не может быть пустым.", nameof(login));
        if (string.IsNullOrEmpty(oldPassword))
            throw new ArgumentException("oldPassword не может быть пустым.", nameof(oldPassword));
        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 8)
            throw new ArgumentException(
                "newPassword должен содержать минимум 8 символов.", nameof(newPassword));

        await using var tx = await _connection.BeginTransactionAsync();

        const string sql = "CALL change_user_password(@login, @old, @new);";
        await using var cmd = new NpgsqlCommand(sql, _connection, tx);
        cmd.Parameters.AddWithValue("login", login);
        cmd.Parameters.AddWithValue("old", oldPassword);
        cmd.Parameters.AddWithValue("new", newPassword);

        try
        {
            await cmd.ExecuteNonQueryAsync();
            await tx.CommitAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == "28P01")
        {
            throw new UnauthorizedAccessException("Неверный старый пароль.", ex);
        }
        catch (PostgresException ex) when (ex.SqlState == "P0002")
        {
            throw new InvalidOperationException(
                $"Пользователь '{login}' не найден.", ex);
        }
        catch (PostgresException ex) when (ex.SqlState == "22023")
        {
            throw new ArgumentException(ex.MessageText, ex);
        }
    }

    // ========================================================================
    // Чтение фотографий
    // ========================================================================

    public async Task<List<PhotoInfo>> GetVisiblePhotosAsync(long userId, long roleId)
    {
        await using var tx = await _connection.BeginTransactionAsync();
        await DatabaseConfig.SetSessionContextAsync(_connection, tx, userId, roleId);

        const string sql = @"
            SELECT p.id, p.title, p.bucket, p.object_key,
                   p.is_public, p.uploaded_at,
                   a.author_name, a.address
            FROM   photos p
            LEFT JOIN attributes a ON a.photo_id = p.id
            ORDER BY p.uploaded_at DESC;";

        var photos = new List<PhotoInfo>();

        await using (var cmd = new NpgsqlCommand(sql, _connection, tx))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                photos.Add(new PhotoInfo
                {
                    Id = reader.GetGuid(0),
                    Title = reader.IsDBNull(1) ? null : reader.GetString(1),
                    Bucket = reader.GetString(2),
                    ObjectKey = reader.GetString(3),
                    IsPublic = reader.GetBoolean(4),
                    UploadedAt = reader.GetDateTime(5),
                    AuthorName = reader.IsDBNull(6) ? null : reader.GetString(6),
                    Address = reader.IsDBNull(7) ? null : reader.GetString(7)
                });
            }
        }   // ← reader и cmd закрыты здесь

        await tx.CommitAsync();
        return photos;
    }

    public async Task<Thumbnail?> GetThumbnailAsync(
        Guid photoId,
        long userId,
        long roleId)
    {
        await using var tx = await _connection.BeginTransactionAsync();
        await DatabaseConfig.SetSessionContextAsync(_connection, tx, userId, roleId);

        const string sql =
            "SELECT mime, data, width, height, sha256 " +
            "FROM   get_photo_thumbnail(@id);";

        Thumbnail? result = null;

        await using (var cmd = new NpgsqlCommand(sql, _connection, tx))
        {
            cmd.Parameters.AddWithValue("id", photoId);

            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    var mime = reader.IsDBNull(0) ? null : reader.GetString(0);
                    var data = reader.IsDBNull(1) ? null : reader.GetFieldValue<byte[]>(1);
                    var width = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2);
                    var height = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3);
                    var sha256 = reader.IsDBNull(4) ? null : reader.GetString(4);

                    if (mime != null && data != null)
                    {
                        result = new Thumbnail
                        {
                            Mime = mime,
                            Data = data,
                            Width = width ?? 0,
                            Height = height ?? 0,
                            Sha256 = sha256 ?? string.Empty
                        };
                    }
                }
            }
        }   // ← reader и cmd закрыты здесь

        await tx.CommitAsync();
        return result;
    }

    public async Task<List<LinkInfo>> GetPhotoLinksAsync(
        Guid photoId,
        long userId,
        long roleId)
    {
        await using var tx = await _connection.BeginTransactionAsync();
        await DatabaseConfig.SetSessionContextAsync(_connection, tx, userId, roleId);

        const string sql =
            "SELECT id_f, id_b, comment FROM get_photo_links(@id);";

        var links = new List<LinkInfo>();

        await using (var cmd = new NpgsqlCommand(sql, _connection, tx))
        {
            cmd.Parameters.AddWithValue("id", photoId);

            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    links.Add(new LinkInfo
                    {
                        IdF = reader.GetGuid(0),
                        IdB = reader.GetGuid(1),
                        Comment = reader.IsDBNull(2) ? null : reader.GetString(2)
                    });
                }
            }
        }

        await tx.CommitAsync();
        return links;
    }

    /// <summary>
    /// Проверяет логин и пароль. Возвращает (userId, roleId) или null.
    /// </summary>
    public async Task<(long UserId, long? RoleId)?> AuthenticateAsync(
        string login, string password)
    {
        await using var tx = await _connection.BeginTransactionAsync();
        await DatabaseConfig.SetAnonymousContextAsync(_connection, tx);

        const string sql = "SELECT user_id, role_id FROM authenticate_user(@l, @p);";
        await using var cmd = new NpgsqlCommand(sql, _connection, tx);
        cmd.Parameters.AddWithValue("l", login);
        cmd.Parameters.AddWithValue("p", password);

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                await reader.CloseAsync();
                await tx.CommitAsync();
                return null;
            }

            long userId = reader.GetInt64(0);
            long? roleId = reader.IsDBNull(1) ? null : reader.GetInt64(1);
            await reader.CloseAsync();
            await tx.CommitAsync();
            return (userId, roleId);
        }
        catch (PostgresException ex) when (ex.SqlState == "28P01")
        {
            throw new UnauthorizedAccessException("Неверный пароль.", ex);
        }
        catch (PostgresException ex) when (ex.SqlState == "P0002")
        {
            throw new InvalidOperationException("Пользователь не найден.", ex);
        }
    }

    /// <summary>
    /// Возвращает страницу фотографий с фильтром.
    /// userId == null и roleId == null → анонимный режим (только is_public).
    /// </summary>
    public async Task<PhotoPageResult> GetPhotosPageAsync(
        long? userId,
        long? roleId,
        PhotoFilter filter,
        int offset,
        int limit,
        CancellationToken ct = default)
    {
        await using var tx = await _connection.BeginTransactionAsync(ct);

        if (userId.HasValue && roleId.HasValue)
            await DatabaseConfig.SetSessionContextAsync(
                _connection, tx, userId.Value, roleId.Value);
        else
            await DatabaseConfig.SetAnonymousContextAsync(_connection, tx);

        const string sql = @"
        SELECT p.id, p.title, p.bucket, p.object_key,
               p.is_public, p.uploaded_at, p.taken_at,
               a.author_name, a.address,
               ST_Y(a.geo) AS lat,
               ST_X(a.geo) AS lon,
               a.extra::text AS extra_json,
               a.thumbnail_data, a.thumbnail_mime,
               count(*) OVER () AS total_count
        FROM   photos p
        LEFT JOIN attributes a ON a.photo_id = p.id
        WHERE  (@uploadedFrom::timestamptz IS NULL OR p.uploaded_at >= @uploadedFrom::timestamptz)
          AND  (@uploadedTo::timestamptz   IS NULL OR p.uploaded_at <  @uploadedTo::timestamptz)
          AND  (@takenFrom::timestamptz    IS NULL OR p.taken_at    >= @takenFrom::timestamptz)
          AND  (@takenTo::timestamptz      IS NULL OR p.taken_at    <  @takenTo::timestamptz)
          AND  (@authorName::text          IS NULL OR a.author_name =  @authorName::text)
          AND  (@album::text IS NULL
                OR a.extra @> jsonb_build_object('albums', jsonb_build_array(@album::text)))
        ORDER BY p.uploaded_at DESC
        OFFSET @offset LIMIT @limit;";

        var photos = new List<PhotoListItem>();
        int totalCount = 0;

        await using (var cmd = new NpgsqlCommand(sql, _connection, tx))
        {
            cmd.Parameters.AddWithValue("uploadedFrom", (object?)filter.UploadedFrom ?? DBNull.Value);
            cmd.Parameters.AddWithValue("uploadedTo", (object?)filter.UploadedTo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("takenFrom", (object?)filter.TakenFrom ?? DBNull.Value);
            cmd.Parameters.AddWithValue("takenTo", (object?)filter.TakenTo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("authorName", (object?)filter.AuthorName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("album", (object?)filter.Album ?? DBNull.Value);
            cmd.Parameters.AddWithValue("offset", offset);
            cmd.Parameters.AddWithValue("limit", limit);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var item = new PhotoListItem
                {
                    Id = reader.GetGuid(0),
                    Title = reader.IsDBNull(1) ? null : reader.GetString(1),
                    Bucket = reader.GetString(2),
                    ObjectKey = reader.GetString(3),
                    IsPublic = reader.GetBoolean(4),
                    UploadedAt = reader.GetDateTime(5),
                    TakenAt = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                    AuthorName = reader.IsDBNull(7) ? null : reader.GetString(7),
                    Address = reader.IsDBNull(8) ? null : reader.GetString(8),
                    Lat = reader.IsDBNull(9) ? null : reader.GetDouble(9),
                    Lon = reader.IsDBNull(10) ? null : reader.GetDouble(10),
                    ExtraJson = reader.IsDBNull(11) ? null : reader.GetString(11),
                    ThumbnailData = reader.IsDBNull(12) ? null : reader.GetFieldValue<byte[]>(12),
                    ThumbnailMime = reader.IsDBNull(13) ? null : reader.GetString(13)
                };
                photos.Add(item);
                if (reader.FieldCount > 14 && !reader.IsDBNull(14))
                    totalCount = (int)reader.GetInt64(14);
            }
        }

        await tx.CommitAsync(ct);

        return new PhotoPageResult
        {
            Photos = photos,
            TotalCount = totalCount,
            Offset = offset,
            Limit = limit
        };
    }

    /// <summary>
    /// Проверяет, видна ли фотография пользователю, без выгрузки всего списка.
    /// Возвращает object_key и bucket, если фотография доступна.
    /// </summary>
    public async Task<(string Bucket, string ObjectKey)?> GetVisiblePhotoS3InfoAsync(
        Guid photoId, long userId, long roleId)
    {
        await using var tx = await _connection.BeginTransactionAsync();
        await DatabaseConfig.SetSessionContextAsync(_connection, tx, userId, roleId);

        const string sql = @"
        SELECT bucket, object_key
        FROM   photos
        WHERE  id = @id;";

        (string, string)? result = null;
        await using (var cmd = new NpgsqlCommand(sql, _connection, tx))
        {
            cmd.Parameters.AddWithValue("id", photoId);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                result = (reader.GetString(0), reader.GetString(1));
        }

        await tx.CommitAsync();
        return result;
    }

    // ========================================================================

    public void Dispose()
    {
        _connection?.Dispose();
    }
}