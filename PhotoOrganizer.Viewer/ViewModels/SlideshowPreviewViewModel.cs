using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoOrganizer.Data;
using PhotoOrganizer.Data.Repositories;
using PhotoOrganizer.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PhotoOrganizer.Viewer.ViewModels;

public partial class SlideshowPreviewViewModel : ObservableObject
{
    private readonly IPhotoContentProvider _contentProvider;
    private readonly AdminRepository _repo;
    private readonly long _userId, _roleId, _showId;
    private readonly DispatcherTimer _timer = new();

    private List<ShowPhotoItem> _photos = new();
    private int _index;

    [ObservableProperty] private Bitmap? _currentImage;
    [ObservableProperty] private string? _currentCaption;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isPaused;

    public event Action? CloseRequested;

    public SlideshowPreviewViewModel(
        IPhotoContentProvider provider, AdminRepository repo,
        long userId, long roleId, long showId)
    {
        _contentProvider = provider;
        _repo = repo;
        _userId = userId;
        _roleId = roleId;
        _showId = showId;
        _timer.Tick += async (_, _) => await AdvanceAsync();
    }

    public async Task StartAsync()
    {
        var show = await _repo.GetShowAsync(_showId);
        if (show is null) return;

        _photos = await _repo.GetShowPhotosOrderedAsync(_showId);
        if (show.RandomOrder)
            _photos = _photos.OrderBy(_ => Random.Shared.Next()).ToList();

        _timer.Interval = TimeSpan.FromSeconds(show.IntervalSeconds);

        _index = 0;
        await ShowCurrentAsync();
        _timer.Start();
    }

    private async Task ShowCurrentAsync()
    {
        if (_photos.Count == 0) return;
        var item = _photos[_index];

        using var stream = await _contentProvider.GetFullImageAsync(
            item.PhotoId, _userId, _roleId);
        if (stream is null) return;

        CurrentImage = new Bitmap(stream);
        CurrentCaption = item.Title ?? "";
        StatusText = $"{_index + 1}/{_photos.Count}";
    }

    private async Task AdvanceAsync()
    {
        if (IsPaused) return;
        _index = (_index + 1) % _photos.Count;
        await ShowCurrentAsync();
    }

    [RelayCommand] private async Task NextAsync() { await AdvanceAsync(); }
    [RelayCommand] private async Task PreviousAsync() { _index = (_index - 1 + _photos.Count) % _photos.Count; await ShowCurrentAsync(); }
    [RelayCommand] private void TogglePause() { IsPaused = !IsPaused; }
    [RelayCommand] private void Close() { _timer.Stop(); CloseRequested?.Invoke(); }
}