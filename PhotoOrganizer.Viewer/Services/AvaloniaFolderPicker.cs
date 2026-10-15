using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System.Linq;
using System.Threading.Tasks;
using static PhotoOrganizer.Viewer.ViewModels.MainWindowViewModel;

namespace PhotoOrganizer.Viewer.Services;

public class AvaloniaFolderPicker : IFolderPicker
{
    private readonly TopLevel _topLevel;

    public AvaloniaFolderPicker(TopLevel topLevel)
    {
        _topLevel = topLevel;
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await _topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false
            });

        return folders.FirstOrDefault()?.TryGetLocalPath();
    }
}