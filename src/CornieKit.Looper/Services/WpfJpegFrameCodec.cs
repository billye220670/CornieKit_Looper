using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CornieKit.Looper.Services;

public interface IFrameCodec
{
    string Id { get; }
    byte[] Encode(ReadOnlySpan<byte> bgra, int width, int height, int stride);
    byte[] Decode(ReadOnlySpan<byte> encoded, int width, int height);
}

/// <summary>
/// JPEG frames keep the on-disk loop small enough to reopen later.
/// BitmapSource.Create does not need the UI thread.
/// </summary>
public sealed class WpfJpegFrameCodec : IFrameCodec
{
    public string Id => "jpeg";

    public byte[] Encode(ReadOnlySpan<byte> bgra, int width, int height, int stride)
    {
        var tight = stride == width * 4 ? bgra.ToArray() : Tight(bgra, width, height, stride);
        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, tight, width * 4);
        var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    public byte[] Decode(ReadOnlySpan<byte> encoded, int width, int height)
    {
        using var stream = new MemoryStream(encoded.ToArray(), writable: false);
        var decoder = new JpegBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        var pixels = new byte[width * height * 4];
        converted.CopyPixels(pixels, width * 4, 0);
        return pixels;
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
