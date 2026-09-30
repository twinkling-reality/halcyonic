// Flat, soft-edged shapes around a character, one profile per material: the halo behind it (a
// radial glow), the ring that sweeps around it while tests run (a band with a bright head and a
// fading tail), and the plate behind its labels (a rounded rectangle). Edges are anti-aliased in
// the shader, so they stay smooth without MSAA. CharacterView sets _Color and _Rect per renderer.
Shader "Halcyonic/Soft Shape"
{
    Properties
    {
        [Enum(Disc, 0, Band, 1, Plate, 2)] _Profile ("Profile", Float) = 0
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Falloff ("Disc falloff", Float) = 1.6
        _Rect ("Plate width, height and corner radius", Vector) = (1, 1, 0.1, 0)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "ForceNoShadowCasting" = "True" }

        Pass
        {
            // Premultiplied, so the halo can add light as well as cover what is behind it.
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            float _Profile;
            float4 _Color;
            float _Falloff;
            float4 _Rect;

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
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                // Derivatives are taken here, outside the branches, where they are defined.
                float2 pixel = max(fwidth(i.uv), 1e-5);
                float alpha;
                // How much of what is behind a shape it covers, relative to the light it adds.
                float cover = 1.0;
                if (_Profile < 0.5)
                {
                    float d = length(i.uv - 0.5) * 2.0;
                    alpha = pow(saturate(1.0 - d), _Falloff);
                    // A glow: it lightens what is behind it more than it hides it.
                    cover = 0.55;
                }
                else if (_Profile < 1.5)
                {
                    // Across the band: solid in the middle, a pixel of softness at each edge.
                    float across = saturate((0.5 - abs(i.uv.y - 0.5)) / pixel.y);
                    // Along it: a faint ring, brightening toward a head that ends in a short fade.
                    float along = 0.15 + 0.85 * i.uv.x * i.uv.x * i.uv.x * saturate((1.0 - i.uv.x) * 30.0);
                    alpha = across * along;
                }
                else
                {
                    float2 extent = _Rect.xy * 0.5;
                    float2 p = (i.uv - 0.5) * _Rect.xy;
                    float2 q = abs(p) - extent + _Rect.z;
                    float d = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - _Rect.z;
                    float aa = 0.5 * (pixel.x * _Rect.x + pixel.y * _Rect.y);
                    alpha = saturate(0.5 - d / aa);
                }
                float a = _Color.a * alpha;
                return float4(_Color.rgb * a, a * cover);
            }
            ENDCG
        }
    }
}
