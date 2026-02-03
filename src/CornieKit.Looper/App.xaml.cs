using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using CornieKit.Looper.Services;
using CornieKit.Looper.ViewModels;

namespace CornieKit.Looper;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    public App()
    {
        var services = new ServiceCollection();

        services.AddSingleton<VideoPlayerService>();
        services.AddSingleton<SegmentManager>();
        services.AddSingleton<DataPersistenceService>();
        services.AddSingleton<RecentFilesService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var mainWindow = _serviceProvider?.GetRequiredService<MainWindow>();
        mainWindow?.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }

        base.OnExit(e);
    }
}

