// The agent's words band's ground (DESIGN's BUILD-SPEC-v4 1.6, FINISH-2 2026-11-05): one colour, #121416 at the page's 0.72,
// fading to nothing in a straight line over the outer 120 dp at each side and over 22 dp at its top and foot, flat in the
// middle so every word sits on the full ground. No edge, no corner, no sheen: it is a fade, not a surface. One quad the
// band's own size, no texture. The page mixes its alpha in sRGB and this buffer blends in linear light, so each pixel takes
// the alpha that shows over a white wall what the page shows there (the binding case; the middle comes to about 0.91).
// RoomBand.cs sets the values; the colour arrives as sRGB and is decoded here.
Shader "Halcyonic/Room Band"
{
    Properties
    {
        _Band ("The band's width and height, its side fade and its top and foot fade, in dp", Vector) = (880, 136, 120, 22)
        _Ground ("The ground's colour, sRGB, and its alpha as the page mixes it", Vector) = (0.0706, 0.0784, 0.0863, 0.72)
        _Shown ("How far into view the band is, 0 to 1", Float) = 1
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
                UNITY_DEFINE_INSTANCED_PROP(float4, _Band)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Ground)
                UNITY_DEFINE_INSTANCED_PROP(float, _Shown)
            UNITY_INSTANCING_BUFFER_END(Props)

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

            float Decode1(float c)
            {
                return c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4);
            }

            float3 Decode(float3 c)
            {
                return float3(Decode1(c.r), Decode1(c.g), Decode1(c.b));
            }

            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 band = UNITY_ACCESS_INSTANCED_PROP(Props, _Band);
                float4 ground = UNITY_ACCESS_INSTANCED_PROP(Props, _Ground);
                float shown = UNITY_ACCESS_INSTANCED_PROP(Props, _Shown);

                // Where this pixel is, in dp from the band's lower left, and how much of the ground stands there.
                float2 p = i.uv * band.xy;
                float across = saturate(min(p.x, band.x - p.x) / max(band.z, 1e-4));
                float down = saturate(min(p.y, band.y - p.y) / max(band.w, 1e-4));
                float page = ground.a * across * down * saturate(shown);

                // The page's own mix over a white wall, in sRGB, and the linear-light alpha that lands on it.
                float grey = dot(ground.rgb, float3(1.0, 1.0, 1.0)) / 3.0;
                float mixed = lerp(1.0, grey, page);
                float alpha = saturate((1.0 - Decode1(mixed)) / max(1.0 - Decode1(grey), 1e-4));
                return float4(Decode(ground.rgb) * alpha, alpha);
            }
            ENDCG
        }
    }
}
