namespace CornieKit.Looper.Models;

/// <summary>
/// 全局应用设置（跨视频、跨会话持久化到 AppData）
/// </summary>
public class AppSettings
{
    /// <summary>
    /// 上次非静音状态下的音量（0-100）。用于解除静音时恢复到之前的音量。
    /// </summary>
    public int LastVolume { get; set; } = 100;

    /// <summary>
    /// Last mute toggle the user made. Opening a video always starts muted and
    /// does not consult this flag.
    /// </summary>
    public bool IsMuted { get; set; } = false;
}
