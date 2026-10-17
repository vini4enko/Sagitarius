using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Input;
using Microsoft.Extensions.Configuration;
using PhotoOrganizer.Viewer.Services;
using PhotoOrganizer.Viewer.ViewModels;
using PhotoOrganizer.Viewer.Views;
using System;

namespace PhotoOrganizer.Viewer;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("d:\\Sagitarius\\appsettings.Shared.json", optional: false)
            .Build();

        var session = new SessionService();
        var query = new PhotoQueryService(session);
        var vm = new MainWindowViewModel(query, session);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow { DataContext = vm };
        }

        base.OnFrameworkInitializationCompleted();
    }
    //private void OnZoomableImageDoubleTapped(object? sender, TappedEventArgs e)
    //{
    //    if (DataContext is MainWindowViewModel vm)
    //    {
    //        vm.LoadFullImageCommand.Execute(null);
    //    }
    //}
}