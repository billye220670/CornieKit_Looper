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

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var mainWindow = _serviceProvider?.GetRequiredService<MainWindow>();
        mainWindow?.Show();

        // Handle command line arguments (file path from "Open with" or default program)
        if (e.Args.Length > 0)
        {
            var filePath = e.Args[0];
            if (System.IO.File.Exists(filePath))
            {
                var viewModel = _serviceProvider?.GetRequiredService<MainViewModel>();
                if (viewModel != null)
                {
                    // Wait for window to fully load before loading video
                    await System.Threading.Tasks.Task.Delay(200);
                    await viewModel.LoadVideoAsync(filePath);
                }
            }
        }
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

