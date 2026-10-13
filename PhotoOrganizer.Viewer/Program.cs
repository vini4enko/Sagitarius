using Avalonia;
using System;

namespace PhotoOrganizer.Viewer;

internal static class Program
{
    // Инициализация не должна использовать никакие API Avalonia до вызова
    // AppMain — это стандартное требование Avalonia.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Используется также дизайнером Avalonia Previewer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}