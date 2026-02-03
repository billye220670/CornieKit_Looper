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
        try
        {
            metadata.LastModified = DateTime.Now;
            metadata.VideoFileHash = ComputeFileHash(metadata.VideoFilePath);

            var metadataPath = GetMetadataFilePath(metadata.VideoFilePath);
            var json = JsonSerializer.Serialize(metadata, _jsonOptions);
            await File.WriteAllTextAsync(metadataPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save metadata: {ex.Message}");
            throw;
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
