using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;

namespace CornieKit.Looper.Interpolation;

/// <summary>
/// Identity of a source file that does not read the whole video.
/// Size plus samples from the head, middle, and tail.
/// </summary>
public static class VideoFingerprint
{
    public static string Compute(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        Span<byte> length = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(length, stream.Length);
        sha.TransformBlock(length.ToArray(), 0, 8, null, 0);
        Add(stream, sha, 0, 1024 * 1024);
        if (stream.Length > 1024 * 1024)
        {
            var middle = Math.Max(0, stream.Length / 2 - 32 * 1024);
            Add(stream, sha, middle, 64 * 1024);
            var tail = Math.Max(0, stream.Length - 64 * 1024);
            Add(stream, sha, tail, 64 * 1024);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!);
    }

    private static void Add(Stream stream, HashAlgorithm sha, long offset, int count)
    {
        if (offset >= stream.Length || count <= 0)
            return;
        stream.Position = offset;
        var buffer = new byte[8192];
        var left = (int)Math.Min(count, stream.Length - offset);
        while (left > 0)
        {
            var read = stream.Read(buffer, 0, Math.Min(buffer.Length, left));
            if (read == 0)
                break;
            sha.TransformBlock(buffer, 0, read, null, 0);
            left -= read;
        }
    }
}
