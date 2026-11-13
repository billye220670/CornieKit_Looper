using System.IO;
using System.Text.Json;

namespace CornieKit.Looper.Interpolation;

public sealed class InterpolationCacheManifest
{
    public string VideoPath { get; set; } = string.Empty;
    public string VideoHash { get; set; } = string.Empty;
    public long StartTicks { get; set; }
    public long EndTicks { get; set; }
    public string ModelId { get; set; } = InterpolationMath.ModelId;
    public int Scale { get; set; } = InterpolationMath.Scale;
    public int Width { get; set; }
    public int Height { get; set; }
    public int FrameCount { get; set; }
    public double SourceFps { get; set; }
    public string Codec { get; set; } = "raw";
}

public sealed class InterpolationCacheReader : IDisposable
{
    private readonly FileStream _stream;
    private readonly long[] _offsets;
    private readonly int[] _lengths;
    private readonly InterpolationCacheManifest _manifest;

    internal InterpolationCacheReader(FileStream stream, long[] offsets, int[] lengths, InterpolationCacheManifest manifest)
    {
        _stream = stream;
        _offsets = offsets;
        _lengths = lengths;
        _manifest = manifest;
    }

    public InterpolationCacheManifest Manifest => _manifest;

    public byte[] Read(int index)
    {
        var bytes = new byte[LengthOf(index)];
        ReadTo(index, bytes);
        return bytes;
    }

    public int EncodedLength(int index) => LengthOf(index);

    public long EncodedBytes
    {
        get
        {
            long total = 0;
            foreach (var length in _lengths)
                total += length;
            return total;
        }
    }

    /// <summary>
    /// Copies every encoded frame into memory when the cache is small enough
    /// that playback can stop touching the disk.
    /// </summary>
    public byte[][]? CopyAllIfAtMost(long maxBytes)
    {
        if (EncodedBytes > maxBytes || _lengths.Length == 0)
            return null;
        var frames = new byte[_lengths.Length][];
        for (var i = 0; i < frames.Length; i++)
            frames[i] = Read(i);
        return frames;
    }

    public int ReadTo(int index, byte[] buffer)
    {
        var length = LengthOf(index);
        if (buffer.Length < length)
            throw new ArgumentException("Buffer is smaller than the encoded frame.", nameof(buffer));
        lock (_stream)
        {
            _stream.Position = _offsets[index];
            _stream.ReadExactly(buffer, 0, length);
        }
        return length;
    }

    private int LengthOf(int index)
    {
        if ((uint)index >= (uint)_lengths.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        return _lengths[index];
    }

    public void Dispose() => _stream.Dispose();
}

/// <summary>
/// Disk cache for one interpolated loop. A folder is valid only when its
/// manifest matches the video fingerprint and the segment bounds.
/// </summary>
public sealed class InterpolationCacheStore
{
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public InterpolationCacheStore(string root)
    {
        Root = root;
    }

    public string Root { get; }

    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CornieKit.Looper", "interpolation");

    public string DirectoryFor(string videoHash, TimeSpan start, TimeSpan end) =>
        Path.Combine(Root, videoHash, $"{start.Ticks}_{end.Ticks}_{InterpolationMath.ModelId}_x{InterpolationMath.Scale}");

    public bool TryOpen(string videoPath, string videoHash, TimeSpan start, TimeSpan end, string codec, out InterpolationCacheReader? reader)
    {
        reader = null;
        var dir = DirectoryFor(videoHash, start, end);
        var manifestPath = Path.Combine(dir, "manifest.json");
        var framesPath = Path.Combine(dir, "frames.bin");
        if (!File.Exists(manifestPath) || !File.Exists(framesPath))
            return false;

        InterpolationCacheManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<InterpolationCacheManifest>(File.ReadAllText(manifestPath), _json);
        }
        catch
        {
            return false;
        }
        if (manifest == null || !Matches(manifest, videoPath, videoHash, start, end, codec))
            return false;

        try
        {
            var stream = new FileStream(framesPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
            if (!TryReadIndex(stream, manifest.FrameCount, out var offsets, out var lengths))
            {
                stream.Dispose();
                return false;
            }
            reader = new InterpolationCacheReader(stream, offsets, lengths, manifest);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Write(InterpolationCacheManifest manifest, IReadOnlyList<byte[]> frames)
    {
        if (frames.Count < 2)
            throw new InvalidOperationException("An interpolated loop needs at least two frames.");
        var dir = DirectoryFor(manifest.VideoHash, TimeSpan.FromTicks(manifest.StartTicks), TimeSpan.FromTicks(manifest.EndTicks));
        Directory.CreateDirectory(dir);
        var framesPath = Path.Combine(dir, "frames.bin");
        var tempFrames = framesPath + ".tmp";
        using (var stream = new FileStream(tempFrames, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            Span<byte> header = stackalloc byte[8];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header, 0x46494B43); // CKIF
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header[4..], frames.Count);
            stream.Write(header);
            Span<byte> len = stackalloc byte[4];
            foreach (var frame in frames)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(len, frame.Length);
                stream.Write(len);
                stream.Write(frame);
            }
        }
        File.Move(tempFrames, framesPath, overwrite: true);
        manifest.FrameCount = frames.Count;
        var manifestPath = Path.Combine(dir, "manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, _json));
    }

    /// <summary>
    /// Deletes caches whose source path is missing. Caches for files that
    /// are still on disk are left alone, even if that video is not open.
    /// </summary>
    public int DeleteCachesForMissingVideos()
    {
        if (!Directory.Exists(Root))
            return 0;
        var manifests = Directory.GetFiles(Root, "manifest.json", SearchOption.AllDirectories);
        var removed = 0;
        foreach (var manifestPath in manifests)
        {
            InterpolationCacheManifest? manifest = null;
            try
            {
                manifest = JsonSerializer.Deserialize<InterpolationCacheManifest>(File.ReadAllText(manifestPath), _json);
            }
            catch
            {
                // Unreadable cache cannot be matched to a video. Remove it.
            }
            var path = manifest?.VideoPath;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                continue;
            var dir = Path.GetDirectoryName(manifestPath);
            if (string.IsNullOrEmpty(dir))
                continue;
            try
            {
                Directory.Delete(dir, recursive: true);
                removed++;
            }
            catch
            {
                // Another play may still have the file open. Skip it.
            }
        }
        return removed;
    }

    private static bool Matches(InterpolationCacheManifest manifest, string videoPath, string videoHash, TimeSpan start, TimeSpan end, string codec) =>
        string.Equals(manifest.VideoHash, videoHash, StringComparison.OrdinalIgnoreCase)
        && string.Equals(manifest.VideoPath, videoPath, StringComparison.OrdinalIgnoreCase)
        && manifest.StartTicks == start.Ticks
        && manifest.EndTicks == end.Ticks
        && manifest.Scale == InterpolationMath.Scale
        && string.Equals(manifest.ModelId, InterpolationMath.ModelId, StringComparison.Ordinal)
        && string.Equals(manifest.Codec, codec, StringComparison.Ordinal)
        && manifest.FrameCount >= 2
        && manifest.Width >= 2
        && manifest.Height >= 2;

    private static bool TryReadIndex(FileStream stream, int frameCount, out long[] offsets, out int[] lengths)
    {
        offsets = Array.Empty<long>();
        lengths = Array.Empty<int>();
        Span<byte> header = stackalloc byte[8];
        if (stream.Read(header) != 8)
            return false;
        var magic = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(header);
        var count = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
        if (magic != 0x46494B43 || count != frameCount || count < 2)
            return false;
        offsets = new long[count];
        lengths = new int[count];
        Span<byte> lenBuf = stackalloc byte[4];
        for (var i = 0; i < count; i++)
        {
            if (stream.Read(lenBuf) != 4)
                return false;
            var len = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(lenBuf);
            if (len <= 0)
                return false;
            offsets[i] = stream.Position;
            lengths[i] = len;
            stream.Position += len;
        }
        return true;
    }
}
