using System.IO;

namespace CornieKit.Looper.ViewModels;

public class RecentFileItem
{
    public string FilePath { get; }
    public string FileName { get; }

    public RecentFileItem(string filePath)
    {
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);
    }
}
