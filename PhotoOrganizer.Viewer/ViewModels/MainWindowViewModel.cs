using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoOrganizer.Data;
using PhotoOrganizer.Storage;
using PhotoOrganizer.Viewer.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace PhotoOrganizer.Viewer.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly PhotoQueryService _query;
    private readonly SessionService _session;

    private const int PageSize = 20;
    private readonly IPhotoContentProvider _contentProvider;
    private readonly long _userId;    // <<< добавлено
    private readonly long _roleId;    // <<< добавлено

    private  Dictionary<Guid, Bitmap> _fullImageCache = new();

    public MainWindowViewModel(PhotoQueryService query, SessionService session)
    {
        _query = query;
        _session = session;
        _userId = 1;   // admin  <<< захардкожено для теста
        _roleId = 2;   // analyst <<< захардкожено для теста
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
    // ----- Пагинация ----------------------------------------------------
    [ObservableProperty] private int _pageOffset;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _pageNumber;
    [ObservableProperty] private int _pageCount;

    public bool CanPrevPage => PageOffset > 0;
    public bool CanNextPage => PageOffset + PageSize < TotalCount;

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

    // ----- Курсор -------------------------------------------------------
    [ObservableProperty] private bool _isBusy;

    // ----- Команды ------------------------------------------------------

    [RelayCommand]
    private async Task LoadInitialAsync()
    {
        PageOffset = 0;
        await LoadPageAsync();
    }

    [RelayCommand]
    private async Task PrevPageAsync()
    {
        if (!CanPrevPage) return;
        PageOffset = Math.Max(0, PageOffset - PageSize);
        await LoadPageAsync();
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        if (!CanNextPage) return;
        PageOffset += PageSize;
        await LoadPageAsync();
    }

    [RelayCommand]
    private async Task ApplyFilterAsync()
    {
        PageOffset = 0;
        await LoadPageAsync();
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
        PageOffset = 0;
        await LoadPageAsync();
    }

    [RelayCommand]
    private async Task AnonymousModeAsync()
    {
        _session.SetAnonymous();
        ModeText = "Аноним";
        IsRegistered = false;
        PageOffset = 0;
        await LoadPageAsync();
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
            PageOffset = 0;
            await LoadPageAsync();
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
        if (CurrentPhoto is null) return;

        if (_fullImageCache.TryGetValue(CurrentPhoto.Id, out var cached))
        {
            FullImage = cached;
            return;
        }

        IsBusy = true;
        try
        {
            using var stream = await _contentProvider.GetFullImageAsync(
                CurrentPhoto.Id, _userId, _roleId);

            if (stream is null) return;

            var bmp = new Bitmap(stream);
            _fullImageCache[CurrentPhoto.Id] = bmp;
            FullImage = bmp;
        }
        finally { IsBusy = false; }
    }

    // ----- Внутреннее ---------------------------------------------------

    private async Task LoadPageAsync()
    {
        IsBusy = true;
        try
        {
            var filter = BuildFilter();
            var page = await _query.GetPageAsync(filter, PageOffset, PageSize);

            TotalCount = page.TotalCount;
            PageNumber = PageSize == 0 ? 1 : (PageOffset / PageSize) + 1;
            PageCount = (int)Math.Ceiling(TotalCount / (double)PageSize);
            if (PageCount == 0) PageCount = 1;

            Photos.Clear();
            foreach (var item in page.Photos)
                Photos.Add(new PhotoItemViewModel(item));

            CurrentPhoto = Photos.FirstOrDefault();
            UpdateCurrentImage();

            StatusText = $"Записей: {TotalCount}, страница {PageNumber}/{PageCount}";
            OnPropertyChanged(nameof(CanPrevPage));
            OnPropertyChanged(nameof(CanNextPage));
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

    partial void OnCurrentPhotoChanged(PhotoItemViewModel? value)
    {
        UpdateCurrentImage();
    }

    private void UpdateCurrentImage()
    {
        // Пока используем миниатюру как основное изображение.
        // Для полного изображения — скачивание из S3 через PhotoService.
        CurrentImage = CurrentPhoto?.Thumbnail;
    }
}