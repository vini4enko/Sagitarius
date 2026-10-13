using Npgsql;
using NpgsqlTypes;

namespace PhotoOrganizer.Data.Repositories;

/// <summary>
/// Операции, доступные администратору приложения (роль app_admin).
/// Все методы выполняются в транзакции и устанавливают контекст сессии.
/// </summary>
public class AdminRepository : IDisposable
{
    private readonly NpgsqlConnection _connection;

    public AdminRepository()
    {
        _connection = new NpgsqlConnection(DatabaseConfig.AdminConnectionString);
        _connection.Open();
    }

    // ========================================================================
    // Пользователи
    // ========================================================================

    /// <summary>
    /// Регистрирует нового пользователя приложения.
    /// Возвращает id созданной записи в app_users.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Логин уже занят или указана несуществующая роль.
    /// </exception>
    public async Task<long> RegisterUserAsync(
        string login,
        string password,
        long? roleId = null)
    {
        await using var tx = await _connection.BeginTransactionAsync();

        const string sql = "SELECT register_user(@login, @password, @roleId);";
        await using var cmd = new NpgsqlCommand(sql, _connection, tx);
        cmd.Parameters.AddWithValue("login", login);
        cmd.Parameters.AddWithValue("password", password);
        cmd.Parameters.AddWithValue("roleId", (object?)roleId ?? DBNull.Value);

        try
        {
            var result = await cmd.ExecuteScalarAsync();
            await tx.CommitAsync();
            return (long)result!;
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            throw new InvalidOperationException(
                $"Пользователь с логином '{login}' уже существует.", ex);
        }
        catch (PostgresException ex) when (ex.SqlState == "23503")
        {
            throw new InvalidOperationException(
                $"Роль с id = {roleId} не найдена.", ex);
        }
        catch (PostgresException ex) when (ex.SqlState == "22023")
        {
            throw new ArgumentException(ex.MessageText, ex);
        }
    }

    /// <summary>
    /// Возвращает id пользователя по логину или null, если пользователь не найден.
    /// </summary>
    public async Task<long?> GetUserIdByLoginAsync(string login)
    {
        await using var tx = await _connection.BeginTransactionAsync();
        await DatabaseConfig.SetSessionContextAsync(_connection, tx, 1, 1);

        const string sql = "SELECT id FROM app_users WHERE login = @login;";
        await using var cmd = new NpgsqlCommand(sql, _connection, tx);
        cmd.Parameters.AddWithValue("login", login);

        var result = await cmd.ExecuteScalarAsync();
        await tx.CommitAsync();

        return result is long id ? id : null;
    }

    /// <summary>
    /// Удаляет пользователя с заданной стратегией обработки его фотографий.
    /// </summary>
    /// <param name="userId">id удаляемого пользователя.</param>
    /// <param name="mode">Стратегия обработки фотографий.</param>
    /// <param name="targetUserId">Целевой пользователь (обязателен для режима Transfer).</param>
    public async Task DeleteUserAsync(
        long userId,
        DeleteUserMode mode,
        long? targetUserId = null)
    {
        string modeStr = mode switch
        {
            DeleteUserMode.DeletePhotos => "delete_photos",
            DeleteUserMode.Transfer => "transfer",
            DeleteUserMode.Unrestrict => "unrestrict",
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

        if (mode == DeleteUserMode.Transfer && targetUserId is null)
            throw new ArgumentException(
                "Для режима Transfer необходимо указать targetUserId.",
                nameof(targetUserId));

        await using var tx = await _connection.BeginTransactionAsync();
        await DatabaseConfig.SetSessionContextAsync(_connection, tx, 1, 1);

        const string sql = "CALL delete_user(@userId, @mode, @targetUserId);";
        await using var cmd = new NpgsqlCommand(sql, _connection, tx);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("mode", modeStr);
        cmd.Parameters.AddWithValue("targetUserId", (object?)targetUserId ?? DBNull.Value);

        try
        {
            await cmd.ExecuteNonQueryAsync();
            await tx.CommitAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == "23503")
        {
            throw new InvalidOperationException(
                $"Нельзя удалить пользователя {userId}: он ещё владелец фотографий. " +
                $"Сначала переназначьте владельца через ReassignPhotosOwnerAsync.", ex);
        }
        catch (PostgresException ex) when (ex.SqlState == "42501")
        {
            throw new InvalidOperationException(
                "Недостаточно прав для удаления пользователя.", ex);
        }
        catch (PostgresException ex) when (ex.SqlState == "P0002")
        {
            throw new InvalidOperationException(
                $"Пользователь {userId} не найден.", ex);
        }
        catch (PostgresException ex) when (ex.SqlState == "22023")
        {
            throw new ArgumentException(ex.MessageText, ex);
        }
    }

    /// <summary>
    /// Переназначает владельца всех фотографий с одного пользователя на другого.
    /// </summary>
    public async Task ReassignPhotosOwnerAsync(long fromUserId, long toUserId)
    {
        await using var tx = await _connection.BeginTransactionAsync();
        await DatabaseConfig.SetSessionContextAsync(_connection, tx, 1, 1);

        const string sql = "CALL reassign_photos_owner(@from, @to);";
        await using var cmd = new NpgsqlCommand(sql, _connection, tx);
        cmd.Parameters.AddWithValue("from", fromUserId);
        cmd.Parameters.AddWithValue("to", toUserId);

        try
        {
            await cmd.ExecuteNonQueryAsync();
            await tx.CommitAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == "P0002")
        {
            throw new InvalidOperationException(
                $"Целевой пользователь {toUserId} не найден.", ex);
        }
    }

    // ========================================================================
    // Фотографии
    // ========================================================================

    /// <summary>
    /// Создаёт запись о фотографии (без атрибутов). Возвращает id.
    /// </summary>
    public async Task<Guid> AddPhotoAsync(
        long ownerAdminId,
        string bucket,
        string objectKey,
        string? title = null)
    {
        await using var tx = await _connection.BeginTransactionAsync();
        await DatabaseConfig.SetSessionContextAsync(_connection, tx, ownerAdminId, 1);

        const string sql = @"
            INSERT INTO photos (owner_admin_id, bucket, object_key, title)
            VALUES (@owner, @bucket, @key, @title)
            RETURNING id;";

        await using var cmd = new NpgsqlCommand(sql, _connection, tx);
        cmd.Parameters.AddWithValue("owner", ownerAdminId);
        cmd.Parameters.AddWithValue("bucket", bucket);
        cmd.Parameters.AddWithValue("key", objectKey);
        cmd.Parameters.AddWithValue("title", (object)title ?? DBNull.Value);

        try
        {
            var result = await cmd.ExecuteScalarAsync();
            await tx.CommitAsync();
            return (Guid)result!;
        }
        catch (PostgresException ex) when (ex.SqlState == "23503")
        {
            throw new InvalidOperationException(
                $"Пользователь с id = {ownerAdminId} не найден. " +
                $"Зарегистрируйте его через RegisterUserAsync перед созданием фотографии.", ex);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            throw new InvalidOperationException(
                $"Объект S3 '{bucket}/{objectKey}' уже привязан к другой фотографии.", ex);
        }
    }

    /// <summary>
    /// Возвращает id фотографии по паре (bucket, object_key) или null,
    /// если записи с таким S3-объектом нет.
    /// </summary>
    public async Task<Guid?> GetPhotoIdByS3Async(string bucket, string objectKey)
    {
        await using var tx = await _connection.BeginTransactionAsync();
        await DatabaseConfig.SetSessionContextAsync(_connection, tx, 1, 1);

        const string sql = @"
            SELECT id FROM photos
            WHERE  bucket = @bucket AND object_key = @key;";

        Guid? result = null;

        await using (var cmd = new NpgsqlCommand(sql, _connection, tx))
        {
            cmd.Parameters.AddWithValue("bucket", bucket);
            cmd.Parameters.AddWithValue("key", objectKey);

            var scalar = await cmd.ExecuteScalarAsync();
            if (scalar is Guid id) result = id;
        }

        await tx.CommitAsync();
        return result;
    }

    /// <summary>
    /// Удаляет фотографию. Связанные записи (ACL, атрибуты, ссылки) удаляются каскадом.
    /// </summary>
    public async Task DeletePhotoAsync(Guid photoId)
    {
        await using var tx = await _connection.BeginTransactionAsync();
        await DatabaseConfig.SetSessionContextAsync(_connection, tx, 1, 1);

        const string sql = "DELETE FROM photos WHERE id = @id;";
        await using var cmd = new NpgsqlCommand(sql, _connection, tx);
        cmd.Parameters.AddWithValue("id", photoId);

        await cmd.ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }

    // ========================================================================
    // Атрибуты
    // ========================================================================

    /// <summary>
    /// Создаёт или обновляет атрибуты фотографии (включая миниатюру).
    /// Патч-семантика: переданные значения перетирают существующие,
    /// null означает «не трогать». extra мёржится через ||.
    /// </summary>
    public async Task UpsertAttributesAsync(
        Guid photoId,
        byte[]? thumbnailData = null,
        string? thumbnailMime = null,
        int? thumbnailWidth = null,
        int? thumbnailHeight = null,
        double? lon = null,
        double? lat = null,
        string? address = null,
        string? authorName = null,
        string? extraJson = null)
    {
        await using var tx = await _connection.BeginTransactionAsync();
        await DatabaseConfig.SetSessionContextAsync(_connection, tx, 1, 1);

        const string sql = @"
        CALL attribute_upsert(
            p_photo_id         => @photoId,
            p_thumbnail_data   => @data,
            p_thumbnail_mime   => @mime,
            p_thumbnail_width  => @width,
            p_thumbnail_height => @height,
            p_lon              => @lon,
            p_lat              => @lat,
            p_address          => @address,
            p_author_name      => @authorName,
            p_extra            => @extra
        );";

        await using var cmd = new NpgsqlCommand(sql, _connection, tx);

        cmd.Parameters.AddWithValue("photoId", photoId);

        cmd.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Bytea)
        {
            Value = (object?)thumbnailData ?? DBNull.Value
        });

        cmd.Parameters.AddWithValue("mime", (object?)thumbnailMime ?? DBNull.Value);
        cmd.Parameters.AddWithValue("width", (object?)thumbnailWidth ?? DBNull.Value);
        cmd.Parameters.AddWithValue("height", (object?)thumbnailHeight ?? DBNull.Value);
        cmd.Parameters.AddWithValue("lon", (object?)lon ?? DBNull.Value);
        cmd.Parameters.AddWithValue("lat", (object?)lat ?? DBNull.Value);
        cmd.Parameters.AddWithValue("address", (object?)address ?? DBNull.Value);
        cmd.Parameters.AddWithValue("authorName", (object?)authorName ?? DBNull.Value);

        cmd.Parameters.Add(new NpgsqlParameter("extra", NpgsqlDbType.Jsonb)
        {
            Value = (object?)extraJson ?? DBNull.Value
        });

        try
        {
            await cmd.ExecuteNonQueryAsync();
            await tx.CommitAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == "P0002")
        {
            throw new InvalidOperationException(
                $"Фотография {photoId} не найдена.", ex);
        }
        catch (PostgresException ex) when (ex.SqlState == "22023")
        {
            throw new ArgumentException(ex.MessageText, ex);
        }
    }
    /// <summary>
    /// Обнуляет указанное поле атрибутов: 'thumbnail', 'geo', 'address', 'author', 'extra'.
    /// </summary>
    public async Task ClearAttributeFieldAsync(Guid photoId, string field)
    {
        await using var tx = await _connection.BeginTransactionAsync();
        await DatabaseConfig.SetSessionContextAsync(_connection, tx, 1, 1);

        const string sql = "CALL attribute_clear_field(@photoId, @field);";
        await using var cmd = new NpgsqlCommand(sql, _connection, tx);
        cmd.Parameters.AddWithValue("photoId", photoId);
        cmd.Parameters.AddWithValue("field", field);

        try
        {
            await cmd.ExecuteNonQueryAsync();
            await tx.CommitAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == "22023")
        {
            throw new ArgumentException(ex.MessageText, ex);
        }
    }

    /// <summary>
    /// Добавляет или обновляет произвольные поля в extra JSON.
    /// </summary>
    public async Task SetExtraAttributeAsync(Guid photoId, string extraJson)
    {
        await using var tx = await _connection.BeginTransactionAsync();
        await DatabaseConfig.SetSessionContextAsync(_connection, tx, 1, 1);

        const string sql = "CALL attribute_set_extra(@photoId, @extra::jsonb);";
        await using var cmd = new NpgsqlCommand(sql, _connection, tx);
        cmd.Parameters.AddWithValue("photoId", photoId);
        cmd.Parameters.AddWithValue("extra", extraJson);

        await cmd.ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }

    /// <summary>
    /// Удаляет перечисленные ключи из extra.
    /// </summary>
    public async Task RemoveExtraKeysAsync(Guid photoId, string[] keys)
    {
        await using var tx = await _connection.BeginTransactionAsync();
        await DatabaseConfig.SetSessionContextAsync(_connection, tx, 1, 1);

        const string sql = "CALL attribute_remove_extra_keys(@photoId, @keys);";
        await using var cmd = new NpgsqlCommand(sql, _connection, tx);
        cmd.Parameters.AddWithValue("photoId", photoId);
        cmd.Parameters.AddWithValue("keys", keys);

        await cmd.ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }

    // ========================================================================
    // ACL
    // ========================================================================

    /// <summary>
    /// Выдаёт право чтения фотографии пользователю ('U') или роли ('R').
    /// </summary>
    public async Task GrantPhotoReadAsync(Guid photoId, char subjectType, long subjectId)
    {
        if (subjectType != 'U' && subjectType != 'R')
            throw new ArgumentException("subjectType должен быть 'U' или 'R'.",
                nameof(subjectType));

        await using var tx = await _connection.BeginTransactionAsync();
        await DatabaseConfig.SetSessionContextAsync(_connection, tx, 1, 1);

        const string sql = "CALL grant_photo_read(@photoId, @type, @subjectId);";
        await using var cmd = new NpgsqlCommand(sql, _connection, tx);
        cmd.Parameters.AddWithValue("photoId", photoId);
        cmd.Parameters.AddWithValue("type", subjectType);
        cmd.Parameters.AddWithValue("subjectId", subjectId);

        try
        {
            await cmd.ExecuteNonQueryAsync();
            await tx.CommitAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == "P0002")
        {
            throw new InvalidOperationException(
                $"Субъект типа '{subjectType}' с id = {subjectId} не найден.", ex);
        }
    }

    /// <summary>
    /// Отзывает право чтения фотографии у пользователя или роли.
    /// </summary>
    public async Task RevokePhotoReadAsync(Guid photoId, char subjectType, long subjectId)
    {
        if (subjectType != 'U' && subjectType != 'R')
            throw new ArgumentException("subjectType должен быть 'U' или 'R'.",
                nameof(subjectType));

        await using var tx = await _connection.BeginTransactionAsync();
        await DatabaseConfig.SetSessionContextAsync(_connection, tx, 1, 1);

        const string sql = "CALL revoke_photo_read(@photoId, @type, @subjectId);";
        await using var cmd = new NpgsqlCommand(sql, _connection, tx);
        cmd.Parameters.AddWithValue("photoId", photoId);
        cmd.Parameters.AddWithValue("type", subjectType);
        cmd.Parameters.AddWithValue("subjectId", subjectId);

        await cmd.ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }

    // ========================================================================
    // Связи
    // ========================================================================

    /// <summary>
    /// Создаёт (или обновляет комментарий) связь между двумя фотографиями.
    /// </summary>
    public async Task AddLinkAsync(Guid idF, Guid idB, string? comment = null)
    {
        await using var tx = await _connection.BeginTransactionAsync();
        await DatabaseConfig.SetSessionContextAsync(_connection, tx, 1, 1);

        const string sql = "CALL link_add(@idF, @idB, @comment);";
        await using var cmd = new NpgsqlCommand(sql, _connection, tx);
        cmd.Parameters.AddWithValue("idF", idF);
        cmd.Parameters.AddWithValue("idB", idB);
        cmd.Parameters.AddWithValue("comment", (object?)comment ?? DBNull.Value);

        try
        {
            await cmd.ExecuteNonQueryAsync();
            await tx.CommitAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == "23503")
        {
            throw new InvalidOperationException(
                "Одна из фотографий не существует.", ex);
        }
        catch (PostgresException ex) when (ex.SqlState == "23514")  // check_violation
        {
            throw new InvalidOperationException(
                "Фотография не может ссылаться на саму себя.", ex);
        }
    }

    /// <summary>
    /// Удаляет связь между двумя фотографиями.
    /// </summary>
    public async Task RemoveLinkAsync(Guid idF, Guid idB)
    {
        await using var tx = await _connection.BeginTransactionAsync();
        await DatabaseConfig.SetSessionContextAsync(_connection, tx, 1, 1);

        const string sql = "CALL link_remove(@idF, @idB);";
        await using var cmd = new NpgsqlCommand(sql, _connection, tx);
        cmd.Parameters.AddWithValue("idF", idF);
        cmd.Parameters.AddWithValue("idB", idB);

        await cmd.ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }


    /// <summary>
    /// Добавляет задачу в cleanup_queue.
    /// </summary>
    public async Task EnqueueCleanupAsync(
        string actionType,
        string payloadJson,
        string? errorMessage = null)
    {
        await using var tx = await _connection.BeginTransactionAsync();
        await DatabaseConfig.SetSessionContextAsync(_connection, tx, 1, 1);

        const string sql = @"
        INSERT INTO cleanup_queue(action_type, payload, error_message)
        VALUES (@type, @payload::jsonb, @error);";

        await using var cmd = new NpgsqlCommand(sql, _connection, tx);
        cmd.Parameters.AddWithValue("type", actionType);
        cmd.Parameters.AddWithValue("payload", payloadJson);
        cmd.Parameters.AddWithValue("error", (object?)errorMessage ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }

    /// <summary>
    /// Возвращает bucket и object_key фотографии по её id.
    /// Используется при удалении/скачивании объекта из S3.
    /// </summary>
    public async Task<(string Bucket, string ObjectKey)?> GetS3InfoAsync(Guid photoId)
    {
        await using var tx = await _connection.BeginTransactionAsync();
        await DatabaseConfig.SetSessionContextAsync(_connection, tx, 1, 1);

        const string sql = "SELECT bucket, object_key FROM photos WHERE id = @id;";
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

    /// <summary>
    /// Возвращает число необработанных задач в cleanup_queue.
    /// </summary>
    public async Task<long> GetPendingCleanupCountAsync()
    {
        await using var tx = await _connection.BeginTransactionAsync();
        await DatabaseConfig.SetSessionContextAsync(_connection, tx, 1, 1);

        const string sql =
            "SELECT count(*) FROM cleanup_queue WHERE processed_at IS NULL;";

        await using (var cmd = new NpgsqlCommand(sql, _connection, tx))
        {
            var result = await cmd.ExecuteScalarAsync();
            await tx.CommitAsync();
            return (long)result!;
        }
    }

    /// <summary>
    /// Возвращает список всех фотографий бакета: (id, object_key).
    /// Используется в режиме очистки -d.
    /// </summary>
    public async Task<List<(Guid Id, string ObjectKey)>> GetAllPhotosInBucketAsync(string bucket)
    {
        await using var tx = await _connection.BeginTransactionAsync();
        await DatabaseConfig.SetSessionContextAsync(_connection, tx, 1, 1);

        const string sql = "SELECT id, object_key FROM photos WHERE bucket = @b;";
        var list = new List<(Guid, string)>();

        await using (var cmd = new NpgsqlCommand(sql, _connection, tx))
        {
            cmd.Parameters.AddWithValue("b", bucket);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                list.Add((reader.GetGuid(0), reader.GetString(1)));
        }

        await tx.CommitAsync();
        return list;
    }

    /// <summary>
    /// Устанавливает или снимает флаг is_public у фотографии.
    /// </summary>
    public async Task SetPhotoPublicAsync(Guid photoId, bool isPublic)
    {
        await using var tx = await _connection.BeginTransactionAsync();
        await DatabaseConfig.SetSessionContextAsync(_connection, tx, 1, 1);

        const string sql = "UPDATE photos SET is_public = @p WHERE id = @id;";
        await using var cmd = new NpgsqlCommand(sql, _connection, tx);
        cmd.Parameters.AddWithValue("p", isPublic);
        cmd.Parameters.AddWithValue("id", photoId);

        await cmd.ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }

    /// <summary>
    /// Возвращает id роли приложения по имени или null, если не найдена.
    /// </summary>
    public async Task<long?> GetRoleIdByNameAsync(string name)
    {
        await using var tx = await _connection.BeginTransactionAsync();
        await DatabaseConfig.SetSessionContextAsync(_connection, tx, 1, 1);

        const string sql = "SELECT id FROM app_roles WHERE name = @n;";
        long? result = null;

        await using (var cmd = new NpgsqlCommand(sql, _connection, tx))
        {
            cmd.Parameters.AddWithValue("n", name);
            var r = await cmd.ExecuteScalarAsync();
            if (r is long id) result = id;
        }

        await tx.CommitAsync();
        return result;
    }
    public void Dispose()
    {
        _connection?.Dispose();
    }
}