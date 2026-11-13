namespace CornieKit.Looper.Interpolation;

/// <summary>
/// Sits between LibVLC's display callback and the WriteableBitmap.
/// Capture runs only while a cache is being built. Presentation replaces
/// the source frames once a cache is allowed on screen.
/// </summary>
public interface IInterpolatedFrameBridge
{
    bool WantsCapture { get; }
    bool IsPresenting { get; }
    int PresentedFrameIndex { get; }
    void NotePlaybackRunning(bool running);
    void CaptureFrame(ReadOnlySpan<byte> pixels, int width, int height, int stride, TimeSpan mediaTime);
    bool TryCopyPresentedFrame(Span<byte> destination, out int width, out int height);
}
