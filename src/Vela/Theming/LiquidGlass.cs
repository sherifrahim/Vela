using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;
using Vela.Core;

namespace Vela.Theming;

/// <summary>
/// Real Liquid Glass: a GPU pixel shader that bends what is behind the dock like a thick lens. The flat middle stays
/// clear (with a soft blur and a colour lift); towards the rounded edge the image is pulled inwards and stretched,
/// red and blue split slightly (dispersion), and a light-catching rim runs along the edge facing the light.
///
/// The HLSL below is compiled once at startup with Windows' own d3dcompiler_47.dll (no tools or packages) into a
/// ps_3_0 shader that WPF runs on the GPU. The input is the wallpaper slice under the bar (WallpaperGlass.Slice), so
/// the glass redraws only when the dock moves or the wallpaper changes: zero idle cost.
/// </summary>
public static class LiquidGlass
{
    const string Hlsl = """
        sampler2D input : register(s0);
        float4 geo  : register(c0);  // x,y: size in px   z: corner radius px   w: rim (lens) width px
        float4 lens : register(c1);  // x: refraction px  y: blur px  z: saturation  w: dispersion px
        float4 lite : register(c2);  // x,y: light direction  z: specular  w: edge glow
        float4 tint : register(c3);  // rgb: tint colour  a: tint amount

        float sdBox(float2 p, float2 b, float r)
        {
            float2 q = abs(p) - b + r;
            return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
        }

        float3 tap(float2 uv) { return tex2D(input, uv).rgb; }

        float4 main(float2 uv : TEXCOORD) : COLOR
        {
            float2 size = geo.xy;
            float2 hs = size * 0.5;
            float2 p = uv * size - hs;
            float d = sdBox(p, hs, geo.z);                                  // < 0 inside the shape

            // Outward surface normal from the distance field.
            float2 n = float2(sdBox(p + float2(1, 0), hs, geo.z) - sdBox(p - float2(1, 0), hs, geo.z),
                              sdBox(p + float2(0, 1), hs, geo.z) - sdBox(p - float2(0, 1), hs, geo.z));
            n = n / max(length(n), 0.0001);

            // Lens profile: 0 across the flat middle, rising smoothly to 1 at the very edge.
            float t = saturate(1.0 + d / geo.w);
            float bend = t * t * (3.0 - 2.0 * t);
            bend = bend * bend;

            float2 px = 1.0 / size;
            float2 c = uv - n * bend * lens.x * px;                        // refraction: pull the image in from the rim

            // Soft 9-tap blur (frosted body of the glass).
            float b = lens.y;
            float3 col = tap(c) * 0.2;
            col += (tap(c + float2(b, 0) * px) + tap(c - float2(b, 0) * px) + tap(c + float2(0, b) * px) + tap(c - float2(0, b) * px)) * 0.125;
            col += (tap(c + float2(b, b) * px * 0.7) + tap(c - float2(b, b) * px * 0.7) + tap(c + float2(b, -b) * px * 0.7) + tap(c - float2(b, -b) * px * 0.7)) * 0.075;

            // Dispersion: red and blue bend a little differently near the rim.
            float2 dsp = n * bend * lens.w * px;
            col.r = lerp(col.r, tex2D(input, c + dsp).r, bend);
            col.b = lerp(col.b, tex2D(input, c - dsp).b, bend);

            // Vibrancy and tint.
            float l = dot(col, float3(0.2126, 0.7152, 0.0722));
            col = lerp(float3(l, l, l), col, lens.z);
            col = lerp(col, tint.rgb, tint.a);

            // Light: a thin bright band on the edge facing the light, a dimmer bounce opposite, a soft glow all round.
            float2 L = normalize(lite.xy);
            float band = pow(t, 7.0);
            float facing = saturate(dot(n, -L));
            float back = saturate(dot(n, L)) * 0.4;
            col += (facing + back) * band * lite.z;
            col += pow(t, 3.0) * lite.w;

            float a = saturate(0.5 - d);                                    // 1 px anti-aliased edge
            return float4(col * a, a);
        }
        """;

    static PixelShader? _shader;
    static bool _tried;

    /// <summary>Compiled shader, or null when the GPU / compiler can't run it (then the dock keeps its live blur).</summary>
    public static PixelShader? Shader
    {
        get
        {
            if (_tried) return _shader;
            _tried = true;
            try
            {
                if (!RenderCapability.IsPixelShaderVersionSupported(3, 0)) { Log.Info("Liquid Glass: ps_3_0 not supported by this GPU"); return null; }
                var bytes = Compile(Hlsl);
                var ps = new PixelShader();
                ps.SetStreamSource(new MemoryStream(bytes));
                ps.Freeze();
                _shader = ps;
                Log.Info($"Liquid Glass shader ready ({bytes.Length} bytes)");
            }
            catch (Exception ex) { Log.Warn("Liquid Glass shader unavailable: " + ex.Message); }
            return _shader;
        }
    }

    public static bool Available => Shader != null;

    // ---------- runtime HLSL compile (d3dcompiler_47.dll ships with Windows 10/11) ----------

    [ComImport, Guid("8BA5FB08-5195-40e2-AC58-0D989C3A0102"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ID3DBlob
    {
        [PreserveSig] IntPtr GetBufferPointer();
        [PreserveSig] IntPtr GetBufferSize();
    }

    [DllImport("d3dcompiler_47.dll", CharSet = CharSet.Ansi)]
    static extern int D3DCompile([MarshalAs(UnmanagedType.LPStr)] string src, IntPtr size, [MarshalAs(UnmanagedType.LPStr)] string? name,
        IntPtr defines, IntPtr include, [MarshalAs(UnmanagedType.LPStr)] string entry, [MarshalAs(UnmanagedType.LPStr)] string target,
        uint flags1, uint flags2, out ID3DBlob? code, out ID3DBlob? errors);

    static byte[] Compile(string source)
    {
        const uint OPTIMIZATION_LEVEL3 = 1 << 15;
        int hr = D3DCompile(source, (IntPtr)source.Length, "LiquidGlass", IntPtr.Zero, IntPtr.Zero, "main", "ps_3_0", OPTIMIZATION_LEVEL3, 0, out var code, out var errors);
        try
        {
            if (hr < 0 || code == null)
            {
                string msg = errors != null ? Marshal.PtrToStringAnsi(errors.GetBufferPointer(), (int)errors.GetBufferSize()) ?? "" : "";
                throw new InvalidOperationException($"HLSL compile failed 0x{hr:X8}: {msg.Trim()}");
            }
            var bytes = new byte[(int)code.GetBufferSize()];
            Marshal.Copy(code.GetBufferPointer(), bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            if (code != null) Marshal.ReleaseComObject(code);
            if (errors != null) Marshal.ReleaseComObject(errors);
        }
    }
}

/// <summary>The WPF effect around <see cref="LiquidGlass.Shader"/>; parameters are in physical pixels.</summary>
public sealed class LiquidGlassEffect : ShaderEffect
{
    public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty(nameof(Input), typeof(LiquidGlassEffect), 0);
    public static readonly DependencyProperty GeometryProperty = Const(nameof(Geometry), 0);
    public static readonly DependencyProperty LensProperty = Const(nameof(Lens), 1);
    public static readonly DependencyProperty LightProperty = Const(nameof(Light), 2);
    public static readonly DependencyProperty TintProperty = Const(nameof(Tint), 3);

    static DependencyProperty Const(string name, int register) =>
        DependencyProperty.Register(name, typeof(Point4D), typeof(LiquidGlassEffect), new UIPropertyMetadata(new Point4D(), PixelShaderConstantCallback(register)));

    public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }
    /// <summary>Width, height, corner radius, lens (rim) width — physical px.</summary>
    public Point4D Geometry { get => (Point4D)GetValue(GeometryProperty); set => SetValue(GeometryProperty, value); }
    /// <summary>Refraction px, blur px, saturation, dispersion px.</summary>
    public Point4D Lens { get => (Point4D)GetValue(LensProperty); set => SetValue(LensProperty, value); }
    /// <summary>Light direction (x, y), specular strength, edge glow.</summary>
    public Point4D Light { get => (Point4D)GetValue(LightProperty); set => SetValue(LightProperty, value); }
    /// <summary>Tint r, g, b (0–1) and amount.</summary>
    public Point4D Tint { get => (Point4D)GetValue(TintProperty); set => SetValue(TintProperty, value); }

    public LiquidGlassEffect()
    {
        PixelShader = LiquidGlass.Shader;
        UpdateShaderValue(InputProperty);
        UpdateShaderValue(GeometryProperty);
        UpdateShaderValue(LensProperty);
        UpdateShaderValue(LightProperty);
        UpdateShaderValue(TintProperty);
    }
}
