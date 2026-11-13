using System.IO;
using CornieKit.Looper.Interpolation;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace CornieKit.Looper.Services;

/// <summary>
/// 2x RIFE via ONNX Runtime DirectML. If the model or the GPU provider
/// cannot be loaded, <see cref="IsAvailable"/> is false and playback stays
/// on the original frames.
/// </summary>
public sealed class RifeFrameInterpolator : IDisposable
{
    private readonly object _gate = new();
    private InferenceSession? _session;
    private bool _unavailable;
    private string _img0 = "img0";
    private string _img1 = "img1";
    private string _timestep = "timestep";
    private string _output = "frame";

    public static string DefaultModelPath =>
        Path.Combine(AppContext.BaseDirectory, "Models", "rife_v425lite.onnx");

    public string ModelPath { get; init; } = DefaultModelPath;

    public bool IsAvailable
    {
        get
        {
            lock (_gate)
            {
                EnsureLoaded();
                return _session != null;
            }
        }
    }

    public byte[] Interpolate(byte[] bgra0, byte[] bgra1, int width, int height, float timestep)
    {
        lock (_gate)
        {
            EnsureLoaded();
            if (_session == null)
                throw new InvalidOperationException("RIFE is not available.");

            var padW = InterpolationMath.Pad(width);
            var padH = InterpolationMath.Pad(height);
            var img0 = ToTensor(bgra0, width, height, padW, padH);
            var img1 = ToTensor(bgra1, width, height, padW, padH);
            var stepMeta = _session.InputMetadata[_timestep];
            var stepDims = Normalized(stepMeta.Dimensions);
            var stepData = new float[Math.Max(1, stepDims.Aggregate(1, (a, b) => a * b))];
            Array.Fill(stepData, timestep);
            var step = new DenseTensor<float>(stepData, stepDims);

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(_img0, img0),
                NamedOnnxValue.CreateFromTensor(_img1, img1),
                NamedOnnxValue.CreateFromTensor(_timestep, step)
            };
            using var results = _session.Run(inputs);
            var output = results.First().AsTensor<float>()
                ?? throw new InvalidOperationException("RIFE returned no frame.");
            return FromTensor(output, width, height);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _session?.Dispose();
            _session = null;
        }
    }

    private void EnsureLoaded()
    {
        if (_session != null || _unavailable)
            return;
        if (!File.Exists(ModelPath))
        {
            _unavailable = true;
            return;
        }
        try
        {
            var options = new SessionOptions();
            options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_EXTENDED;
            options.AppendExecutionProvider_DML(0);
            _session = new InferenceSession(ModelPath, options);
            var names = _session.InputMetadata.Keys.ToList();
            if (names.Count >= 3)
            {
                _img0 = names[0];
                _img1 = names[1];
                _timestep = names[2];
            }
            _output = _session.OutputMetadata.Keys.FirstOrDefault() ?? _output;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RIFE] DirectML session was not created: {ex.Message}");
            _session?.Dispose();
            _session = null;
            _unavailable = true;
        }
    }

    private static int[] Normalized(IReadOnlyList<int> dimensions)
    {
        if (dimensions.Count == 0)
            return new[] { 1 };
        var dims = new int[dimensions.Count];
        for (var i = 0; i < dims.Length; i++)
            dims[i] = dimensions[i] <= 0 ? 1 : dimensions[i];
        return dims;
    }

    private static DenseTensor<float> ToTensor(byte[] bgra, int width, int height, int padW, int padH)
    {
        var data = new float[3 * padH * padW];
        var plane = padH * padW;
        for (var y = 0; y < padH; y++)
        {
            var srcY = Math.Min(y, height - 1);
            for (var x = 0; x < padW; x++)
            {
                var srcX = Math.Min(x, width - 1);
                var p = (srcY * width + srcX) * 4;
                var o = y * padW + x;
                data[o] = bgra[p + 2] / 255f;
                data[plane + o] = bgra[p + 1] / 255f;
                data[plane * 2 + o] = bgra[p] / 255f;
            }
        }
        return new DenseTensor<float>(data, new[] { 1, 3, padH, padW });
    }

    private static byte[] FromTensor(Tensor<float> tensor, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var p = (y * width + x) * 4;
                pixels[p] = ToByte(tensor[0, 2, y, x]);
                pixels[p + 1] = ToByte(tensor[0, 1, y, x]);
                pixels[p + 2] = ToByte(tensor[0, 0, y, x]);
                pixels[p + 3] = 255;
            }
        }
        return pixels;
    }

    private static byte ToByte(float value)
    {
        if (value < 0)
            return 0;
        if (value > 1)
            return 255;
        return (byte)Math.Round(value * 255f);
    }
}
