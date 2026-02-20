using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace CornieKit.Looper.Effects;

public class VideoAdjustEffect : ShaderEffect
{
    private static readonly PixelShader _shader = CreateShader();

    private static PixelShader CreateShader()
    {
        var shader = new PixelShader();
        var dir = Path.GetDirectoryName(Assembly.GetEntryAssembly()!.Location)!;
        var path = Path.Combine(dir, "Effects", "VideoAdjust.ps");
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Pixel shader not found at: {path}\nBuild the project once to compile VideoAdjust.fx → VideoAdjust.ps.");
        shader.UriSource = new Uri(path, UriKind.Absolute);
        return shader;
    }

    // s0 — video texture
    public static readonly DependencyProperty InputProperty =
        RegisterPixelShaderSamplerProperty("Input", typeof(VideoAdjustEffect), 0);

    // c0 — Temperature  [-1, +1]
    public static readonly DependencyProperty TemperatureProperty =
        DependencyProperty.Register(nameof(Temperature), typeof(double), typeof(VideoAdjustEffect),
            new UIPropertyMetadata(0.0, PixelShaderConstantCallback(0)));

    // c1 — Saturation  [0, 2]
    public static readonly DependencyProperty SaturationProperty =
        DependencyProperty.Register(nameof(Saturation), typeof(double), typeof(VideoAdjustEffect),
            new UIPropertyMetadata(1.0, PixelShaderConstantCallback(1)));

    // c2 — Sharpness  [0, 2]
    public static readonly DependencyProperty SharpnessProperty =
        DependencyProperty.Register(nameof(Sharpness), typeof(double), typeof(VideoAdjustEffect),
            new UIPropertyMetadata(0.0, PixelShaderConstantCallback(2)));

    // c3 — Contrast  [0.5, 2]
    public static readonly DependencyProperty ContrastProperty =
        DependencyProperty.Register(nameof(Contrast), typeof(double), typeof(VideoAdjustEffect),
            new UIPropertyMetadata(1.0, PixelShaderConstantCallback(3)));

    // c4, c5 — reserved: WPF injects texel size via DdxUvDdyUvRegisterIndex = 4

    // c6 — Brightness  [-0.5, +0.5]
    public static readonly DependencyProperty BrightnessProperty =
        DependencyProperty.Register(nameof(Brightness), typeof(double), typeof(VideoAdjustEffect),
            new UIPropertyMetadata(0.0, PixelShaderConstantCallback(6)));

    // c7 — Vignette  [0, 1]
    public static readonly DependencyProperty VignetteProperty =
        DependencyProperty.Register(nameof(Vignette), typeof(double), typeof(VideoAdjustEffect),
            new UIPropertyMetadata(0.0, PixelShaderConstantCallback(7)));

    // c8 — SkinTone  [-1, +1]
    public static readonly DependencyProperty SkinToneProperty =
        DependencyProperty.Register(nameof(SkinTone), typeof(double), typeof(VideoAdjustEffect),
            new UIPropertyMetadata(0.0, PixelShaderConstantCallback(8)));

    public VideoAdjustEffect()
    {
        PixelShader = _shader;
        DdxUvDdyUvRegisterIndex = 4; // WPF injects 1/texWidth into c4.x, 1/texHeight into c5.y
        UpdateShaderValue(InputProperty);
        UpdateShaderValue(TemperatureProperty);
        UpdateShaderValue(SaturationProperty);
        UpdateShaderValue(SharpnessProperty);
        UpdateShaderValue(ContrastProperty);
        UpdateShaderValue(BrightnessProperty);
        UpdateShaderValue(VignetteProperty);
        UpdateShaderValue(SkinToneProperty);
    }

    [System.ComponentModel.Browsable(false)]
    public Brush Input       { get => (Brush)GetValue(InputProperty);       set => SetValue(InputProperty, value); }
    public double Temperature { get => (double)GetValue(TemperatureProperty); set => SetValue(TemperatureProperty, value); }
    public double Saturation  { get => (double)GetValue(SaturationProperty);  set => SetValue(SaturationProperty, value); }
    public double Sharpness   { get => (double)GetValue(SharpnessProperty);   set => SetValue(SharpnessProperty, value); }
    public double Contrast    { get => (double)GetValue(ContrastProperty);    set => SetValue(ContrastProperty, value); }
    public double Brightness  { get => (double)GetValue(BrightnessProperty);  set => SetValue(BrightnessProperty, value); }
    public double Vignette    { get => (double)GetValue(VignetteProperty);    set => SetValue(VignetteProperty, value); }
    public double SkinTone    { get => (double)GetValue(SkinToneProperty);    set => SetValue(SkinToneProperty, value); }
}
