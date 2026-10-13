using Microsoft.Extensions.Options;
using PhotoOrganizer.Data;
using PhotoOrganizer.Data.Repositories;
using PhotoOrganizer.Storage;

namespace PhotoOrganizer.Storage.Tests;

class Program
{
    static async Task Main(string[] args)
    {
        var cfg = TestConfig.Load();
        Console.WriteLine($"S3 endpoint: {cfg.S3.Endpoint}");
        Console.WriteLine($"S3 bucket:   {cfg.S3.BucketName}");
        Console.WriteLine();

        var s3Options = Options.Create(cfg.S3);

        using var s3Service = new S3StorageService(s3Options);
        using var adminRepo = new AdminRepository();
        var photoService = new PhotoService(s3Service, adminRepo, s3Options);

        // ----------------------------------------------------------------
        // Сценарий 1: загрузка фотографии в S3 + запись в БД
        // ----------------------------------------------------------------
        Console.WriteLine("=== Сценарий 1: UploadPhotoAsync ===");
        long adminId;
        try
        {
            adminId = await adminRepo.RegisterUserAsync(cfg.Test.AdminLogin, cfg.Test.AdminPassword);
            Console.WriteLine($"Зарегистрирован {cfg.Test.AdminLogin}, id = {adminId}");
        }
        catch (InvalidOperationException)
        {
            adminId = (await adminRepo.GetUserIdByLoginAsync(cfg.Test.AdminLogin))!.Value;
            Console.WriteLine($"{cfg.Test.AdminLogin} уже существует, id = {adminId}");
        }

        string sourcePath = Path.Combine(AppContext.BaseDirectory, "example.jpeg");
        if (!File.Exists(sourcePath))
        {
            Console.Error.WriteLine($"Тестовый JPEG не найден: {sourcePath}");
            return;
        }

        Guid photoId;
        try
        {
            await using var fileStream = File.OpenRead(sourcePath);
            photoId = await photoService.UploadPhotoAsync(
                ownerAdminId: adminId,
                fileStream: fileStream,
                objectKey: cfg.Test.TestObjectKey,
                contentType: "image/jpeg",
                title: "Тестовая фотография",
                lon: 37.6173,
                lat: 55.7558,
                address: "Москва",
                authorName: "Иван Петров");

            Console.WriteLine($"OK: фотография загружена, id = {photoId}");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("уже привязан"))
        {
            Console.WriteLine("Объект уже загружен в прошлый раз. Используем существующий.");
            photoId = (await adminRepo.GetPhotoIdByS3Async(cfg.S3.BucketName, cfg.Test.TestObjectKey))!.Value;
        }

        // ----------------------------------------------------------------
        // Сценарий 2: скачивание фотографии из S3
        // ----------------------------------------------------------------
        Console.WriteLine();
        Console.WriteLine("=== Сценарий 2: DownloadPhotoAsync ===");
        try
        {
            await using var downloaded = await photoService.DownloadPhotoAsync(
                photoId, userId: adminId, roleId: 1);

            if (downloaded == null)
            {
                Console.Error.WriteLine("Не удалось скачать: фотография не найдена или нет доступа.");
            }
            else
            {
                using var ms = new MemoryStream();
                await downloaded.CopyToAsync(ms);
                Console.WriteLine($"OK: скачано {ms.Length} байт из S3.");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Ошибка скачивания: {ex.Message}");
        }

        // ----------------------------------------------------------------
        // Сценарий 3: удаление фотографии (БД + S3)
        // ----------------------------------------------------------------
        Console.WriteLine();
        Console.WriteLine("=== Сценарий 3: DeletePhotoAsync ===");
        try
        {
            await photoService.DeletePhotoAsync(photoId);
            Console.WriteLine($"OK: фотография {photoId} удалена из БД и из S3.");

            // Проверяем, что её больше нет в БД
            var s3Info = await adminRepo.GetS3InfoAsync(photoId);
            Console.WriteLine(s3Info is null
                ? "OK: запись в БД отсутствует."
                : "ПРЕДУПРЕЖДЕНИЕ: запись в БД осталась!");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Ошибка удаления: {ex.Message}");
        }

        // ----------------------------------------------------------------
        // Сценарий 4: проверка cleanup_queue
        // ----------------------------------------------------------------
        Console.WriteLine();
        Console.WriteLine("=== Сценарий 4: cleanup_queue ===");
        var pendingCount = await adminRepo.GetPendingCleanupCountAsync();
        Console.WriteLine($"Необработанных задач в cleanup_queue: {pendingCount}");

        Console.WriteLine();
        Console.WriteLine("Все сценарии завершены.");
    }
}