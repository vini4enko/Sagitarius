using Npgsql;

namespace PhotoOrganizer.Data;

/// <summary>
/// Конфигурация подключения и общая логика установки контекста сессии.
/// В штатном приложении строки подключения должны читаться из конфигурации
/// или переменных окружения; здесь они вынесены в константы для теста.
/// </summary>
public static class DatabaseConfig
{
    public const string AdminConnectionString =
        "Host=46.19.66.115;Port=5432;Database=photo_organizer;" +
        "Username=app_admin;Password=change_me_admin;";

    public const string UserConnectionString =
        "Host=46.19.66.115;Port=5432;Database=photo_organizer;" +
        "Username=app_user;Password=change_me_user;";

    /// <summary>
    /// Устанавливает контекст сессии (app.user_id, app.role_id) в текущей транзакции.
    /// Использовать сразу после BeginTransaction и до выполнения защищённых запросов.
    /// SET LOCAL гарантирует сброс переменных при COMMIT/ROLLBACK, что критично
    /// при работе через пул соединений Npgsql.
    /// </summary>
    public static async Task SetSessionContextAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long userId,
        long roleId)
    {
        const string sql =
            "SELECT set_config('app.user_id', $1, true), " +
            "       set_config('app.role_id', $2, true);";

        await using var cmd = new NpgsqlCommand(sql, connection, transaction);
        cmd.Parameters.AddWithValue(userId.ToString());
        cmd.Parameters.AddWithValue(roleId.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task SetAnonymousContextAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction)
    {
        const string sql =
            "SELECT set_config('app.user_id', '', true), " +
            "       set_config('app.role_id', '', true);";

        await using var cmd = new NpgsqlCommand(sql, connection, transaction);
        await cmd.ExecuteNonQueryAsync();
    }
}