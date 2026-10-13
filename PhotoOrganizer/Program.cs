using PhotoOrganizer;
using PhotoOrganizer.Data;
using PhotoOrganizer.Data.Repositories;

class Program
{
    static async Task Main()
    {
        var thumbOptions = new ThumbnailOptions
        {
            MaxWidth = 320,
            MaxHeight = 240,
            MimeType = "image/webp",
            Quality = 80,
            KeepOriginalIfSmaller = true
        };

        string sourcePath = Path.Combine(AppContext.BaseDirectory, "example.jpeg");
        if (!File.Exists(sourcePath))
        {
            Console.Error.WriteLine($"Тестовый JPEG не найден: {sourcePath}");
            return;
        }

        long adminId;
        using (var admin = new AdminRepository())
        {
            try
            {
                adminId = await admin.RegisterUserAsync("admin", "admin_pass_2026");
                Console.WriteLine($"Зарегистрирован admin, id = {adminId}");
            }
            catch (InvalidOperationException)
            {
                adminId = (await admin.GetUserIdByLoginAsync("admin"))!.Value;
                Console.WriteLine($"admin уже существует, id = {adminId}");
            }
        }

        Guid photoId;
        using (var admin = new AdminRepository())
        {
            try
            {
                photoId = await admin.AddPhotoAsync(
                    ownerAdminId: adminId,
                    bucket: "my-photos",
                    objectKey: "2026/summer/photo1.jpg",
                    title: "Летний закат");
                Console.WriteLine($"Создана фотография: {photoId}");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("уже привязан"))
            {
                // Повторный запуск: объект уже загружен. Берём существующий id.
                Console.WriteLine(
                    "Внимание: объект S3 уже привязан к фотографии в базе. " +
                    "Используем существующую запись.");

                photoId = (await admin.GetPhotoIdByS3Async("my-photos", "2026/summer/photo1.jpg"))
                    ?? throw new InvalidOperationException(
                        "Сообщение говорит «уже привязан», но записи в БД нет. " +
                        "Проверьте целостность данных.");
                Console.WriteLine($"Используем существующую фотографию: {photoId}");
            }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine($"Не удалось создать фотографию: {ex.Message}");
                return;
            }

            ThumbnailResult thumb = ThumbnailGenerator.Generate(sourcePath, thumbOptions);
            Console.WriteLine(
                $"Миниатюра: {thumb.Mime}, {thumb.Width}x{thumb.Height}, {thumb.Bytes} байт");

            try
            {
                await admin.UpsertAttributesAsync(
                    photoId: photoId,
                    thumbnailData: thumb.Data,
                    thumbnailMime: thumb.Mime,
                    thumbnailWidth: thumb.Width,
                    thumbnailHeight: thumb.Height,
                    lon: 37.6173,
                    lat: 55.7558,
                    address: "Москва, Красная площадь",
                    authorName: "Иван Петров");

                await admin.GrantPhotoReadAsync(photoId, 'U', adminId);
            }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine(
                    $"Не удалось записать атрибуты или выдать права: {ex.Message}");
                return;
            }
        }

        using (var user = new UserRepository())
        {
            try
            {
                var photos = await user.GetVisiblePhotosAsync(adminId, 1);
                foreach (var p in photos)
                    Console.WriteLine($"  {p.Id} | {p.Title} | {p.AuthorName}");

                if (photos.Count > 0)
                {
                    var thumb = await user.GetThumbnailAsync(photos[0].Id, adminId, 1);
                    if (thumb != null)
                        Console.WriteLine(
                            $"  Миниатюра: {thumb.Mime}, {thumb.Width}x{thumb.Height}, " +
                            $"{thumb.Data.Length} байт");
                }

                await user.ChangePasswordAsync(
                    "admin", "admin_pass_2026", "admin_new_pass_2026");
                Console.WriteLine("Пароль admin изменён.");
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.Error.WriteLine($"Смена пароля: {ex.Message}");
            }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine($"Ошибка чтения: {ex.Message}");
            }
        }

    }
}