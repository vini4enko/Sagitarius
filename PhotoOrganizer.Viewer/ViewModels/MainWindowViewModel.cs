using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Selection;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoOrganizer.Data;
using PhotoOrganizer.Data.Repositories;
using PhotoOrganizer.Storage;
using PhotoOrganizer.Viewer.Services;
using PhotoOrganizer.Viewer.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PhotoOrganizer.Viewer.Views;

namespace PhotoOrganizer.Viewer.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly PhotoQueryService _query;
    private readonly SessionService _session;

    private readonly IPhotoContentProvider _contentProvider;
    private readonly long _userId;    // <<< добавлено
    private readonly long _roleId;    // <<< добавлено

    private Dictionary<Guid, Bitmap> _fullImageCache = new();

    public MainWindowViewModel(
        PhotoQueryService query,
        SessionService session)
    {
        _query = query;
        _session = session;
        _userId = 1;   // admin  <<< захардкожено для теста
        _roleId = 2;   // analyst <<< захардкожено для теста
        var s3Options = new S3Options
        {
            Endpoint = "https://s3.twcstorage.ru",
            Region = "ru-1",
            AccessKey = "OMH4S3TXWWPWJPMILZM3",
            SecretKey = "v3lV7ceCqvsqgHhZfdKAj8SoANFl7d1dMzWyUYMf",
            BucketName = "02bb6fae-d098-43eb-a914-7e4298939304"
        };
        var s3 = new S3StorageService(
            Microsoft.Extensions.Options.Options.Create(s3Options));
        _contentProvider = new S3PhotoContentProvider(
            s3, s3Options, new AdminRepository(), new UserRepository());
        Debug.WriteLine("HELLO FROM STARTUP");
    }

    // ----- Лента --------------------------------------------------------
    public ObservableCollection<PhotoItemViewModel> Photos { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentPhoto))]
    private PhotoItemViewModel? _currentPhoto;

    public bool HasCurrentPhoto => CurrentPhoto is not null;

    [ObservableProperty] private Bitmap? _currentImage;

    // ----- Полное отображение -------------------------------------------
    [ObservableProperty]
    private Bitmap? _fullImage;

    [ObservableProperty] private string _statusText = "";

    // ----- Режим --------------------------------------------------------
    [ObservableProperty] private string _modeText = "Аноним";
    [ObservableProperty] private bool _isRegistered;

    [ObservableProperty] private string _loginInput = "";
    [ObservableProperty] private string _passwordInput = "";

    // ----- Фильтр -------------------------------------------------------
    [ObservableProperty] private bool _filterPanelExpanded;
    [ObservableProperty] private DateTimeOffset? _filterUploadedFrom;
    [ObservableProperty] private DateTimeOffset? _filterUploadedTo;
    [ObservableProperty] private DateTimeOffset? _filterTakenFrom;
    [ObservableProperty] private DateTimeOffset? _filterTakenTo;
    [ObservableProperty] private string _filterAuthor = "";
    [ObservableProperty] private string _filterAlbum = "";
    public IEnumerable<PhotoItemViewModel> MarkedPhotos =>
    Photos.Where(p => p.IsMarked);

    // ----- Счетчик -----------------------------------------------------
    private void HookPhoto(PhotoItemViewModel p)
    {
        p.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PhotoItemViewModel.IsMarked))
                OnPropertyChanged(nameof(MarkedCount));
        };
    }

    public int MarkedCount => Photos.Count(p => p.IsMarked);
    // ----- Курсор -------------------------------------------------------
    [ObservableProperty] private bool _isBusy;

    // ----- Команды ------------------------------------------------------

    [RelayCommand]
    private async Task LoadInitialAsync()
    {
        ;
        await LoadPhotosAsync();
    }

    [RelayCommand]
    private async Task ApplyFilterAsync()
    {
        ;
        await LoadPhotosAsync();
    }

    [RelayCommand]
    private async Task ResetFilterAsync()
    {
        FilterUploadedFrom = null;
        FilterUploadedTo = null;
        FilterTakenFrom = null;
        FilterTakenTo = null;
        FilterAuthor = "";
        FilterAlbum = "";
        ;
        await LoadPhotosAsync();
    }

    [RelayCommand]
    private async Task AnonymousModeAsync()
    {
        _session.SetAnonymous();
        ModeText = "Аноним";
        IsRegistered = false;
        ;
        await LoadPhotosAsync();
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(LoginInput) || string.IsNullOrEmpty(PasswordInput))
        {
            StatusText = "Введите логин и пароль.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _query.AuthenticateAsync(LoginInput, PasswordInput);
            if (result is null)
            {
                StatusText = "Неверные учётные данные.";
                return;
            }

            _session.SetRegistered(result.Value.UserId, result.Value.RoleId, LoginInput);
            ModeText = $"Пользователь: {LoginInput}";
            IsRegistered = true;
            PasswordInput = "";
            ;
            await LoadPhotosAsync();
        }
        catch (UnauthorizedAccessException)
        {
            StatusText = "Неверный пароль.";
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoadFullImageAsync()
    {

        //System.Diagnostics.Debug.WriteLine(
        //$"[DBG] LoadFullImageAsync: CurrentPhoto = {CurrentPhoto?.Id}");
        //Console.WriteLine($"[DBG] LoadFullImageAsync called, CurrentPhoto = {CurrentPhoto?.Id}");
        if (CurrentPhoto is null) return;
        //if (CurrentPhoto is null) { Console.WriteLine("CurrentPhoto is null"); return; }
        //Console.WriteLine($"Loading full image for {CurrentPhoto.Id}");
        if (_fullImageCache.TryGetValue(CurrentPhoto.Id, out var cached))
        {
            System.Diagnostics.Debug.WriteLine(
            $"[CACHE HIT] photo={CurrentPhoto.Id} — взято из памяти");
            FullImage = cached;
            return;
        }

        System.Diagnostics.Debug.WriteLine(
        $"[CACHE MISS] photo={CurrentPhoto.Id} — загрузка из S3");

        IsBusy = true;
        try
        {
            using var stream = await _contentProvider.GetFullImageAsync(
                        CurrentPhoto.Id, _userId, _roleId);

            if (stream is null) return;

            var bmp = new Bitmap(stream);
            _fullImageCache[CurrentPhoto.Id] = bmp;
            FullImage = bmp;

            System.Diagnostics.Debug.WriteLine(
                $"[CACHE STORE] photo={CurrentPhoto.Id}, " +
                $"кеш теперь содержит {_fullImageCache.Count} элементов");

            //Console.WriteLine("[DBG] requesting stream from content provider");
            //using var stream = await _contentProvider.GetFullImageAsync(
            //    CurrentPhoto.Id, _userId, _roleId);
            //Console.WriteLine($"[DBG] stream received: {stream != null}");
            //if (stream is null) { Console.WriteLine("Stream is null"); return; }
            //var bmp = new Bitmap(stream);
            //Console.WriteLine($"[DBG] bitmap created: {bmp.PixelSize}");
            //_fullImageCache[CurrentPhoto.Id] = bmp;
            //FullImage = bmp;
            //Console.WriteLine("[DBG] FullImage property assigned");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void ClearMarks()
    {
        foreach (var p in Photos)
            p.IsMarked = false;
    }

    [RelayCommand]
    private void PreviousPhoto()
    {
        if (Photos.Count == 0) return;
        var idx = CurrentPhoto is null ? -1 : Photos.IndexOf(CurrentPhoto);
        if (idx <= 0) return;
        CurrentPhoto = Photos[idx - 1];
    }

    [RelayCommand]
    private void NextPhoto()
    {
        if (Photos.Count == 0) return;
        var idx = CurrentPhoto is null ? -1 : Photos.IndexOf(CurrentPhoto);
        if (idx < 0 || idx >= Photos.Count - 1) return;
        CurrentPhoto = Photos[idx + 1];
    }

    [RelayCommand]
    private void FirstPhoto()
    {
        if (Photos.Count > 0)
            CurrentPhoto = Photos[0];
    }

    [RelayCommand]
    private void LastPhoto()
    {
        if (Photos.Count > 0)
            CurrentPhoto = Photos[^1];
    }

    public bool CanPreviousPhoto =>
        CurrentPhoto is not null && Photos.IndexOf(CurrentPhoto) > 0;

    public bool CanNextPhoto =>
        CurrentPhoto is not null && Photos.IndexOf(CurrentPhoto) < Photos.Count - 1;

    public interface IFolderPicker
    {
        Task<string?> PickFolderAsync(string title);
    }
    public IFolderPicker? FilePicker { get; set; }

    private async Task<string?> SaveCurrentToTempFileAsync(PhotoItemViewModel photo)
    {
        if (FullImage is null) return null;

        var dir = Path.Combine(Path.GetTempPath(), "PhotoOrganizer");
        Directory.CreateDirectory(dir);

        // Уникальное имя: Id гарантирует отсутствие коллизий.
        // Человекочитаемый Title оставляем как суффикс — удобно при отладке.
        var safeTitle = SanitizeFileName(photo.Title ?? "photo");
        var ext = GuessExtension(photo.ObjectKey);
        var path = Path.Combine(dir, $"{photo.Id}_{safeTitle}{ext}");

        // Всегда перезаписываем — содержимое могло измениться.
        FullImage.Save(path);

        return path;
    }
    // Убедитесь, что подключены нужные пространства имён:

    [RelayCommand]
    private async Task CopyCurrentToClipboardAsync()
    {
        if (CurrentPhoto is null) return;

        IsBusy = true;
        try
        {
            // Убеждаемся, что полное изображение загружено.
            if (FullImage is null)
                await LoadFullImageAsync();

            if (FullImage is null) return;

            var topLevel = GetTopLevel();
            if (topLevel?.Clipboard is null) return;

            // 1. Скачиваем во временный файл.
            var tempPath = await SaveCurrentToTempFileAsync(CurrentPhoto);
            if (tempPath is null) return;

            // 2. Формируем IStorageItem для буфера обмена.
            //    Исправление CS1503: TryGetFileFromPathAsync ожидает Uri.
            var file = await topLevel.StorageProvider.TryGetFileFromPathAsync(new Uri(tempPath));

            // 3. Создаём DataTransfer с двумя форматами: File и Bitmap.
            //    Исправление CS0618 и CS0117: вместо устаревшего DataObject
            //    используем DataTransfer и DataTransferItem.
            var dataTransfer = new DataTransfer();

            // Формат File (позволяет вставлять файл в проводник, мессенджеры и т.д.)
            if (file is not null)
            {
                var fileItem = new DataTransferItem();
                fileItem.Set(DataFormat.File, file);
                dataTransfer.Add(fileItem);
            }

            // Формат Bitmap (для вставки изображения в Word, Photoshop и т.п.)
            var bitmapItem = new DataTransferItem();
            bitmapItem.Set(DataFormat.Bitmap, FullImage);
            dataTransfer.Add(bitmapItem);

            // 4. Помещаем данные в буфер обмена.
            //    Исправление CS1061: вместо SetFilesAsync используем SetDataAsync.
            await topLevel.Clipboard.SetDataAsync(dataTransfer);

            StatusText = $"Скопировано в буфер: {CurrentPhoto.Title ?? CurrentPhoto.Id.ToString()}";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task CopyMarkedToDiskAsync()
    {
        var marked = MarkedPhotos.ToList();
        if (marked.Count == 0)
        {
            StatusText = "Не отмечено ни одной фотографии";
            return;
        }

        var picker = FilePicker;
        if (picker is null) return;

        var targetDir = await picker.PickFolderAsync("Куда скопировать выбранные фотографии");
        if (string.IsNullOrEmpty(targetDir)) return;

        IsBusy = true;
        int ok = 0, failed = 0;
        try
        {
            foreach (var photo in marked)
            {
                try
                {
                    // Скачиваем полное изображение.
                    using var stream = await _contentProvider.GetFullImageAsync(
                        photo.Id, _userId, _roleId);
                    if (stream is null) { failed++; continue; }

                    // Формируем имя файла с защитой от коллизий.
                    var baseName = SanitizeFileName(photo.Title ?? photo.Id.ToString());
                    var ext = GuessExtension(photo.ObjectKey);
                    var path = MakeUniquePath(targetDir, baseName, ext);

                    await using var fs = File.Create(path);
                    await stream.CopyToAsync(fs);

                    ok++;
                    StatusText = $"Скопировано {ok}/{marked.Count}: {Path.GetFileName(path)}";
                }
                catch (Exception ex)
                {
                    failed++;
                    System.Diagnostics.Debug.WriteLine(
                        $"[COPY] ошибка для {photo.Id}: {ex.Message}");
                }
            }
        }
        finally
        {
            IsBusy = false;
            StatusText = $"Готово: {ok} скопировано, {failed} ошибок";
        }
    }
    private TopLevel? GetTopLevel()
    {
        if (Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }
    // ----- Hежим отбора ------------------------------------------------
    [ObservableProperty] private bool _isSelectionMode;
    [ObservableProperty] private ShowListItem? _currentShow;
    [ObservableProperty] private int _currentShowPhotoCount;
    private readonly AdminRepository _adminRepo = new();

    [RelayCommand]
    private void ToggleSelectionMode()
    {
        if (CurrentShow is null)
        {
            StatusText = "Сначала выберите шоу в менеджере шоу";
            return;
        }
        IsSelectionMode = !IsSelectionMode;
    }

    [RelayCommand]
    private async Task OpenShowManagerAsync()
    {
        var vm = new ShowManagerViewModel(_contentProvider, _adminRepo, _userId, _roleId);
        var window = new ShowManagerWindow { DataContext = vm };

        // Пока менеджер открыт, главное окно заблокировано.
        await window.ShowDialog(GetMainWindow());

        // После закрытия перечитываем текущее шоу (могли переименовать/удалить).
        await ReloadCurrentShowAsync();
        await RefreshShowMembershipAsync();
    }
    /// <summary>Вызывается из MainWindow.axaml.cs при клике по чекбоксу в режиме отбора.</summary>
    public async Task TogglePhotoInShowAsync(PhotoItemViewModel photo)
    {
        if (CurrentShow is null) return;
        using var repo = new AdminRepository();

        if (photo.IsInCurrentShow)
            await repo.RemovePhotoFromShowAsync(CurrentShow.Id, photo.Id);
        else
            await repo.AddPhotoToShowAsync(CurrentShow.Id, photo.Id);

        photo.IsInCurrentShow = !photo.IsInCurrentShow;
        CurrentShowPhotoCount = await repo.GetShowPhotoCountAsync(CurrentShow.Id);
        StatusText = $"Шоу «{CurrentShow.Name}»: {CurrentShowPhotoCount} фото";
    }

    public async Task SetCurrentShowAsync(ShowListItem? show)
    {
        CurrentShow = show;
        CurrentShowPhotoCount = show is null ? 0 : await _adminRepo.GetShowPhotoCountAsync(show.Id);
        await RefreshShowMembershipAsync();
    }

    private async Task ReloadCurrentShowAsync()
    {
        if (CurrentShow is null) return;
        var fresh = await _adminRepo.GetShowAsync(CurrentShow.Id);
        if (fresh is null)
        {
            CurrentShow = null;
            CurrentShowPhotoCount = 0;
            IsSelectionMode = false;
            StatusText = "Текущее шоу было удалено";
            await RefreshShowMembershipAsync();
            return;
        }
        CurrentShow = new ShowListItem
        {
            Id = fresh.Id,
            Name = fresh.Name,
            Description = fresh.Description,
            AuthorId = fresh.AuthorId,
            CreatedAt = fresh.CreatedAt,
            IntervalSeconds = fresh.IntervalSeconds,
            PhotoCount = fresh.PhotoCount
        };
        CurrentShowPhotoCount = fresh.PhotoCount;
        await RefreshShowMembershipAsync();
    }

    private async Task RefreshShowMembershipAsync()
    {
        var ids = CurrentShow is null
            ? new HashSet<Guid>()
            : await _adminRepo.GetShowPhotoIdsAsync(CurrentShow.Id);

        foreach (var p in Photos)
            p.IsInCurrentShow = ids.Contains(p.Id);
    }

    private Window? GetMainWindow()
    {
        return Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
    }


    // ----- Внутреннее ---------------------------------------------------
    public string PositionInfo
    {
        get
        {
            if (CurrentPhoto is null || Photos.Count == 0) return "";
            var idx = Photos.IndexOf(CurrentPhoto);
            return $"Фото {idx + 1}/{Photos.Count}";
        }
    }

    private async Task LoadPhotosAsync()
    {
        IsBusy = true;
        try
        {
            var filter = BuildFilter();
            // Возвращаемся к GetPageAsync, но запрашиваем «всё сразу».
            var page = await _query.GetPageAsync(filter, 0, int.MaxValue);

            Photos.Clear();
            foreach (var item in page.Photos)
                Photos.Add(new PhotoItemViewModel(item));

            CurrentPhoto = Photos.FirstOrDefault();
            UpdateCurrentImage();

            StatusText = $"Записей: {Photos.Count}";
            OnPropertyChanged(nameof(CanPreviousPhoto));
            OnPropertyChanged(nameof(CanNextPhoto));
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка загрузки: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
    private PhotoFilter BuildFilter()
    {
        return new PhotoFilter
        {
            UploadedFrom = FilterUploadedFrom?.UtcDateTime,
            UploadedTo = FilterUploadedTo?.UtcDateTime,
            TakenFrom = FilterTakenFrom?.UtcDateTime,
            TakenTo = FilterTakenTo?.UtcDateTime,
            AuthorName = string.IsNullOrWhiteSpace(FilterAuthor) ? null : FilterAuthor.Trim(),
            Album = string.IsNullOrWhiteSpace(FilterAlbum) ? null : FilterAlbum.Trim()
        };
    }

    private void UpdateCurrentImage()
    {
        // Пока используем миниатюру как основное изображение.
        // Для полного изображения — скачивание из S3 через PhotoService.
        CurrentImage = CurrentPhoto?.Thumbnail;
    }

    partial void OnCurrentPhotoChanged(PhotoItemViewModel? value)
    {
        foreach (var p in Photos)
            p.IsCurrent = ReferenceEquals(p, value);

        OnPropertyChanged(nameof(CanPreviousPhoto));
        OnPropertyChanged(nameof(CanNextPhoto));
        OnPropertyChanged(nameof(PositionInfo));

        if (value is null)
        {
            FullImage = null;
            return;
        }

        _ = LoadFullImageAsync();
    }
    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrEmpty(clean) ? "photo" : clean;
    }

    private static string GuessExtension(string objectKey)
    {
        var ext = Path.GetExtension(objectKey);
        return string.IsNullOrEmpty(ext) ? ".jpg" : ext;
    }

    private static string MakeUniquePath(string dir, string baseName, string ext)
    {
        var path = Path.Combine(dir, baseName + ext);
        int n = 1;
        while (File.Exists(path))
            path = Path.Combine(dir, $"{baseName}_{n++}{ext}");
        return path;
    }

}
