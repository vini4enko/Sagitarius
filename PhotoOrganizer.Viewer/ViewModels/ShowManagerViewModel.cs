using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoOrganizer.Data;
using PhotoOrganizer.Data.Repositories;
using PhotoOrganizer.Storage;
using PhotoOrganizer.Viewer.Views;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace PhotoOrganizer.Viewer.ViewModels;

public partial class ShowManagerViewModel : ObservableObject
{
    private readonly IPhotoContentProvider _contentProvider;
    private readonly AdminRepository _repo;
    private readonly long _userId;
    private readonly long _roleId;

    public ObservableCollection<ShowListItem> Shows { get; } = new();
    public ObservableCollection<ShowPhotoItem> CurrentShowPhotos { get; } = new();

    [ObservableProperty] private ShowListItem? _selectedShow;
    [ObservableProperty] private ShowDetail? _editingShow;
    [ObservableProperty] private ShowPhotoItem? _selectedShowPhoto;
    [ObservableProperty] private string _statusText = "";

    public event Action<ShowListItem?>? ShowSelected;

    public ShowManagerViewModel(
        IPhotoContentProvider provider,
        AdminRepository repo,
        long userId,
        long roleId)
    {
        _contentProvider = provider;
        _repo = repo;
        _userId = userId;
        _roleId = roleId;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        Shows.Clear();
        foreach (var s in await _repo.GetShowsAsync())
            Shows.Add(s);
    }

    partial void OnSelectedShowChanged(ShowListItem? value)
    {
        _ = LoadSelectedShowDetailsAsync(value);
    }

    private async Task LoadSelectedShowDetailsAsync(ShowListItem? item)
    {
        CurrentShowPhotos.Clear();
        if (item is null) { EditingShow = null; return; }

        EditingShow = await _repo.GetShowAsync(item.Id);
        foreach (var p in await _repo.GetShowPhotosOrderedAsync(item.Id))
            CurrentShowPhotos.Add(p);
    }

    [RelayCommand]
    private async Task CreateShowAsync()
    {
        var name = $"Новое шоу {DateTime.Now:yyyy-MM-dd HH-mm-ss}";
        await _repo.CreateShowAsync(name, _userId);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteShowAsync()
    {
        if (SelectedShow is null) return;
        await _repo.DeleteShowAsync(SelectedShow.Id);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task SaveShowAsync()
    {
        if (EditingShow is null) return;
        await _repo.UpdateShowAsync(EditingShow);
        await LoadAsync();
        StatusText = "Сохранено";
    }

    [RelayCommand]
    private async Task MakeCurrentAsync()
    {
        if (SelectedShow is null) return;
        ShowSelected?.Invoke(SelectedShow);
        StatusText = $"Текущее шоу: {SelectedShow.Name}";
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task MoveUpAsync()
    {
        if (SelectedShowPhoto is null || SelectedShow is null) return;
        await _repo.MovePhotoInShowAsync(SelectedShow.Id, SelectedShowPhoto.PhotoId, -1);
        await LoadSelectedShowDetailsAsync(SelectedShow);
    }

    [RelayCommand]
    private async Task MoveDownAsync()
    {
        if (SelectedShowPhoto is null || SelectedShow is null) return;
        await _repo.MovePhotoInShowAsync(SelectedShow.Id, SelectedShowPhoto.PhotoId, +1);
        await LoadSelectedShowDetailsAsync(SelectedShow);
    }

    [RelayCommand]
    private async Task RemovePhotoAsync()
    {
        if (SelectedShowPhoto is null || SelectedShow is null) return;
        await _repo.RemovePhotoFromShowAsync(SelectedShow.Id, SelectedShowPhoto.PhotoId);
        await LoadSelectedShowDetailsAsync(SelectedShow);
    }

    [RelayCommand]
    private async Task PreviewAsync()
    {
        if (SelectedShow is null) return;

        var previewVm = new SlideshowPreviewViewModel(
            _contentProvider, _repo, _userId, _roleId, SelectedShow.Id);

        var window = new SlideshowPreviewWindow { DataContext = previewVm };
        previewVm.CloseRequested += () => window.Close();

        window.Show();
        await previewVm.StartAsync();
    }
}