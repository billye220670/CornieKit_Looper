using System.IO;
using System.Text.Json;
using CornieKit.Looper.Models;

namespace CornieKit.Looper.Services;

/// <summary>
/// 应用级设置持久化服务（如音量、静音等跨视频的全局设置）。
/// 存储路径：%LocalAppData%\CornieKit.Looper\app_settings.json
/// </summary>
public class AppSettingsService
{
    private readonly string _settingsFilePath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private AppSettings _settings = new();
    private readonly object _saveLock = new();

    public AppSettings Settings => _settings;

    public AppSettingsService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CornieKit.Looper");

        Directory.CreateDirectory(appDataPath);
        _settingsFilePath = Path.Combine(appDataPath, "app_settings.json");

        Load();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
                if (loaded != null)
                {
                    _settings = loaded;
                    // 防御性钳制
                    _settings.LastVolume = Math.Clamp(_settings.LastVolume, 0, 100);
                    if (_settings.LastVolume == 0)
                    {
                        // LastVolume 应始终保存非静音时的音量；若持久化文件被手动改成 0，纠正为默认
                        _settings.LastVolume = 100;
                    }
                }
            }
        }
        catch
        {
            _settings = new AppSettings();
        }
    }

    /// <summary>
    /// 同步保存（fire-and-forget 场景使用）。内部加锁避免并发写冲突。
    /// </summary>
    public void Save()
    {
        try
        {
            lock (_saveLock)
            {
                var json = JsonSerializer.Serialize(_settings, _jsonOptions);
                File.WriteAllText(_settingsFilePath, json);
            }
        }
        catch
        {
            // 静默失败：设置持久化不是关键功能，不应阻塞 UI
        }
    }

    /// <summary>
    /// 更新音量与静音状态并立即保存。
    /// </summary>
    /// <param name="lastNonZeroVolume">上次非静音时的音量（0-100）</param>
    /// <param name="isMuted">是否静音</param>
    public void UpdateVolumeState(int lastNonZeroVolume, bool isMuted)
    {
        _settings.LastVolume = Math.Clamp(lastNonZeroVolume <= 0 ? 100 : lastNonZeroVolume, 1, 100);
        _settings.IsMuted = isMuted;
        Save();
    }
}
