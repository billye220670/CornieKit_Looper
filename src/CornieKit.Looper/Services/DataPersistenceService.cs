using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using CornieKit.Looper.Models;

namespace CornieKit.Looper.Services;

public class DataPersistenceService
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public string GetMetadataFilePath(string videoFilePath)
    {
        return videoFilePath + ".cornieloop";
    }

    public async Task<VideoMetadata?> LoadMetadataAsync(string videoFilePath)
    {
        var metadataPath = GetMetadataFilePath(videoFilePath);

        if (!File.Exists(metadataPath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(metadataPath);
            var metadata = JsonSerializer.Deserialize<VideoMetadata>(json, _jsonOptions);

            if (metadata != null && ValidateVideoFile(videoFilePath, metadata.VideoFileHash))
            {
                return metadata;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load metadata: {ex.Message}");
        }

        return null;
    }

    public async Task SaveMetadataAsync(VideoMetadata metadata)
    {
        await _saveLock.WaitAsync();
        var metadataPath = GetMetadataFilePath(metadata.VideoFilePath);
        var tempPath = metadataPath + ".tmp";

        try
        {
            metadata.LastModified = DateTime.Now;
            metadata.VideoFileHash = ComputeFileHash(metadata.VideoFilePath);

            var json = JsonSerializer.Serialize(metadata, _jsonOptions);

            // Write a temp file and replace. Quit kills the process without waiting
            // for in-flight saves; a direct write torn by that kill would corrupt
            // the .cornieloop file. The previous file stays intact until replace.
            await File.WriteAllTextAsync(tempPath, json);
            File.Move(tempPath, metadataPath, overwrite: true);
        }
        catch (Exception ex)
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // Best-effort. A leftover .tmp is ignored on the next save.
            }

            Console.WriteLine($"Failed to save metadata: {ex.Message}");
            throw;
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public async Task DeleteMetadataAsync(string videoFilePath)
    {
        try
        {
            var metadataPath = GetMetadataFilePath(videoFilePath);
            if (File.Exists(metadataPath))
            {
                await Task.Run(() => File.Delete(metadataPath));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to delete metadata: {ex.Message}");
        }
    }

    private string ComputeFileHash(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            using var sha256 = SHA256.Create();

            var buffer = new byte[8192];
            var bytesRead = stream.Read(buffer, 0, buffer.Length);

            var hash = sha256.ComputeHash(buffer, 0, bytesRead);
            return Convert.ToHexString(hash)[..16];
        }
        catch
        {
            return string.Empty;
        }
    }

    private bool ValidateVideoFile(string filePath, string expectedHash)
    {
        if (!File.Exists(filePath))
            return false;

        var actualHash = ComputeFileHash(filePath);
        return actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase);
    }
}
