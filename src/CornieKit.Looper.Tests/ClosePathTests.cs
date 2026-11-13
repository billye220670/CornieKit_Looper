using CornieKit.Looper.Models;
using CornieKit.Looper.Services;
using CornieKit.Looper.ViewModels;

namespace CornieKit.Looper.Tests;

public class ClosePathTests
{
    [Fact]
    public void PlayerAbandonAndDispose_WhenPlaybackWasNeverStarted_ReturnsImmediately()
    {
        // Close used to call MediaPlayer.Stop/Dispose on the UI thread. That join
        // waits for decoder threads and deadlocks if one of them is inside
        // Dispatcher.Invoke. Shutdown must only drop managed callbacks.
        var player = new VideoPlayerService();
        var watch = System.Diagnostics.Stopwatch.StartNew();

        player.Abandon();
        player.Abandon();
        player.Dispose();
        player.Dispose();

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task SaveMetadata_ReplacesExistingFileAndLeavesNoTemp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cornie-close-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var videoPath = Path.Combine(dir, "clip.mp4");
            await File.WriteAllBytesAsync(videoPath, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

            var service = new DataPersistenceService();
            var metadataPath = service.GetMetadataFilePath(videoPath);
            await File.WriteAllTextAsync(metadataPath, "{\"previous\":true}");

            await service.SaveMetadataAsync(new VideoMetadata
            {
                VideoFilePath = videoPath,
                Segments = new List<LoopSegment>
                {
                    new()
                    {
                        Name = "Loop 1",
                        StartTime = TimeSpan.FromSeconds(1),
                        EndTime = TimeSpan.FromSeconds(2)
                    }
                }
            });

            Assert.True(File.Exists(metadataPath));
            Assert.False(File.Exists(metadataPath + ".tmp"));

            var json = await File.ReadAllTextAsync(metadataPath);
            Assert.Contains("Loop 1", json);
            Assert.DoesNotContain("\"previous\":true", json);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void EachVideoOpen_StartsMuted_AndUnmuteRestoresTheLevel()
    {
        var viewModel = new MainViewModel(
            new VideoPlayerService(),
            new SegmentManager(),
            new DataPersistenceService(),
            new RecentFilesService(),
            new AppSettingsService());

        viewModel.SetVolume(35);
        viewModel.MuteForNewVideo();
        Assert.Equal(0, viewModel.CurrentVolume);

        viewModel.ToggleMute();
        Assert.Equal(35, viewModel.CurrentVolume);

        viewModel.SetVolume(60);
        viewModel.MuteForNewVideo();
        Assert.Equal(0, viewModel.CurrentVolume);

        viewModel.ToggleMute();
        Assert.Equal(60, viewModel.CurrentVolume);
    }
}
