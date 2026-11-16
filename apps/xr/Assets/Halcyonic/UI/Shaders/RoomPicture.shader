// A still of a photo or video given to a task (RoomPictureView): the picture filling its square, cropped to its middle so it is
// never stretched, its corners rounded as the card's tiles are, its edge smoothed by a pixel. The picture is sampled as sRGB, as
// it was decoded, so the headset's linear blend shows it as the person took it.
Shader "Halcyonic/Room Picture"
{
    Properties
    {
        _Picture ("The picture", 2D) = "black" {}
        _Crop ("The part of the picture shown: scale xy, offset zw", Vector) = (1, 1, 0, 0)
        _Corner ("The corner's radius as a share of the side", Float) = 0.15
        _Alpha ("How much of it shows", Float) = 1
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
            #include "UnityCG.cginc"
            sampler2D _Picture;
            float4 _Crop;
            float _Corner;
            float _Alpha;
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
                // The rounded square's distance, in sides, then a pixel's smoothing at its edge.
                float2 q = abs(i.uv - 0.5) - (0.5 - _Corner);
                float outside = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - _Corner;
                float edge = max(fwidth(outside), 1e-5);
                float inside = saturate(0.5 - outside / edge);
                float3 colour = tex2D(_Picture, i.uv * _Crop.xy + _Crop.zw).rgb;
                float alpha = inside * _Alpha;
                return float4(colour * alpha, alpha);
            }
            ENDCG
        }
    }
}
