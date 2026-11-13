using CornieKit.Looper.Interpolation;
using CornieKit.Looper.Services;

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
    public void Playback_clock_advances_forward_and_pause_holds_the_position()
    {
        var clock = new LoopPlaybackClock();
        clock.Restart(TimeSpan.FromSeconds(2));
        var first = clock.PositionInSegment();
        Thread.Sleep(30);
        var second = clock.PositionInSegment();
        Assert.True(second > first);
        clock.SetRunning(false);
        var held = clock.PositionInSegment();
        Thread.Sleep(30);
        Assert.Equal(held, clock.PositionInSegment());
        clock.SetRunning(true);
        Thread.Sleep(20);
        Assert.True(clock.PositionInSegment() > held);
    }

    [Fact]
    public void Playback_clock_wraps_inside_the_segment_without_a_backward_step()
    {
        var duration = TimeSpan.FromMilliseconds(100);
        var previous = TimeSpan.Zero;
        for (var ms = 0; ms < 100; ms += 10)
        {
            var position = LoopPlaybackClock.Wrap(TimeSpan.FromMilliseconds(ms), duration);
            Assert.True(position >= previous);
            previous = position;
        }
        Assert.Equal(TimeSpan.FromMilliseconds(5), LoopPlaybackClock.Wrap(TimeSpan.FromMilliseconds(105), duration));
    }

    [Fact]
    public void Decoded_ring_serves_the_playhead_from_memory_and_reuses_slots()
    {
        var ring = new DecodedFrameRing(3);
        ring.Reset(1, 1);
        ring.Write(0, new byte[] { 1, 0, 0, 0 });
        ring.Write(1, new byte[] { 2, 0, 0, 0 });
        ring.Write(2, new byte[] { 3, 0, 0, 0 });
        Assert.Equal(-1, ring.NextToDecode(0, 6));
        Assert.Equal(-1, ring.NextToDecode(0, 3));

        var destination = new byte[4];
        Assert.True(ring.TryCopy(1, destination));
        Assert.Equal(2, destination[0]);

        ring.Write(3, new byte[] { 4, 0, 0, 0 });
        Assert.False(ring.Contains(0));
        Assert.True(ring.Contains(3));
        Assert.Equal(4, ring.NextToDecode(2, 6));
    }

    [Fact]
    public void Ring_shows_the_newest_ready_frame_when_the_exact_index_is_still_decoding()
    {
        var ring = new DecodedFrameRing(4);
        ring.Reset(1, 1);
        ring.Write(0, new byte[] { 1, 0, 0, 0 });
        ring.Write(1, new byte[] { 2, 0, 0, 0 });
        var destination = new byte[4];
        Assert.True(ring.TryCopyLatest(3, 8, destination));
        Assert.Equal(2, destination[0]);
        Assert.False(ring.TryCopyLatest(6, 8, destination));
    }

    [Fact]
    public void Frame_size_is_not_capped_at_1080p()
    {
        Assert.True(InterpolationMath.IsSupportedSize(2560, 1440));
        Assert.True(InterpolationMath.IsSupportedSize(3840, 2160));
        Assert.True(InterpolationMath.IsSupportedSize(7680, 4320));
        Assert.False(InterpolationMath.IsSupportedSize(1, 2160));
        Assert.False(InterpolationMath.IsSupportedSize(1920, 0));
    }

    [Fact]
    public void Seam_keeps_the_opening_frames_for_the_wrap()
    {
        var seam = new SeamFrames();
        seam.Reset(1, 1);
        Assert.Equal(0, seam.NextMissing(40));
        seam.Write(0, new byte[] { 7, 0, 0, 0 });
        seam.Write(0, new byte[] { 9, 0, 0, 0 });
        var destination = new byte[4];
        Assert.True(seam.TryCopy(0, destination));
        Assert.Equal(7, destination[0]);
        Assert.Equal(1, seam.NextMissing(40));
        Assert.Equal(-1, seam.NextMissing(1));
    }

    [Fact]
    public void Ring_does_not_reload_frames_the_seam_already_holds()
    {
        var ring = new DecodedFrameRing(4);
        ring.Reset(1, 1);
        ring.Write(2, new byte[] { 1, 0, 0, 0 });
        Assert.Equal(3, ring.NextToDecode(0, 10, skipBelow: 2));
        ring.Write(3, new byte[] { 2, 0, 0, 0 });
        Assert.Equal(4, ring.NextToDecode(2, 10, skipBelow: 2));
        Assert.Equal(-1, ring.NextToDecode(2, 4, skipBelow: 2));
    }

    [Fact]
    public void Collector_keeps_frames_when_the_clock_barely_moves()
    {
        var collector = new SegmentFrameCollector();
        collector.Reset(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Assert.False(collector.Add(TimeSpan.FromSeconds(1.000), new byte[] { 1 }, 4, 4));
        Assert.False(collector.Add(TimeSpan.FromSeconds(1.001), new byte[] { 2 }, 4, 4));
        Assert.False(collector.Add(TimeSpan.FromSeconds(1.001), new byte[] { 3 }, 4, 4));
        Assert.Equal(3, collector.Count);
    }

    [Fact]
    public void Jpeg_roundtrip_keeps_color_and_opaque_alpha_and_decodes_into_the_same_buffer()
    {
        const int width = 64;
        const int height = 64;
        var source = new byte[width * height * 4];
        for (var i = 0; i < source.Length; i += 4)
        {
            source[i] = 20;
            source[i + 1] = 180;
            source[i + 2] = 40;
            source[i + 3] = 0;
        }
        var codec = new JpegFrameCodec();
        var encoded = codec.Encode(source, width, height, width * 4);
        var destination = new byte[source.Length];
        codec.DecodeTo(encoded, width, height, destination);
        codec.DecodeTo(encoded, width, height, destination);
        Assert.Equal(255, destination[3]);
        Assert.InRange(destination[0], 10, 35);
        Assert.InRange(destination[1], 165, 195);
        Assert.InRange(destination[2], 25, 55);
    }

    [Fact]
    public void Jpeg_1080p_decode_is_fast_enough_for_sixty_frames_per_second()
    {
        const int width = 1920;
        const int height = 1080;
        var source = new byte[width * height * 4];
        for (var i = 0; i < source.Length; i += 64)
            source[i] = (byte)(i / 64);
        var codec = new JpegFrameCodec();
        var encoded = codec.Encode(source, width, height, width * 4);
        var destination = new byte[source.Length];
        codec.DecodeTo(encoded, width, height, destination);
        var started = System.Diagnostics.Stopwatch.StartNew();
        const int passes = 8;
        for (var i = 0; i < passes; i++)
            codec.DecodeTo(encoded, width, height, destination);
        started.Stop();
        var averageMs = started.Elapsed.TotalMilliseconds / passes;
        Assert.True(averageMs < 16, $"1080p JPEG decode averaged {averageMs:F1} ms, which cannot feed a 60 fps loop.");
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
