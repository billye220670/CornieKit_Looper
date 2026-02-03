namespace CornieKit.Looper.Models;

public class VideoMetadata
{
    public string VideoFilePath { get; set; } = string.Empty;
    public string VideoFileHash { get; set; } = string.Empty;
    public List<LoopSegment> Segments { get; set; } = new();
    public LoopMode DefaultLoopMode { get; set; } = LoopMode.Single;
    public DateTime LastModified { get; set; } = DateTime.Now;
}
