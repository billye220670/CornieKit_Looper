using CornieKit.Looper.Services;

namespace CornieKit.Looper.ShaderTests;

public class VideoAdjustGhostingTests
{
    private const int Width = 32;
    private const int Height = 32;

    [Fact]
    public void Kernel_is_centered_and_keeps_cross_axis_derivatives()
    {
        var offsets = VideoAdjustReference.CardinalOffsets(new VideoAdjustReference.Derivatives(0.10f, 0.02f, -0.03f, 0.20f));

        Assert.Equal(0f, offsets.Right.U + offsets.Left.U, 6);
        Assert.Equal(0f, offsets.Right.V + offsets.Left.V, 6);
        Assert.Equal(0f, offsets.Down.U + offsets.Up.U, 6);
        Assert.Equal(0f, offsets.Down.V + offsets.Up.V, 6);

        // The old kernel used (c4.x, 0) and (0, c5.y), dropping ddx(v) and ddy(u).
        Assert.Equal(0.02f, offsets.Right.V, 6);
        Assert.Equal(-0.03f, offsets.Down.U, 6);
    }

    [Fact]
    public void Neutral_adjustments_keep_rgb_and_force_opaque_alpha()
    {
        var source = new VideoAdjustReference.Pixel(0.25f, 0.50f, 0.75f, 0f);
        var calls = 0;
        var result = VideoAdjustReference.Apply(0.4f, 0.6f, new VideoAdjustReference.Params(), (u, v) =>
        {
            calls++;
            Assert.Equal(0.4f, u, 6);
            Assert.Equal(0.6f, v, 6);
            return source;
        });

        Assert.Equal(1, calls);
        Assert.Equal(source.R, result.R, 6);
        Assert.Equal(source.G, result.G, 6);
        Assert.Equal(source.B, result.B, 6);
        Assert.Equal(1f, result.A, 6);
    }

    [Fact]
    public void Point_adjustments_do_not_sample_any_other_pixel()
    {
        var seen = new List<(float U, float V)>();
        var result = VideoAdjustReference.Apply(
            0.3f,
            0.7f,
            new VideoAdjustReference.Params
            {
                Temperature = 0.8f,
                Saturation = 1.4f,
                Contrast = 1.2f,
                Brightness = 0.05f,
                Vignette = 0.4f,
                SkinTone = 0.6f,
            },
            (u, v) =>
            {
                seen.Add((u, v));
                return new VideoAdjustReference.Pixel(0.55f, 0.35f, 0.25f, 0f);
            });

        Assert.Single(seen);
        Assert.Equal(0.3f, seen[0].U, 6);
        Assert.Equal(0.7f, seen[0].V, 6);
        Assert.Equal(1f, result.A, 6);
        // Temperature shifts R up and B down. It must not invent a second spatial sample.
        Assert.True(result.R > 0.55f);
        Assert.True(result.B < 0.25f);
    }

    [Fact]
    public void Temperature_matches_the_shader_channel_bias()
    {
        var result = VideoAdjustReference.Apply(
            0.5f,
            0.5f,
            new VideoAdjustReference.Params { Temperature = 1f },
            (_, _) => new VideoAdjustReference.Pixel(0.40f, 0.40f, 0.40f, 0f));

        Assert.Equal(0.55f, result.R, 5);
        Assert.Equal(0.45f, result.G, 5);
        Assert.Equal(0.25f, result.B, 5);
    }

    [Fact]
    public void Sharpness_zero_does_not_mix_neighbors_even_if_a_stale_offset_is_large()
    {
        var calls = 0;
        var result = VideoAdjustReference.Apply(
            PixelCenter(10),
            PixelCenter(10),
            new VideoAdjustReference.Params
            {
                Sharpness = 0f,
                Delta = new VideoAdjustReference.Derivatives(0.5f, 0.25f, -0.4f, 0.5f),
            },
            (u, v) =>
            {
                calls++;
                return new VideoAdjustReference.Pixel(0.2f, 0.3f, 0.4f, 0f);
            });

        Assert.Equal(1, calls);
        Assert.Equal(0.2f, result.R, 6);
        Assert.Equal(0.3f, result.G, 6);
        Assert.Equal(0.4f, result.B, 6);
    }

    [Fact]
    public void Flat_area_stays_flat_at_full_sharpness()
    {
        var result = VideoAdjustReference.Apply(
            PixelCenter(8),
            PixelCenter(8),
            new VideoAdjustReference.Params
            {
                Sharpness = 2f,
                Delta = OnePixel(),
            },
            (_, _) => new VideoAdjustReference.Pixel(0.45f, 0.45f, 0.45f, 0f));

        Assert.Equal(0.45f, result.R, 5);
        Assert.Equal(0.45f, result.G, 5);
        Assert.Equal(0.45f, result.B, 5);
    }

    [Fact]
    public void Fixed_sharpen_does_not_pull_a_distant_feature_into_the_frame()
    {
        // A single red pixel at (8, 4). Row 16 is twelve pixels away — outside a 1px kernel.
        var image = NewImage();
        Set(image, 8, 4, 1f, 0f, 0f);

        var far = VideoAdjustReference.Apply(
            PixelCenter(8),
            PixelCenter(16),
            new VideoAdjustReference.Params { Sharpness = 2f, Delta = OnePixel() },
            (u, v) => Read(image, u, v));

        Assert.Equal(0.2f, far.R, 5);
        Assert.Equal(0.2f, far.G, 5);
        Assert.Equal(0.2f, far.B, 5);
    }

    [Fact]
    public void Legacy_c5_offset_ghosts_a_distant_feature_into_the_unsharp_result()
    {
        // Bright field with one dark pixel. The old vertical tap reads c5.y, which
        // WPF does not set. A stale 12px offset reaches that pixel and the unsharp
        // formula paints an inverted copy of it here. A 1px kernel cannot.
        var image = NewImage(0.8f);
        Set(image, 8, 4, 0f, 0f, 0f);

        var staleTy = 12f / Height;
        var ghosted = VideoAdjustReference.ApplyLegacyUnsharp(
            PixelCenter(8),
            PixelCenter(16),
            sharpness: 2f,
            c4x: 1f / Width,
            c5y: staleTy,
            (u, v) => Read(image, u, v));
        var fixedKernel = VideoAdjustReference.Apply(
            PixelCenter(8),
            PixelCenter(16),
            new VideoAdjustReference.Params { Sharpness = 2f, Delta = OnePixel() },
            (u, v) => Read(image, u, v));

        Assert.True(ghosted.R > 0.9f, $"legacy kernel should leave a bright ghost, got R={ghosted.R}");
        Assert.Equal(0.8f, fixedKernel.R, 5);
    }

    [Fact]
    public void Sharpen_samples_only_the_center_and_four_immediate_neighbors()
    {
        var seen = new List<(int X, int Y)>();
        VideoAdjustReference.Apply(
            PixelCenter(10),
            PixelCenter(12),
            new VideoAdjustReference.Params { Sharpness = 1f, Delta = OnePixel() },
            (u, v) =>
            {
                var x = (int)Math.Floor(u * Width);
                var y = (int)Math.Floor(v * Height);
                seen.Add((x, y));
                return new VideoAdjustReference.Pixel(0.2f, 0.2f, 0.2f, 0f);
            });

        Assert.Equal(
            new[] { (10, 12), (10, 13), (10, 11), (11, 12), (9, 12) },
            seen);
    }

    [Fact]
    public void Neighbor_halos_are_symmetric_so_the_feature_does_not_walk()
    {
        var image = NewImage();
        Set(image, 10, 10, 0.9f, 0.9f, 0.9f);
        var p = new VideoAdjustReference.Params { Sharpness = 1.5f, Delta = OnePixel() };

        var above = VideoAdjustReference.Apply(PixelCenter(10), PixelCenter(9), p, (u, v) => Read(image, u, v));
        var below = VideoAdjustReference.Apply(PixelCenter(10), PixelCenter(11), p, (u, v) => Read(image, u, v));
        var left = VideoAdjustReference.Apply(PixelCenter(9), PixelCenter(10), p, (u, v) => Read(image, u, v));
        var right = VideoAdjustReference.Apply(PixelCenter(11), PixelCenter(10), p, (u, v) => Read(image, u, v));

        Assert.Equal(above.R, below.R, 5);
        Assert.Equal(left.R, right.R, 5);
        Assert.Equal(above.R, left.R, 5);
    }

    [Fact]
    public void ForceOpaque_sets_only_the_alpha_byte()
    {
        var bgra = new byte[] { 1, 2, 3, 0, 4, 5, 6, 9, 7 };
        BgraFrameAlpha.ForceOpaque(bgra);

        Assert.Equal(new byte[] { 1, 2, 3, 255, 4, 5, 6, 255, 7 }, bgra);
    }

    [Fact]
    public void Shader_source_uses_one_derivative_register_and_opaque_output()
    {
        var fx = File.ReadAllText(RepoFile("src/CornieKit.Looper/Effects/VideoAdjust.fx"));
        var effect = File.ReadAllText(RepoFile("src/CornieKit.Looper/Effects/VideoAdjustEffect.cs"));

        Assert.Contains("float4 TexelDelta  : register(c4);", fx);
        Assert.Contains("float2 right = TexelDelta.xy;", fx);
        Assert.Contains("float2 down  = TexelDelta.zw;", fx);
        Assert.Contains("tex2D(input, uv + down)", fx);
        Assert.Contains("tex2D(input, uv - down)", fx);
        Assert.Contains("tex2D(input, uv + right)", fx);
        Assert.Contains("tex2D(input, uv - right)", fx);
        Assert.Contains("color.a = 1;", fx);
        Assert.DoesNotContain("color.a = orig.a", fx);
        Assert.DoesNotContain("register(c5)", fx);
        Assert.DoesNotContain("DdyUV", fx);
        Assert.Equal(5, Count(fx, "tex2D("));

        Assert.Contains("DdxUvDdyUvRegisterIndex = 4;", effect);
        Assert.DoesNotContain("PixelShaderConstantCallback(4)", effect);
        Assert.DoesNotContain("PixelShaderConstantCallback(5)", effect);
    }

    [Fact]
    public void Compiled_shader_does_not_sample_with_an_unbound_c5()
    {
        // VideoAdjust.ps is what the process loads. WPF writes only c4. A read of
        // c5 that the shader itself does not `def` is the stale vertical offset.
        // fxc may later park a literal in c5; that is fine, because a def owns it.
        // A Windows build recompiles the .fx over this blob and must keep the same rule.
        var data = File.ReadAllBytes(RepoFile("src/CornieKit.Looper/Effects/VideoAdjust.ps"));
        var info = InspectConstantRegister(data, 5);
        Assert.True(info.Reads == 0 || info.Defined, "c5 is read but never defined; that offset is not the texel step WPF provides");
        Assert.True(InspectConstantRegister(data, 4).Reads > 0);
    }

    private static VideoAdjustReference.Derivatives OnePixel() =>
        new(1f / Width, 0f, 0f, 1f / Height);

    private static float PixelCenter(int index) => (index + 0.5f) / Width;

    private static float[] NewImage(float gray = 0.2f)
    {
        var image = new float[Width * Height * 4];
        for (var i = 0; i < image.Length; i += 4)
        {
            image[i] = gray;
            image[i + 1] = gray;
            image[i + 2] = gray;
            image[i + 3] = 0f;
        }
        return image;
    }

    private static void Set(float[] image, int x, int y, float r, float g, float b)
    {
        var i = (y * Width + x) * 4;
        image[i] = r;
        image[i + 1] = g;
        image[i + 2] = b;
    }

    private static VideoAdjustReference.Pixel Read(float[] image, float u, float v)
    {
        var x = Math.Clamp((int)Math.Floor(u * Width), 0, Width - 1);
        var y = Math.Clamp((int)Math.Floor(v * Height), 0, Height - 1);
        var i = (y * Width + x) * 4;
        return new VideoAdjustReference.Pixel(image[i], image[i + 1], image[i + 2], image[i + 3]);
    }

    private readonly record struct ConstantRegisterInfo(int Reads, bool Defined);

    private static ConstantRegisterInfo InspectConstantRegister(byte[] data, int register)
    {
        var reads = 0;
        var defined = false;
        var pos = 4;
        while (pos + 4 <= data.Length)
        {
            var tok = BitConverter.ToUInt32(data, pos);
            var op = tok & 0xFFFF;
            if (op == 0xFFFF)
                break;
            if (op == 0xFFFE)
            {
                pos += 4 * (1 + (int)(tok >> 16));
                continue;
            }

            var instLen = (int)((tok >> 24) & 0xF);
            if (op == 81)
            {
                // def dest, four floats. The dest token names the constant register.
                if (IsConstantRegister(ReadDword(data, pos + 4), register))
                    defined = true;
            }
            else
            {
                for (var i = 0; i < instLen; i++)
                {
                    if (IsConstantRegister(ReadDword(data, pos + 4 * (1 + i)), register))
                        reads++;
                }
            }

            pos += 4 * (1 + instLen);
        }

        return new ConstantRegisterInfo(reads, defined);
    }

    private static uint ReadDword(byte[] data, int pos) =>
        pos >= 0 && pos + 4 <= data.Length ? BitConverter.ToUInt32(data, pos) : 0;

    private static bool IsConstantRegister(uint param, int register)
    {
        if ((param & 0x80000000) == 0)
            return false;
        var regNum = (int)(param & 0x7FF);
        var type = (int)(((param & 0x70000000) >> 28) | ((param & 0x00001800) >> 8));
        return type == 2 && regNum == register;
    }

    private static int Count(string text, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }
        return count;
    }

    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(relative);
    }
}
