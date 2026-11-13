using System.IO;
using SkiaSharp;

namespace CornieKit.Looper.Services;

public interface IFrameCodec
{
    string Id { get; }
    byte[] Encode(ReadOnlySpan<byte> bgra, int width, int height, int stride);
    byte[] Decode(ReadOnlySpan<byte> encoded, int width, int height);
    void DecodeTo(ReadOnlySpan<byte> encoded, int width, int height, byte[] destination);
}

/// <summary>
/// JPEG frames keep the on-disk loop small enough to reopen later.
/// Decode writes straight into the caller's buffer. The WPF JPEG decoder
/// allocates a full frame and takes the same imaging lock as WriteableBitmap,
/// which stalls the UI thread while a high-fps loop is on screen.
/// </summary>
public sealed class JpegFrameCodec : IFrameCodec
{
    public const int Quality = 85;

    public string Id => "jpeg";

    public byte[] Encode(ReadOnlySpan<byte> bgra, int width, int height, int stride)
    {
        var tight = stride == width * 4 ? bgra.ToArray() : Tight(bgra, width, height, stride);
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        unsafe
        {
            fixed (byte* pixels = tight)
            {
                using var pixmap = new SKPixmap(info, (IntPtr)pixels, width * 4);
                using var image = SKImage.FromPixels(pixmap);
                using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, Quality)
                    ?? throw new InvalidDataException("JPEG encode failed.");
                return encoded.ToArray();
            }
        }
    }

    public byte[] Decode(ReadOnlySpan<byte> encoded, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        DecodeTo(encoded, width, height, pixels);
        return pixels;
    }

    public void DecodeTo(ReadOnlySpan<byte> encoded, int width, int height, byte[] destination)
    {
        var bytes = checked(width * height * 4);
        if (destination.Length < bytes)
            throw new ArgumentException("Destination is smaller than the frame.", nameof(destination));
        if (encoded.IsEmpty)
            throw new InvalidDataException("JPEG frame is empty.");

        unsafe
        {
            fixed (byte* source = encoded)
            fixed (byte* target = destination)
            {
                using var data = SKData.Create((IntPtr)source, encoded.Length);
                using var codec = SKCodec.Create(data)
                    ?? throw new InvalidDataException("JPEG frame could not be read.");
                var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
                var result = codec.GetPixels(info, (IntPtr)target);
                if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
                    throw new InvalidDataException("JPEG decode failed: " + result);
            }
        }

        // JPEG has no alpha. An opaque bitmap is required so the picture-adjustment
        // shader does not blend this frame onto the previous one.
        if (destination[3] != 255)
            BgraFrameAlpha.ForceOpaque(destination.AsSpan(0, bytes));
    }

    private static byte[] Tight(ReadOnlySpan<byte> bgra, int width, int height, int stride)
    {
        var tight = new byte[width * height * 4];
        var row = width * 4;
        for (var y = 0; y < height; y++)
            bgra.Slice(y * stride, row).CopyTo(tight.AsSpan(y * row, row));
        return tight;
    }
}
