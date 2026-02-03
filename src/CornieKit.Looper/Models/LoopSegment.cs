namespace CornieKit.Looper.Models;

public class LoopSegment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public int Order { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public TimeSpan Duration => EndTime - StartTime;
}
