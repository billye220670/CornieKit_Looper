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
    /// 上次退出时是否处于静音状态。
    /// </summary>
    public bool IsMuted { get; set; } = false;
}
