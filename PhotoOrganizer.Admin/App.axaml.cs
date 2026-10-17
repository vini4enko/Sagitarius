using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using PhotoOrganizer.Admin.Services;
using PhotoOrganizer.Admin.ViewModels;
using PhotoOrganizer.Admin.Views;

namespace PhotoOrganizer.Admin;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
        var services = new ServiceCollection();
        services.AddSingleton<AdminRepository>();
        services.AddSingleton<S3StorageService>(...);
        services.AddSingleton<IntegrityService>();
        services.AddTransient<UsersViewModel>();
        services.AddTransient<AuditViewModel>();
        services.AddTransient<IntegrityViewModel>();
        services.AddTransient<ExtraChecksViewModel>();
        services.AddTransient<MainWindowViewModel>();
        var provider = services.BuildServiceProvider();

        var mainVm = provider.GetRequiredService<MainWindowViewModel>();
        desktop.MainWindow = new MainWindow { DataContext = mainVm };
    }
}