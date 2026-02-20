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
        mainWindow?.Show();  // 立即显示窗口，不被 LibVLC 阻塞

        // 在后台初始化 LibVLC（冷启动时耗时较长，热启动约2秒）
        var viewModel = _serviceProvider?.GetRequiredService<MainViewModel>();
        if (viewModel != null)
        {
            await viewModel.InitializeAsync();

            // Handle command line arguments (file path from "Open with" or default program)
            if (e.Args.Length > 0)
            {
                var filePath = e.Args[0];
                if (System.IO.File.Exists(filePath))
                {
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

