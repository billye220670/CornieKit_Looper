namespace CornieKit.Looper.Models;

public class VideoMetadata
{
    public string VideoFilePath { get; set; } = string.Empty;
    public string VideoFileHash { get; set; } = string.Empty;
    public List<LoopSegment> Segments { get; set; } = new();
    public LoopMode DefaultLoopMode { get; set; } = LoopMode.Single;
    public DateTime LastModified { get; set; } = DateTime.Now;
    public TimeSpan LastPlaybackPosition { get; set; } = TimeSpan.Zero;

    // 缩放/平移状态
    public double ZoomLevel { get; set; } = 1.0;
    public double ViewCenterX { get; set; } = 0.5;
    public double ViewCenterY { get; set; } = 0.5;

    // 滤镜状态
    public double ColorTemperature { get; set; } = 0.0;
    public double ColorSaturation { get; set; } = 1.0;
    public double ColorSharpness { get; set; } = 0.0;
    public double ColorContrast { get; set; } = 1.0;
    public double ColorBrightness { get; set; } = 0.0;
    public double ColorVignette { get; set; } = 0.0;
    public double ColorSkinTone { get; set; } = 0.0;

    // 播放状态
    public bool WasPlayingSegments { get; set; } = false;
    public bool WasPlayingSingleSegment { get; set; } = false;
    public Guid? LastPlayingSegmentId { get; set; } = null;
}
