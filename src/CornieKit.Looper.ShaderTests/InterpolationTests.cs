using CornieKit.Looper.Interpolation;

namespace CornieKit.Looper.ShaderTests;

public class InterpolationTests
{
    [Fact]
    public void Two_x_sequence_includes_the_wrap_from_last_frame_to_first()
    {
        Assert.Equal(8, InterpolationMath.OutputFrameCount(4));
        Assert.Equal((3, 3, 0f), InterpolationMath.SeamPair(6, 4));
        var seam = InterpolationMath.SeamPair(7, 4);
        Assert.Equal(3, seam.Left);
        Assert.Equal(0, seam.Right);
        Assert.Equal(0.5f, seam.Timestep);
        Assert.Equal((0, 1, 0.5f), InterpolationMath.SeamPair(1, 4));
    }

    [Fact]
    public void Frame_index_stays_inside_the_segment_and_maps_the_whole_duration()
    {
        var start = TimeSpan.FromSeconds(10);
        var end = TimeSpan.FromSeconds(12);
        Assert.Equal(0, InterpolationMath.FrameIndex(start, start, end, 8));
        Assert.Equal(7, InterpolationMath.FrameIndex(end, start, end, 8));
        Assert.Equal(4, InterpolationMath.FrameIndex(TimeSpan.FromSeconds(11), start, end, 8));
    }

    [Fact]
    public void Ready_cache_plays_immediately_and_a_mid_loop_cache_waits_for_the_wrap()
    {
        Assert.True(InterpolationMath.PresentImmediately(cacheReadyAtStart: true));
        Assert.False(InterpolationMath.PresentImmediately(cacheReadyAtStart: false));
        Assert.True(InterpolationMath.HoldUntilLoopBoundary(becameReadyMidLoop: true));
    }

    [Fact]
    public void Collector_finishes_a_pass_that_covers_the_segment()
    {
        var collector = new SegmentFrameCollector();
        var start = TimeSpan.FromSeconds(1);
        var end = TimeSpan.FromSeconds(1.5);
        collector.Reset(start, end);
        Assert.False(collector.Add(TimeSpan.FromSeconds(1.02), new byte[] { 1 }, 4, 4));
        Assert.False(collector.Add(TimeSpan.FromSeconds(1.04), new byte[] { 1 }, 4, 4));
        Assert.True(collector.Add(TimeSpan.FromSeconds(1.48), new byte[] { 2 }, 4, 4));
        Assert.Equal(3, collector.Snapshot().Length);
    }

    [Fact]
    public void Cache_roundtrip_requires_the_same_file_bounds_and_codec()
    {
        var root = Path.Combine(Path.GetTempPath(), "cornie-interp-" + Guid.NewGuid().ToString("N"));
        var video = Path.Combine(root, "clip.mp4");
        Directory.CreateDirectory(root);
        File.WriteAllBytes(video, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });
        var store = new InterpolationCacheStore(Path.Combine(root, "cache"));
        var hash = VideoFingerprint.Compute(video);
        var start = TimeSpan.FromSeconds(1);
        var end = TimeSpan.FromSeconds(2);
        var frames = new byte[][] { new byte[] { 9, 9 }, new byte[] { 8, 8 }, new byte[] { 7 } };
        store.Write(new InterpolationCacheManifest
        {
            VideoPath = video,
            VideoHash = hash,
            StartTicks = start.Ticks,
            EndTicks = end.Ticks,
            Width = 4,
            Height = 4,
            SourceFps = 30,
            Codec = "raw"
        }, frames);

        Assert.True(store.TryOpen(video, hash, start, end, "raw", out var reader));
        Assert.NotNull(reader);
        Assert.Equal(frames[1], reader!.Read(1));
        reader.Dispose();

        Assert.False(store.TryOpen(video, hash, start, end.Add(TimeSpan.FromMilliseconds(1)), "raw", out _));
        Assert.False(store.TryOpen(video, "deadbeef", start, end, "raw", out _));
        Assert.False(store.TryOpen(video, hash, start, end, "jpeg", out _));
    }

    [Fact]
    public void Clear_invalid_cache_removes_only_missing_videos()
    {
        var root = Path.Combine(Path.GetTempPath(), "cornie-interp-" + Guid.NewGuid().ToString("N"));
        var kept = Path.Combine(root, "kept.mp4");
        var gone = Path.Combine(root, "gone.mp4");
        Directory.CreateDirectory(root);
        File.WriteAllBytes(kept, new byte[] { 1, 2, 3, 4 });
        File.WriteAllBytes(gone, new byte[] { 5, 6, 7, 8 });
        var store = new InterpolationCacheStore(Path.Combine(root, "cache"));
        Write(store, kept, VideoFingerprint.Compute(kept));
        Write(store, gone, VideoFingerprint.Compute(gone));
        File.Delete(gone);

        var removed = store.DeleteCachesForMissingVideos();

        Assert.Equal(1, removed);
        Assert.Single(Directory.GetFiles(store.Root, "manifest.json", SearchOption.AllDirectories));
        Assert.True(store.TryOpen(kept, VideoFingerprint.Compute(kept), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "raw", out var reader));
        reader!.Dispose();
        Assert.False(store.TryOpen(gone, "ignored", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "raw", out _));
    }

    [Fact]
    public void Fingerprint_changes_when_the_file_bytes_change()
    {
        var path = Path.Combine(Path.GetTempPath(), "cornie-fp-" + Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5 });
        var first = VideoFingerprint.Compute(path);
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 6 });
        var second = VideoFingerprint.Compute(path);
        Assert.NotEqual(first, second);
        Assert.Equal(second, VideoFingerprint.Compute(path));
        File.Delete(path);
    }

    private static void Write(InterpolationCacheStore store, string video, string hash)
    {
        store.Write(new InterpolationCacheManifest
        {
            VideoPath = video,
            VideoHash = hash,
            StartTicks = TimeSpan.FromSeconds(1).Ticks,
            EndTicks = TimeSpan.FromSeconds(2).Ticks,
            Width = 2,
            Height = 2,
            Codec = "raw"
        }, new byte[][] { new byte[] { 1 }, new byte[] { 2 } });
    }
}
