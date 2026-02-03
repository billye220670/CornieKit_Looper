using System.IO;
using System.Text.Json;

namespace CornieKit.Looper.Services;

public class RecentFilesService
{
    private const int MaxRecentFiles = 10;
    private readonly string _recentFilesPath;
    private List<string> _recentFiles = new();

    public IReadOnlyList<string> RecentFiles => _recentFiles.AsReadOnly();

    public event EventHandler? RecentFilesChanged;

    public RecentFilesService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CornieKit.Looper");

        Directory.CreateDirectory(appDataPath);
        _recentFilesPath = Path.Combine(appDataPath, "recent_files.json");

        Load();
    }

    public void AddRecentFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return;

        // 移除已存在的相同路径（如果有）
        _recentFiles.Remove(filePath);

        // 插入到开头
        _recentFiles.Insert(0, filePath);

        // 限制数量
        if (_recentFiles.Count > MaxRecentFiles)
        {
            _recentFiles.RemoveRange(MaxRecentFiles, _recentFiles.Count - MaxRecentFiles);
        }

        Save();
        RecentFilesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveRecentFile(string filePath)
    {
        if (_recentFiles.Remove(filePath))
        {
            Save();
            RecentFilesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ClearRecentFiles()
    {
        _recentFiles.Clear();
        Save();
        RecentFilesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_recentFilesPath))
            {
                var json = File.ReadAllText(_recentFilesPath);
                _recentFiles = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();

                // 过滤掉不存在的文件
                _recentFiles = _recentFiles.Where(File.Exists).ToList();
            }
        }
        catch
        {
            _recentFiles = new List<string>();
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_recentFiles, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_recentFilesPath, json);
        }
        catch
        {
            // 忽略保存错误
        }
    }
}
