using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PhotoOrganizer.Viewer.Controls;
using PhotoOrganizer.Viewer.ViewModels;

namespace PhotoOrganizer.Viewer.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnThumbnailClicked(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { DataContext: PhotoItemViewModel item }
            && DataContext is MainWindowViewModel vm)
        {
            vm.CurrentPhoto = item;
        }
    }

    private void OnZoomableImageDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.LoadFullImageCommand.Execute(null);
    }
}