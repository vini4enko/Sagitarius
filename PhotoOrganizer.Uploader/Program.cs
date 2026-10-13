using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using PhotoOrganizer;
using PhotoOrganizer.Data;
using PhotoOrganizer.Data.Repositories;
using PhotoOrganizer.Storage;
using PhotoOrganizer.Uploader;
using System.Text.Json;

class Program
{
    static async Task<int> Main(string[] args)
    {
        bool deleteMode = args.Any(a => a is "-d" or "--delete");

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var s3Options = new S3Options();
        configuration.GetSection("S3").Bind(s3Options);

        var uploaderOptions = new UploaderOptions();
        configuration.GetSection("Uploader").Bind(uploaderOptions);

        using var adminRepo = new AdminRepository();
        using var s3Service = new S3StorageService(Options.Create(s3Options));
        var photoService = new PhotoService(s3Service, adminRepo, Options.Create(s3Options));

        if (deleteMode)
            return await RunDeleteAsync(adminRepo, photoService, s3Options);

        return await RunUploadAsync(adminRepo, photoService, s3Options, uploaderOptions);
    }

    // ========================================================================
    // -d : удаление всех фотографий бакета
    // ========================================================================
    static async Task<int> RunDeleteAsync(
        AdminRepository adminRepo,
        PhotoService photoService,
        S3Options s3Options)
    {
        Console.WriteLine("=== РЕЖИМ ОЧИСТКИ ===");
        Console.WriteLine($"Бакет: {s3Options.BucketName}");

        var photos = await adminRepo.GetAllPhotosInBucketAsync(s3Options.BucketName);
        Console.WriteLine($"Найдено фотографий: {photos.Count}");

        if (photos.Count == 0)
        {
            Console.WriteLine("Нечего удалять.");
        }
        else
        {
            int deleted = 0, failed = 0;
            foreach (var (id, key) in photos)
            {
                try
                {
                    await photoService.DeletePhotoAsync(id);
                    deleted++;
                    Console.WriteLine($"  Удалено: {key} ({id})");
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.Error.WriteLine($"  ОШИБКА {id}: {ex.Message}");
                }
            }

            Console.WriteLine();
            Console.WriteLine($"Удалено: {deleted}, ошибок: {failed}.");
        }

        // Остаток в cleanup_queue
        var pending = await adminRepo.GetPendingCleanupCountAsync();
        Console.WriteLine($"Необработанных задач в cleanup_queue: {pending}");
        if (pending > 0)
            Console.WriteLine("(запустите обработчик cleanup_queue отдельно)");

        return 0;
    }

    // ========================================================================
    // Обычный режим: загрузка из манифеста
    // ========================================================================
    static async Task<int> RunUploadAsync(
        AdminRepository adminRepo,
        PhotoService photoService,
        S3Options s3Options,
        UploaderOptions uploaderOptions)
    {
        Console.WriteLine($"S3:       {s3Options.Endpoint} / {s3Options.BucketName}");
        Console.WriteLine($"Каталог:  {uploaderOptions.PhotosDirectory}");
        Console.WriteLine($"Манифест: {uploaderOptions.PhotosManifest}");
        Console.WriteLine();

        string photosDir = Path.Combine(AppContext.BaseDirectory, uploaderOptions.PhotosDirectory);
        string manifestPath = Path.Combine(AppContext.BaseDirectory, uploaderOptions.PhotosManifest);

        if (!Directory.Exists(photosDir))
        {
            Console.Error.WriteLine($"Каталог не найден: {photosDir}");
            return 1;
        }
        if (!File.Exists(manifestPath))
        {
            Console.Error.WriteLine($"Манифест не найден: {manifestPath}");
            return 1;
        }

        PhotosManifest manifest;
        try
        {
            var json = await File.ReadAllTextAsync(manifestPath);
            manifest = JsonSerializer.Deserialize<PhotosManifest>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("Манифест пуст.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Ошибка чтения манифеста: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"В манифесте {manifest.Photos.Count} записей.");
        Console.WriteLine();

        // Аутентификация администратора
        long adminId;
        try
        {
            adminId = await adminRepo.RegisterUserAsync(
                uploaderOptions.AdminLogin, uploaderOptions.AdminPassword);
            Console.WriteLine($"Зарегистрирован '{uploaderOptions.AdminLogin}', id = {adminId}");
        }
        catch (InvalidOperationException)
        {
            adminId = (await adminRepo.GetUserIdByLoginAsync(uploaderOptions.AdminLogin))!.Value;
            Console.WriteLine($"'{uploaderOptions.AdminLogin}' уже существует, id = {adminId}");
        }
        Console.WriteLine();

        int ok = 0, skipped = 0, failed = 0;
        var failures = new List<(string File, string Error)>();

        foreach (var entry in manifest.Photos)
        {
            Console.WriteLine($"--- {entry.File} ---");

            string filePath = Path.Combine(photosDir, entry.File);
            if (!File.Exists(filePath))
            {
                Console.Error.WriteLine($"  ПРОПУСК: файл не найден");
                failed++;
                failures.Add((entry.File, "файл не найден"));
                continue;
            }
            if (string.IsNullOrWhiteSpace(entry.ObjectKey))
            {
                Console.Error.WriteLine($"  ОШИБКА: не указан objectKey");
                failed++;
                failures.Add((entry.File, "не указан objectKey"));
                continue;
            }

            // --- extra: albums ---
            var albums = new List<string>();
            if (entry.Albums != null)
                albums.AddRange(entry.Albums.Where(a => !string.IsNullOrWhiteSpace(a)));
            albums = albums.Distinct().ToList();

            var extraDict = new Dictionary<string, object?>();
            if (albums.Count > 0)
                extraDict["albums"] = albums.ToArray();
            string? extraJson = extraDict.Count > 0
                ? JsonSerializer.Serialize(extraDict)
                : null;

            // --- access ---
            var access = entry.Access
                ?? new PhotoAccess
                {
                    Public = manifest.Defaults.Access?.Public ?? true,
                    Users = manifest.Defaults.Access?.Users ?? new List<string>(),
                    Roles = manifest.Defaults.Access?.Roles ?? new List<string>()
                };

            string? author = entry.AuthorName ?? manifest.Defaults.AuthorName;

            try
            {
                var existingId = await adminRepo.GetPhotoIdByS3Async(
                    s3Options.BucketName, entry.ObjectKey);

                if (existingId is not null)
                {
                    Console.WriteLine($"  ПРОПУСК: уже загружено, id = {existingId}");
                    skipped++;
                    continue;
                }

                var thumb = ThumbnailGenerator.Generate(filePath, uploaderOptions.Thumbnail);

                await using var fileStream = File.OpenRead(filePath);
                Guid photoId = await photoService.UploadPhotoAsync(
                    ownerAdminId: adminId,
                    fileStream: fileStream,
                    objectKey: entry.ObjectKey,
                    contentType: "image/jpeg",
                    title: entry.Title,
                    thumbnailData: thumb.Data,
                    thumbnailMime: thumb.Mime,
                    thumbnailWidth: thumb.Width,
                    thumbnailHeight: thumb.Height,
                    lon: entry.Lon,
                    lat: entry.Lat,
                    address: entry.Address,
                    authorName: author,
                    extraJson: extraJson);

                // --- is_public ---
                await adminRepo.SetPhotoPublicAsync(photoId, access.Public);

                // --- ACL для пользователей ---
                foreach (var login in access.Users)
                {
                    long userId = await EnsureUserAsync(adminRepo, login);
                    await adminRepo.GrantPhotoReadAsync(photoId, 'U', userId);
                    Console.WriteLine($"  + доступ пользователю '{login}' (id={userId})");
                }

                // --- ACL для ролей ---
                foreach (var roleName in access.Roles)
                {
                    var roleId = await adminRepo.GetRoleIdByNameAsync(roleName);
                    if (roleId is null)
                    {
                        Console.Error.WriteLine($"  ! роль '{roleName}' не найдена, пропускаем");
                        continue;
                    }
                    await adminRepo.GrantPhotoReadAsync(photoId, 'R', roleId.Value);
                    Console.WriteLine($"  + доступ роли '{roleName}' (id={roleId})");
                }

                string visibility = access.Public
                    ? "public"
                    : $"private ({access.Users.Count} users, {access.Roles.Count} roles)";

                Console.WriteLine(
                    $"  OK: id = {photoId}, thumb = {thumb.Width}x{thumb.Height} " +
                    $"({thumb.Bytes} б), albums = [{string.Join(", ", albums)}], {visibility}");
                ok++;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  ОШИБКА: {ex.Message}");
                failed++;
                failures.Add((entry.File, ex.Message));
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Готово: успешно {ok}, пропущено {skipped}, ошибок {failed}.");

        if (failures.Count > 0)
        {
            Console.WriteLine("Неудачи:");
            foreach (var (file, error) in failures)
                Console.WriteLine($"  {file}: {error}");
        }

        return failed == 0 ? 0 : 2;
    }

    /// <summary>
    /// Регистрирует пользователя, если его нет. Пароль по умолчанию — заглушка,
    /// реальный пароль пользователь должен установить сам через ChangePasswordAsync.
    /// </summary>
    static async Task<long> EnsureUserAsync(AdminRepository adminRepo, string login)
    {
        var existing = await adminRepo.GetUserIdByLoginAsync(login);
        if (existing is not null) return existing.Value;

        Console.WriteLine($"  (регистрируем нового пользователя '{login}')");
        return await adminRepo.RegisterUserAsync(login, "default_pass_2026");
    }
}