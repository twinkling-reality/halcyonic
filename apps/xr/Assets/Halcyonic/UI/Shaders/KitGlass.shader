// The room's one panel material, ART8's window glass (2026-10-31, at the floor-safe value): an opaque face over the whole
// panel, a grey gradient lightest at its top with a soft radial light there, an inner 1 dp edge and a top line of white,
// one deep soft shadow, and on a moment the bloom of its arrival. Nothing is see-through and nothing glows round it. One
// pass, no textures, no blur: every layer is a signed distance or a Gaussian's closed form. KitGlass.cs sets each panel's
// values, instanced; the material-wide values come once from KitGlassMaterial, whose formulas this repeats. Lengths are in
// dp at the eye; colours arrive as sRGB. The face is mixed in sRGB, as the prototype mixes it, then decoded; the shadow
// over the room is fitted to linear light for the lit room.
Shader "Halcyonic/Kit Glass"
{
    Properties
    {
        _Panel ("Width, height, corner radius and the words' inset, in dp", Vector) = (100, 100, 24, 10)
        _Quad ("The quad's left, bottom, width and height, in dp from the panel's centre", Vector) = (-50, -50, 100, 100)
        _Tone ("A moment's tone, sRGB, and 1 where it blooms", Vector) = (0, 0, 0, 0)
        _Light ("Bloom strength, reserved, opacity, reserved", Vector) = (0, 1, 1, 0)

        _N1 ("The face's middle, sRGB", Vector) = (0.259, 0.271, 0.286, 0)
        _FaceTop ("The face's top, sRGB", Vector) = (0.286, 0.298, 0.314, 0)
        _FaceBottom ("The face's foot, sRGB", Vector) = (0.231, 0.243, 0.259, 0)
        _Window ("Inner edge alpha, top light alpha, top light reach, inner edge width in dp", Vector) = (0.22, 0.10, 0.60, 1)
        _Shadow ("Shadow drop, blur and alpha, and the linear fit's exponent", Vector) = (30, 60, 0.35, 2.2)
        _Lights ("Reserved, top line width and alpha, bloom reach", Vector) = (0, 1, 0.28, 0.70)
        _BloomPeak ("The bloom's peak alpha", Float) = 0.11
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "ForceNoShadowCasting" = "True" }

        Pass
        {
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Panel)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Quad)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Tone)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Light)
            UNITY_INSTANCING_BUFFER_END(Props)

            float4 _N1, _FaceTop, _FaceBottom, _Window, _Shadow, _Lights;
            float _BloomPeak;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float3 Decode(float3 c)
            {
                float3 low = c / 12.92;
                float3 high = pow((c + 0.055) / 1.055, 2.4);
                return lerp(high, low, step(c, 0.04045));
            }

            // A rounded box's signed distance: negative inside.
            float Box(float2 p, float2 extent, float radius)
            {
                float2 q = abs(p) - extent + radius;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;
            }

            // The error function, to about 1e-4 (Winitzki's approximation).
            float Erf(float x)
            {
                float x2 = x * x;
                float t = exp(-x2 * (1.2732395 + 0.147 * x2) / (1.0 + 0.147 * x2));
                return sign(x) * sqrt(1.0 - t);
            }

            // How much of a shape blurred by a CSS blur of `blur` covers a point `d` outside its edge: blur / 2 is the sigma.
            float Blurred(float d, float blur)
            {
                float sigma = max(blur * 0.5, 1e-3);
                return 0.5 * (1.0 - Erf(d / (sigma * 1.4142136)));
            }

            // Three stops at 0, `middle` and 1 of one colour each, interpolated premultiplied as a browser does.
            float4 Stops(float t, float middle, float3 c0, float a0, float3 c1, float a1, float3 c2, float a2)
            {
                float4 p0 = float4(c0 * a0, a0), p1 = float4(c1 * a1, a1), p2 = float4(c2 * a2, a2);
                return t <= middle ? lerp(p0, p1, saturate(t / max(middle, 1e-4))) : lerp(p1, p2, saturate((t - middle) / max(1.0 - middle, 1e-4)));
            }

            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 panel = UNITY_ACCESS_INSTANCED_PROP(Props, _Panel);
                float4 area = UNITY_ACCESS_INSTANCED_PROP(Props, _Quad);
                float4 tone = UNITY_ACCESS_INSTANCED_PROP(Props, _Tone);
                float4 light = UNITY_ACCESS_INSTANCED_PROP(Props, _Light);

                float2 p = area.xy + i.uv * area.zw;
                float aa = max(max(fwidth(p.x), fwidth(p.y)), 1e-3);
                float2 extent = panel.xy * 0.5;
                float radius = min(panel.z, min(extent.x, extent.y));

                // The face, opaque over the whole panel: the grey from its top to its foot, the soft light at its top's middle,
                // the top line and the inner edge in white, a moment's bloom; mixed in sRGB.
                float outer = Box(p, extent, radius);
                float cover = saturate(0.5 - outer / aa);
                float tf = saturate((extent.y - p.y) / (2.0 * extent.y));
                float3 face = Stops(tf, 0.5, _FaceTop.rgb, 1, _N1.rgb, 1, _FaceBottom.rgb, 1).rgb;
                float reach = sqrt(p.x * p.x / (extent.x * extent.x) * 0.25 + (tf / _Window.z) * (tf / _Window.z));
                face = lerp(face, float3(1, 1, 1), _Window.y * max(0.0, 1.0 - reach));
                if (tone.a > 0.5) face = lerp(face, tone.rgb, _BloomPeak * light.x * max(0.0, 1.0 - tf / _Lights.w));
                float topLine = cover * saturate(0.5 + Box(p + float2(0, _Lights.y), extent, radius) / aa);
                float innerEdge = cover * saturate(0.5 + (outer + _Window.w) / aa);
                float white = 1.0 - (1.0 - _Lights.z * topLine) * (1.0 - _Window.x * innerEdge);
                face = lerp(face, float3(1, 1, 1), white);
                float4 faceOut = float4(Decode(face), 1.0);

                // Outside: the shadow, fitted to linear light.
                float shade = _Shadow.z * Blurred(Box(p + float2(0, _Shadow.x), extent, radius), _Shadow.y);
                float4 outside = float4(0, 0, 0, 1.0 - pow(1.0 - shade, _Shadow.w));

                float4 result = faceOut * cover + outside * (1.0 - cover);
                return result * light.z;
            }
            ENDCG
        }
    }
}
