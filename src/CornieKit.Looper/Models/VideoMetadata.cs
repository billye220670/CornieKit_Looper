namespace CornieKit.Looper.Models;

public class VideoMetadata
{
    public string VideoFilePath { get; set; } = string.Empty;
    public string VideoFileHash { get; set; } = string.Empty;
    public List<LoopSegment> Segments { get; set; } = new();
    public LoopMode DefaultLoopMode { get; set; } = LoopMode.Single;
    public DateTime LastModified { get; set; } = DateTime.Now;
    public TimeSpan LastPlaybackPosition { get; set; } = TimeSpan.Zero;

    // 播放状态
    public bool WasPlayingSegments { get; set; } = false;
    public bool WasPlayingSingleSegment { get; set; } = false;
    public Guid? LastPlayingSegmentId { get; set; } = null;
}
