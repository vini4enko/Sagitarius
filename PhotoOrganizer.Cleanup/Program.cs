using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;
using PhotoOrganizer.Cleanup;
using PhotoOrganizer.Data;
using PhotoOrganizer.Storage;

class Program
{
    static async Task<int> Main(string[] args)
    {
        // 1. Если аргументов нет или запрошена справка — показываем её и выходим.
        if (args.Length == 0 || args.Contains("-h") || args.Contains("--help"))
        {
            PrintHelp();
            return 0; // 0 означает успешное завершение (справка — это не ошибка)
        }

        var opts = ParseArgs(args);
        if (opts is null)
            return 1; // 1 — ошибка в аргументах командной строки

        // Загружаем S3-настройки
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("d:\\Sagitarius\\appsettings.Shared.json", optional: false)
            .Build();

        var s3Options = new S3Options();
        configuration.GetSection("S3").Bind(s3Options);

        Console.WriteLine("=== PhotoOrganizer Cleanup ===");
        Console.WriteLine($"S3 endpoint: {s3Options.Endpoint}");
        Console.WriteLine($"S3 bucket:   {s3Options.BucketName}");
        Console.WriteLine($"Режим:       {(opts.DryRun ? "DRY-RUN (без изменений)" : "РЕАЛЬНАЯ ОЧИСТКА")}");
        Console.WriteLine($"Что чистим:  {(opts.CleanS3 ? "S3 " : "")}{(opts.CleanDb ? "БД" : "")}".TrimEnd());
        Console.WriteLine();

        // Подтверждение
        if (!opts.DryRun && !opts.AssumeYes)
        {
            Console.Write("Уверены? Введите 'yes' для подтверждения: ");
            var answer = Console.ReadLine();
            if (!string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Отменено.");
                return 0;
            }
        }

        int exitCode = 0;

        if (opts.CleanS3)
        {
            try
            {
                await CleanS3Async(s3Options, opts.DryRun);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Ошибка при очистке S3: {ex.Message}");
                exitCode = 2; // 2 — ошибка во время выполнения
            }
        }

        if (opts.CleanDb)
        {
            try
            {
                await CleanDbAsync(opts.DryRun);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Ошибка при очистке БД: {ex.Message}");
                exitCode = 2;
            }
        }

        Console.WriteLine();
        Console.WriteLine(exitCode == 0 ? "Готово." : "Завершено с ошибками.");
        return exitCode;
    }

    // ====================================================================
    // Парсинг аргументов
    // ====================================================================
    static CleanupOptions? ParseArgs(string[] args)
    {
        var opts = new CleanupOptions();

        foreach (var a in args)
        {
            switch (a.ToLowerInvariant())
            {
                case "--s3": opts.CleanS3 = true; break;
                case "--db": opts.CleanDb = true; break;
                case "--all": opts.CleanS3 = opts.CleanDb = true; break;
                case "--dry-run": opts.DryRun = true; break;
                case "--yes":
                case "-y": opts.AssumeYes = true; break;
                default:
                    Console.Error.WriteLine($"Неизвестная опция: {a}");
                    Console.Error.WriteLine("Для получения справки запустите программу без параметров или с ключом --help.");
                    return null;
            }
        }

        // По умолчанию — чистим всё, если явно не указано только S3 или только БД
        if (!opts.CleanS3 && !opts.CleanDb)
            opts.CleanS3 = opts.CleanDb = true;

        return opts;
    }

    static void PrintHelp()
    {
        Console.WriteLine("=== PhotoOrganizer Cleanup ===");
        Console.WriteLine("Утилита для полной очистки S3-хранилища и контентных таблиц БД.");
        Console.WriteLine();
        Console.WriteLine("Использование:");
        Console.WriteLine("  PhotoOrganizer.Cleanup [опции]");
        Console.WriteLine();
        Console.WriteLine("Опции:");
        Console.WriteLine("  --s3        Очистить только S3-хранилище");
        Console.WriteLine("  --db        Очистить только БД");
        Console.WriteLine("  --all       Очистить и S3, и БД (действует по умолчанию)");
        Console.WriteLine("  --dry-run   Показать, что будет удалено, без внесения изменений");
        Console.WriteLine("  --yes, -y   Не запрашивать интерактивное подтверждение");
        Console.WriteLine("  -h, --help  Показать эту справку");
        Console.WriteLine();
        Console.WriteLine("Что удаляется в БД:");
        Console.WriteLine("  photos, photo_acl, attributes, link, shows, show_photos, cleanup_queue");
        Console.WriteLine();
        Console.WriteLine("Что НЕ трогается:");
        Console.WriteLine("  app_roles, app_users, user_roles");
        Console.WriteLine();
        Console.WriteLine("Примеры:");
        Console.WriteLine("  PhotoOrganizer.Cleanup --dry-run");
        Console.WriteLine("  PhotoOrganizer.Cleanup --s3 --yes");
        Console.WriteLine("  PhotoOrganizer.Cleanup --db");
    }

    // ====================================================================
    // Очистка S3
    // ====================================================================
    static async Task CleanS3Async(S3Options s3Options, bool dryRun)
    {
        Console.WriteLine("--- S3 ---");

        using var s3 = new S3StorageService(
            Microsoft.Extensions.Options.Options.Create(s3Options));

        var keys = await s3.ListAllKeysAsync();
        Console.WriteLine($"Объектов в бакете: {keys.Count}");

        if (keys.Count == 0)
        {
            Console.WriteLine("Нечего удалять.");
            return;
        }

        if (dryRun)
        {
            Console.WriteLine("DRY-RUN: ключи, которые были бы удалены:");
            foreach (var k in keys.Take(20))
                Console.WriteLine($"  {k}");
            if (keys.Count > 20)
                Console.WriteLine($"  ... и ещё {keys.Count - 20}");
            return;
        }

        Console.WriteLine("Удаление...");
        int deleted = await s3.DeleteManyAsync(keys);
        Console.WriteLine($"Удалено объектов: {deleted}");
    }

    // ====================================================================
    // Очистка БД
    // ====================================================================
    static async Task CleanDbAsync(bool dryRun)
    {
        Console.WriteLine("--- БД ---");

        await using var conn = new NpgsqlConnection(DatabaseConfig.AdminConnectionString);
        await conn.OpenAsync();

        var tables = new[]
        {
            "photos", "photo_acl", "attributes", "link",
            "shows",  "show_photos", "cleanup_queue"
        };

        Console.WriteLine("Содержимое таблиц до очистки:");
        long grandTotal = 0;
        foreach (var t in tables)
        {
            await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM {t};", conn);
            var n = (long)(await cmd.ExecuteScalarAsync())!;
            grandTotal += n;
            Console.WriteLine($"  {t,-16} {n}");
        }
        Console.WriteLine($"  {"ИТОГО",-16} {grandTotal}");

        if (grandTotal == 0)
        {
            Console.WriteLine("Нечего удалять.");
            return;
        }

        if (dryRun)
        {
            Console.WriteLine("DRY-RUN: записи не удалялись.");
            return;
        }

        await using var tx = await conn.BeginTransactionAsync();

        // shows — сначала: удалит show_photos каскадом.
        await ExecAsync(conn, tx, "DELETE FROM shows;");
        // photos — удалит photo_acl, attributes, link, show_photos каскадом.
        await ExecAsync(conn, tx, "DELETE FROM photos;");
        // Очередь компенсаций.
        await ExecAsync(conn, tx, "DELETE FROM cleanup_queue;");

        // Сбрасываем IDENTITY, чтобы новые записи начинались с 1.
        await ExecAsync(conn, tx, "ALTER TABLE shows         ALTER COLUMN id RESTART WITH 1;");
        await ExecAsync(conn, tx, "ALTER TABLE cleanup_queue ALTER COLUMN id RESTART WITH 1;");

        await tx.CommitAsync();

        Console.WriteLine("Таблицы очищены. Справочники (app_roles, app_users, user_roles) не тронуты.");
    }

    static async Task ExecAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        await cmd.ExecuteNonQueryAsync();
    }
}