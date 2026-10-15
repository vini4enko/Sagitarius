using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PhotoOrganizer.Viewer.Controls;
using PhotoOrganizer.Viewer.Services;
using PhotoOrganizer.Viewer.ViewModels;
using System;
using System.IO;

namespace PhotoOrganizer.Viewer.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        CleanupTempFolder();
        InitializeComponent();
        if (DataContext is MainWindowViewModel vm)
        {
            vm.FilePicker = new AvaloniaFolderPicker(this);
        }
        this.AttachDevTools();
    }

    private void OnThumbnailClicked(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { DataContext: PhotoItemViewModel item }
            && DataContext is MainWindowViewModel vm)
        {
            vm.CurrentPhoto = item;
        }
    }
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is MainWindowViewModel vm)
        {
            vm.FilePicker = new AvaloniaFolderPicker(this);

            KeyDown += (_, args) =>
            {
                // Не перехватываем стрелки внутри текстовых полей.
                if (args.Source is TextBox) return;

                switch (args.Key)
                {
                    case Key.Left:
                    case Key.PageUp:
                        vm.PreviousPhotoCommand.Execute(null);
                        args.Handled = true;
                        break;

                    case Key.Right:
                    case Key.PageDown:
                        vm.NextPhotoCommand.Execute(null);
                        args.Handled = true;
                        break;

                    case Key.Home:
                        vm.FirstPhotoCommand.Execute(null);
                        args.Handled = true;
                        break;

                    case Key.End:
                        vm.LastPhotoCommand.Execute(null);
                        args.Handled = true;
                        break;
                }
            };
        }
    }
    // Вызвать один раз при инициализации приложения.
    static void CleanupTempFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PhotoOrganizer");
        if (!Directory.Exists(dir)) return;

        foreach (var file in Directory.EnumerateFiles(dir))
        {
            try { File.Delete(file); } catch { /* пропустить занятые */ }
        }
    }
}