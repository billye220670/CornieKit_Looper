namespace CornieKit.Looper.Services;

/// <summary>
/// LibVLC's RV32 converter stores B, G, R and writes 0 into the alpha byte.
/// A WPF Bgra32 bitmap with alpha 0 is fully transparent. The picture-adjustment
/// shader used to copy that alpha, and WPF then blended the effect with
/// premultiplied SrcOver, which adds the RGB onto the destination.
/// </summary>
public static class BgraFrameAlpha
{
    public static void ForceOpaque(byte[] bgra)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        ForceOpaque(bgra.AsSpan());
    }

    public static void ForceOpaque(Span<byte> bgra)
    {
        for (var i = 3; i < bgra.Length; i += 4)
            bgra[i] = 255;
    }
}
