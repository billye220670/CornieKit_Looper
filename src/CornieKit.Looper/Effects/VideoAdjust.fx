// VideoAdjust.fx — ps_3_0  (requires DirectX 9c GPU, 2003+, standard on Win10/11)
//
// Register map:
//   c0  Temperature  x: [-1, +1]   negative=cool, positive=warm
//   c1  Saturation   x: [ 0,  2]   1.0 = neutral
//   c2  Sharpness    x: [ 0,  2]   0.0 = off
//   c3  Contrast     x: [0.5, 2]   1.0 = neutral
//   c4  (DdxUV)      WPF-injected: x = 1/textureWidth
//   c5  (DdyUV)      WPF-injected: y = 1/textureHeight
//   c6  Brightness   x: [-0.5, +0.5]  0.0 = neutral
//   c7  Vignette     x: [ 0,   1]     0.0 = off
//   c8  SkinTone     x: [-1,  +1]     0.0 = off

sampler2D input : register(s0);

float4 Temperature : register(c0);
float4 Saturation  : register(c1);
float4 Sharpness   : register(c2);
float4 Contrast    : register(c3);
float4 DdxUV       : register(c4);  // (1/w, 0, 0, 0) from WPF DdxUvDdyUvRegisterIndex
float4 DdyUV       : register(c5);  // (0, 1/h, 0, 0)
float4 Brightness  : register(c6);
float4 Vignette    : register(c7);
float4 SkinTone    : register(c8);

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 orig  = tex2D(input, uv);
    float4 color = orig;

    // ---- Sharpness: unsharp mask (4-tap cardinal) ----
    float sharp = Sharpness.x;
    if (sharp > 0.001)
    {
        float tx = DdxUV.x;
        float ty = DdyUV.y;
        float4 blur = (tex2D(input, uv + float2(0,  ty)) +
                       tex2D(input, uv + float2(0, -ty)) +
                       tex2D(input, uv + float2( tx, 0)) +
                       tex2D(input, uv + float2(-tx, 0))) * 0.25;
        color.rgb = saturate(color.rgb + (color.rgb - blur.rgb) * sharp);
    }

    // ---- Brightness ----
    float bri = Brightness.x;
    if (abs(bri) > 0.001)
        color.rgb = saturate(color.rgb + bri);

    // ---- Contrast (pivot at 0.5) ----
    float con = Contrast.x;
    if (abs(con - 1.0) > 0.001)
        color.rgb = saturate((color.rgb - 0.5) * con + 0.5);

    // ---- Temperature: shift R and B in opposite directions ----
    float temp = Temperature.x;
    if (abs(temp) > 0.001)
    {
        color.r = saturate(color.r + temp * 0.15);
        color.g = saturate(color.g + temp * 0.05);
        color.b = saturate(color.b - temp * 0.15);
    }

    // ---- Saturation: Rec.709 luma interpolation ----
    float sat = Saturation.x;
    if (abs(sat - 1.0) > 0.001)
    {
        float luma = dot(color.rgb, float3(0.2126, 0.7152, 0.0722));
        color.rgb = saturate(luma + (color.rgb - luma) * sat);
    }

    // ---- Skin tone correction ----
    // Detection runs on the ORIGINAL pixel (before color effects) to be stable.
    // Algorithm: Peer-Phillips normalized-RGB chrominance range,
    // then apply selective R/G/B channel shift weighted by the skin mask.
    //   positive SkinTone → warmer/rosier  (boost R, slight G, reduce B)
    //   negative SkinTone → cooler/paler   (reduce R, slight G, boost B)
    float st = SkinTone.x;
    if (abs(st) > 0.001)
    {
        float totalRGB = orig.r + orig.g + orig.b + 1e-5;
        float rn = orig.r / totalRGB;
        float gn = orig.g / totalRGB;
        float bn = orig.b / totalRGB;

        // Smooth membership in each chrominance band
        float skinMask =
            smoothstep(0.32, 0.40, rn) * (1.0 - smoothstep(0.58, 0.66, rn)) *
            smoothstep(0.18, 0.26, gn) * (1.0 - smoothstep(0.42, 0.50, gn)) *
            smoothstep(0.07, 0.14, bn) * (1.0 - smoothstep(0.26, 0.34, bn));

        // Fade out on near-black and near-white pixels
        float origLuma = dot(orig.rgb, float3(0.2126, 0.7152, 0.0722));
        skinMask *= smoothstep(0.08, 0.20, origLuma)
                  * (1.0 - smoothstep(0.80, 0.95, origLuma));

        float adj = st * skinMask;
        color.r = saturate(color.r + adj * 0.10);
        color.g = saturate(color.g + adj * 0.03);
        color.b = saturate(color.b - adj * 0.08);
    }

    // ---- Vignette: smooth radial darken toward edges ----
    float vig = Vignette.x;
    if (vig > 0.001)
    {
        float2 centered = uv - 0.5;
        float dist2 = dot(centered, centered);  // 0 at center, ~0.5 at corners
        float vigFactor = 1.0 - vig * smoothstep(0.08, 0.50, dist2);
        color.rgb *= vigFactor;
    }

    color.a = orig.a;
    return color;
}
