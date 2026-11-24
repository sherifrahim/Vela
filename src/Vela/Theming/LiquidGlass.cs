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
/// frosted (blurred first by a WPF BlurEffect); towards the rounded edge the image is pulled inwards by Snell's law,
/// red and blue split slightly (dispersion), and a light-catching rim runs along the edge facing the light.
///
/// The HLSL below is compiled once at startup with Windows' own d3dcompiler_47.dll (no tools or packages) into a
/// ps_3_0 shader that WPF runs on the GPU. The input is the wallpaper slice under the bar (WallpaperGlass.Slice), so
/// the glass redraws only when the dock moves or the wallpaper changes: zero idle cost.
/// </summary>
public static class LiquidGlass
{
    // The lens profile, the specular rim and the constants (bezel 25 % of the height up to 20 px, pull up to 80 % of
    // the height / 60 px, ±10 % chromatic split, saturation 1.8, 5 % veil, 2.5 % grain, light from the top) follow
    // WacOS's real-glass renderer (src/shell/backdrop.cpp: SnellAt, SpecFor, DrawPiece) as published under the MIT
    // License at commit 2beced5 (Copyright (c) 2026 renzyxx; notice in THIRD_PARTY_NOTICES.md), which in turn
    // measured them on macOS 26. Rewritten as a single pixel shader.
    const string Hlsl = """
        sampler2D input : register(s0);
        float4 geo  : register(c0);  // x,y: size px   z: corner radius px   w: bezel (lens band) px
        float4 lens : register(c1);  // x: max pull px   y: chromatic split (0.1)   z: saturation   w: white veil
        float4 lite : register(c2);  // x,y: light direction   z: rim gain   w: rim band px
        float4 tint : register(c3);  // rgb: tint colour   a: tint amount
        float4 more : register(c4);  // x: px per DIP   y: grain amount

        float sdBox(float2 p, float2 b, float r)
        {
            float2 q = abs(p) - b + r;
            return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
        }

        // How far the image under the bezel moves (0..1), by Snell's law: the bezel is a convex squircle
        // h(t) = (1 - (1 - t)^4)^(1/4) (t = 0 at the rim, 1 where the flat middle starts), glass n = 1.5.
        float snell(float t)
        {
            float x = max(t, 0.0001);
            float u = 1.0 - x;
            float u3 = u * u * u;
            float slope = u3 / pow(max(0.000001, 1.0 - u3 * u), 0.75) * 2.5;
            float th = atan(slope);
            float tr = asin(sin(th) / 1.5);
            return tan(th - tr) / 1.1156;                                    // normalised: 1 right at the rim
        }

        float4 main(float2 uv : TEXCOORD) : COLOR
        {
            float2 size = geo.xy;
            float2 hs = size * 0.5;
            float2 p = uv * size - hs;
            float d = sdBox(p, hs, geo.z);                                   // < 0 inside the shape
            float a = saturate(0.5 - d);                                     // anti-aliased edge
            if (a <= 0.0) return float4(0, 0, 0, 0);

            float2 n = float2(sdBox(p + float2(1, 0), hs, geo.z) - sdBox(p - float2(1, 0), hs, geo.z),
                              sdBox(p + float2(0, 1), hs, geo.z) - sdBox(p - float2(0, 1), hs, geo.z));
            n = n / max(length(n), 0.0001);                                  // outward normal

            // Refraction: under the bezel each point reads the (already frosted) scene further in; red a bit
            // further and blue a bit less, so a faint colour fringe shows only where the glass bends.
            float t = saturate(-d / geo.w);
            float2 pull = n * (lens.x * (t < 1.0 ? snell(t) : 0.0)) / size;
            float3 col;
            col.r = tex2D(input, uv - pull * (1.0 + lens.y)).r;
            col.g = tex2D(input, uv - pull).g;
            col.b = tex2D(input, uv - pull * (1.0 - lens.y)).b;

            // Vibrancy ("saturate(180%)"), tint, and the faint white veil inside the glass.
            float l = dot(col, float3(0.2126, 0.7152, 0.0722));
            col = max(lerp(float3(l, l, l), col, lens.z), 0.0);
            col = lerp(col, tint.rgb, tint.a);
            col += (1.0 - col) * lens.w;

            // Grain hides the banding a heavy blur leaves behind.
            float g = frac(sin(dot(floor(uv * size), float2(12.9898, 78.233))) * 43758.5453) - 0.5;
            col += g * more.y;

            // Specular rim: a crisp wire right on the edge plus a soft glow a few px in; brightest where the edge
            // faces the light (top), a little on the opposite edge, almost nothing on the sides.
            float din = max(0.0, -d);
            float soft = saturate(1.0 - din / lite.w);
            float edge = 0.8 * exp(-din / (0.55 * more.x)) + 0.16 * soft * soft * soft;
            float an = dot(n, normalize(lite.xy));
            float ang = 0.04 + 0.85 * pow(saturate(an), 2.0) + 1.0 * pow(saturate(-an), 2.0);
            float s = din > lite.w ? 0.0 : saturate(edge * ang * lite.z);
            col = col + (1.0 - col) * s;

            return float4(saturate(col) * a, a);
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
    public static readonly DependencyProperty MoreProperty = Const(nameof(More), 4);

    static DependencyProperty Const(string name, int register) =>
        DependencyProperty.Register(name, typeof(Point4D), typeof(LiquidGlassEffect), new UIPropertyMetadata(new Point4D(), PixelShaderConstantCallback(register)));

    public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }
    /// <summary>Width, height, corner radius, bezel (lens band) width — physical px.</summary>
    public Point4D Geometry { get => (Point4D)GetValue(GeometryProperty); set => SetValue(GeometryProperty, value); }
    /// <summary>Max pull px, chromatic split (0.1 = ±10 %), saturation, white veil.</summary>
    public Point4D Lens { get => (Point4D)GetValue(LensProperty); set => SetValue(LensProperty, value); }
    /// <summary>Light direction (x, y), rim gain, rim band px.</summary>
    public Point4D Light { get => (Point4D)GetValue(LightProperty); set => SetValue(LightProperty, value); }
    /// <summary>Tint r, g, b (0–1) and amount.</summary>
    public Point4D Tint { get => (Point4D)GetValue(TintProperty); set => SetValue(TintProperty, value); }
    /// <summary>Physical px per DIP, grain amount.</summary>
    public Point4D More { get => (Point4D)GetValue(MoreProperty); set => SetValue(MoreProperty, value); }

    public LiquidGlassEffect()
    {
        PixelShader = LiquidGlass.Shader;
        UpdateShaderValue(InputProperty);
        UpdateShaderValue(GeometryProperty);
        UpdateShaderValue(LensProperty);
        UpdateShaderValue(LightProperty);
        UpdateShaderValue(TintProperty);
        UpdateShaderValue(MoreProperty);
    }
}
