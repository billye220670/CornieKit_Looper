namespace CornieKit.Looper.ShaderTests;

/// <summary>
/// CPU specification of Effects/VideoAdjust.fx. The GPU shader is what ships;
/// these tests run the same sampling rules and color math without WPF or LibVLC.
/// </summary>
public static class VideoAdjustReference
{
    public readonly record struct Pixel(float R, float G, float B, float A);

    public readonly record struct Uv(float U, float V);

    /// <summary>
    /// The single float4 WPF writes at DdxUvDdyUvRegisterIndex.
    /// x = ddx(u), y = ddx(v), z = ddy(u), w = ddy(v).
    /// </summary>
    public readonly record struct Derivatives(float DdxU, float DdxV, float DdyU, float DdyV);

    public sealed class Params
    {
        public float Temperature { get; init; }
        public float Saturation { get; init; } = 1f;
        public float Sharpness { get; init; }
        public float Contrast { get; init; } = 1f;
        public float Brightness { get; init; }
        public float Vignette { get; init; }
        public float SkinTone { get; init; }
        public Derivatives Delta { get; init; }
    }

    public static (Uv Right, Uv Left, Uv Down, Uv Up) CardinalOffsets(Derivatives d) =>
    (
        new Uv(d.DdxU, d.DdxV),
        new Uv(-d.DdxU, -d.DdxV),
        new Uv(d.DdyU, d.DdyV),
        new Uv(-d.DdyU, -d.DdyV)
    );

    public static Pixel Apply(float u, float v, Params p, Func<float, float, Pixel> sample)
    {
        var orig = sample(u, v);
        var color = orig;

        if (p.Sharpness > 0.001f)
        {
            var (right, left, down, up) = CardinalOffsets(p.Delta);
            var blur = Average(
                sample(u + down.U, v + down.V),
                sample(u + up.U, v + up.V),
                sample(u + right.U, v + right.V),
                sample(u + left.U, v + left.V));
            color = new Pixel(
                Sat(color.R + (color.R - blur.R) * p.Sharpness),
                Sat(color.G + (color.G - blur.G) * p.Sharpness),
                Sat(color.B + (color.B - blur.B) * p.Sharpness),
                color.A);
        }

        if (Math.Abs(p.Brightness) > 0.001f)
            color = MapRgb(color, c => Sat(c + p.Brightness));

        if (Math.Abs(p.Contrast - 1f) > 0.001f)
            color = MapRgb(color, c => Sat((c - 0.5f) * p.Contrast + 0.5f));

        if (Math.Abs(p.Temperature) > 0.001f)
        {
            color = color with
            {
                R = Sat(color.R + p.Temperature * 0.15f),
                G = Sat(color.G + p.Temperature * 0.05f),
                B = Sat(color.B - p.Temperature * 0.15f),
            };
        }

        if (Math.Abs(p.Saturation - 1f) > 0.001f)
        {
            var luma = Dot709(color.R, color.G, color.B);
            color = new Pixel(
                Sat(luma + (color.R - luma) * p.Saturation),
                Sat(luma + (color.G - luma) * p.Saturation),
                Sat(luma + (color.B - luma) * p.Saturation),
                color.A);
        }

        if (Math.Abs(p.SkinTone) > 0.001f)
            color = ApplySkin(orig, color, p.SkinTone);

        if (p.Vignette > 0.001f)
        {
            var centeredU = u - 0.5f;
            var centeredV = v - 0.5f;
            var dist2 = centeredU * centeredU + centeredV * centeredV;
            var vigFactor = 1f - p.Vignette * Smoothstep(0.08f, 0.50f, dist2);
            color = MapRgb(color, c => c * vigFactor);
        }

        return color with { A = 1f };
    }

    /// <summary>
    /// The previous kernel: horizontal step from c4.x only, vertical step from
    /// c5.y. WPF never writes c5, so the vertical tap was a stale offset.
    /// </summary>
    public static Pixel ApplyLegacyUnsharp(float u, float v, float sharpness, float c4x, float c5y, Func<float, float, Pixel> sample)
    {
        var center = sample(u, v);
        if (sharpness <= 0.001f)
            return center with { A = 1f };

        var blur = Average(
            sample(u, v + c5y),
            sample(u, v - c5y),
            sample(u + c4x, v),
            sample(u - c4x, v));
        return new Pixel(
            Sat(center.R + (center.R - blur.R) * sharpness),
            Sat(center.G + (center.G - blur.G) * sharpness),
            Sat(center.B + (center.B - blur.B) * sharpness),
            1f);
    }

    private static Pixel ApplySkin(Pixel orig, Pixel color, float skinTone)
    {
        var total = orig.R + orig.G + orig.B + 1e-5f;
        var rn = orig.R / total;
        var gn = orig.G / total;
        var bn = orig.B / total;
        var skinMask =
            Smoothstep(0.32f, 0.40f, rn) * (1f - Smoothstep(0.58f, 0.66f, rn)) *
            Smoothstep(0.18f, 0.26f, gn) * (1f - Smoothstep(0.42f, 0.50f, gn)) *
            Smoothstep(0.07f, 0.14f, bn) * (1f - Smoothstep(0.26f, 0.34f, bn));
        var luma = Dot709(orig.R, orig.G, orig.B);
        skinMask *= Smoothstep(0.08f, 0.20f, luma) * (1f - Smoothstep(0.80f, 0.95f, luma));
        var adj = skinTone * skinMask;
        return color with
        {
            R = Sat(color.R + adj * 0.10f),
            G = Sat(color.G + adj * 0.03f),
            B = Sat(color.B - adj * 0.08f),
        };
    }

    private static float Dot709(float r, float g, float b) => r * 0.2126f + g * 0.7152f + b * 0.0722f;

    private static float Smoothstep(float edge0, float edge1, float x)
    {
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static float Sat(float c) => Math.Clamp(c, 0f, 1f);

    private static Pixel MapRgb(Pixel p, Func<float, float> map) =>
        new(map(p.R), map(p.G), map(p.B), p.A);

    private static Pixel Average(Pixel a, Pixel b, Pixel c, Pixel d) =>
        new(
            (a.R + b.R + c.R + d.R) * 0.25f,
            (a.G + b.G + c.G + d.G) * 0.25f,
            (a.B + b.B + c.B + d.B) * 0.25f,
            (a.A + b.A + c.A + d.A) * 0.25f);
}
