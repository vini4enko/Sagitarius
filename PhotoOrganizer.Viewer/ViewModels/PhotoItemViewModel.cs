using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using PhotoOrganizer.Data;
using System;
using System.IO;

namespace PhotoOrganizer.Viewer.ViewModels;

public partial class PhotoItemViewModel : ObservableObject
{
    public PhotoListItem Model { get; }

    [ObservableProperty]
    private Bitmap? _thumbnail;
    [ObservableProperty] private bool _isCurrent;   // фото сейчас показывается справа
    [ObservableProperty] private bool _isMarked;    // фото отмечено для групповой операции
    public bool IsCurrentOrMarked => IsCurrent || IsMarked;
    public Guid Id => Model.Id;
    public string? Title => Model.Title;
    public string ObjectKey => Model.ObjectKey;
    public bool IsPublic => Model.IsPublic;
    public DateTime UploadedAt => Model.UploadedAt;
    public DateTime? TakenAt => Model.TakenAt;
    public string? AuthorName => Model.AuthorName;
    public string? Address => Model.Address;
    public double? Lat => Model.Lat;
    public double? Lon => Model.Lon;
    public string AlbumsDisplay => Model.Albums.Count == 0
                                     ? "—"
                                     : string.Join(", ", Model.Albums);

    public PhotoItemViewModel(PhotoListItem model)
    {
        Model = model;
        if (model.ThumbnailData is { Length: > 0 })
        {
            try
            {
                using var ms = new MemoryStream(model.ThumbnailData);
                _thumbnail = new Bitmap(ms);
            }
            catch { /* битый файл — оставляем null */ }
        }
    }
}