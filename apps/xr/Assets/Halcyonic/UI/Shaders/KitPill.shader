// The room's glossy capsule material (2026-10-31, unified glass-capsule material on the window glass): a five-stop vertical
// gradient in the button's meaning (green, red, blue or smoked-slate neutral), a bottom inner glow or bounce in the same
// hue, a crisp white top specular crown inset from the top and sides, a 1 dp top rim catch-light inside the 1.5 dp outer
// rim, and the darker outer rim around the edge. Every layer is mixed in sRGB inside the shader, repeating
// KitPillMaterial.Sample, then decoded to linear light for Unity's eye buffer.
// Reads _Shape and _Fill.a from Surface's MaterialPropertyBlock so choreography fades and curve strips work seamlessly.
Shader "Halcyonic/Kit Pill"
{
    Properties
    {
        _Fill ("Fill, straight alpha", Color) = (1, 1, 1, 1)
        _Shape ("Width, height, corner radius and edge width, in units", Vector) = (1, 1, 0.5, 0)
        _Stop0 ("Stop 0 sRGB and position", Vector) = (0.424, 0.769, 0.980, 0.00)
        _Stop1 ("Stop 1 sRGB and position", Vector) = (0.082, 0.424, 0.769, 0.28)
        _Stop2 ("Stop 2 sRGB and position", Vector) = (0.055, 0.365, 0.686, 0.66)
        _Stop3 ("Stop 3 sRGB and position", Vector) = (0.039, 0.294, 0.565, 0.88)
        _Stop4 ("Stop 4 sRGB and position", Vector) = (0.341, 0.788, 1.000, 1.00)
        _Rim ("Outer rim colour, sRGB", Vector) = (0.031, 0.220, 0.439, 1.0)
        _Glow ("Bottom inner glow sRGB and alpha", Vector) = (0.431, 0.843, 1.000, 0.28)
        _Gloss ("Crown mid share, crown end share, crown height share, crown top alpha", Vector) = (0.36, 0.50, 0.50, 0.34)
        _Spec ("Crown mid alpha, glow side inset share, glow height share, top catch alpha", Vector) = (0.10, 0.14, 0.20, 0.42)
        _GlowGeom ("Bottom glow inset dp, radial reach, one dp in units, outer rim dp", Vector) = (1.0, 0.72, 0.001, 1.5)
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
                UNITY_DEFINE_INSTANCED_PROP(float4, _Fill)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Shape)
            UNITY_INSTANCING_BUFFER_END(Props)

            float4 _Stop0, _Stop1, _Stop2, _Stop3, _Stop4, _Rim, _Glow, _Gloss, _Spec, _GlowGeom;

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

            float Box(float2 p, float2 extent, float radius)
            {
                float2 q = abs(p) - extent + radius;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;
            }

            float3 FiveStops(float t, float4 s0, float4 s1, float4 s2, float4 s3, float4 s4)
            {
                if (t <= s1.w) return lerp(s0.rgb, s1.rgb, saturate((t - s0.w) / max(s1.w - s0.w, 1e-4)));
                if (t <= s2.w) return lerp(s1.rgb, s2.rgb, saturate((t - s1.w) / max(s2.w - s1.w, 1e-4)));
                if (t <= s3.w) return lerp(s2.rgb, s3.rgb, saturate((t - s2.w) / max(s3.w - s2.w, 1e-4)));
                return lerp(s3.rgb, s4.rgb, saturate((t - s3.w) / max(s4.w - s3.w, 1e-4)));
            }

            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 fill = UNITY_ACCESS_INSTANCED_PROP(Props, _Fill);
                float4 shape = UNITY_ACCESS_INSTANCED_PROP(Props, _Shape);

                float unit = max(_GlowGeom.z, 1e-6);
                float2 size = max(shape.xy / unit, 1e-4);
                float2 extent = size * 0.5;
                float radius = min(extent.x, extent.y);
                float2 p = (i.uv - 0.5) * size;

                float outer = Box(p, extent, radius);
                float aa = max(fwidth(outer), 1e-3);
                float cover = saturate(0.5 - outer / aa);
                float t = saturate((extent.y - p.y) / size.y);

                float3 colour = FiveStops(t, _Stop0, _Stop1, _Stop2, _Stop3, _Stop4);

                float gx = max((0.5 - _Spec.y) * size.x, 1.0);
                float gy = max(_Spec.z * size.y, 1.0);
                float y0 = -extent.y + _GlowGeom.x;
                float dx = p.x / gx;
                float dy = max(0.0, p.y - y0) / gy;
                float glowDist = sqrt(dx * dx + dy * dy) / max(_GlowGeom.y, 1e-4);
                float glowWeight = _Glow.a * max(0.0, 1.0 - glowDist);
                colour = lerp(colour, _Glow.rgb, glowWeight);

                float crownBeforeMid = lerp(_Gloss.w, _Spec.x, saturate(t / max(_Gloss.x, 1e-4)));
                float crownAfterMid = _Spec.x * (1.0 - saturate((t - _Gloss.x) / max(_Gloss.y - _Gloss.x, 1e-4)));
                float glossAlpha = t < _Gloss.x ? crownBeforeMid : (t < _Gloss.y ? crownAfterMid : 0.0);
                colour = lerp(colour, float3(1.0, 1.0, 1.0), glossAlpha);

                float innerRadius = max(radius - _GlowGeom.w, 0.0);
                float innerTop = Box(float2(p.x, p.y + 1.2), max(extent - _GlowGeom.w, 0.0), innerRadius);
                float catchMask = step(0.0, p.y) * saturate(0.5 - (outer + _GlowGeom.w) / aa) * saturate(0.5 + innerTop / aa);
                colour = lerp(colour, float3(1.0, 1.0, 1.0), _Spec.w * catchMask);

                float rimMask = saturate(0.5 + (outer + _GlowGeom.w) / aa);
                colour = lerp(colour, _Rim.rgb, rimMask);

                return float4(Decode(colour), 1.0) * (cover * fill.a);
            }
            ENDCG
        }
    }
}
