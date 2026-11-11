// A code to scan (RoomCodeView): its cells, one texel each in a texture the size of the code, read without filtering so
// every cell stays square, dark where the texel is set and clear elsewhere over the light well under it. Four taps a
// quarter of a pixel apart smooth the cells' edges without rounding their corners. The colour arrives as sRGB.
Shader "Halcyonic/Room Code"
{
    Properties
    {
        _Cells ("The cells, one texel each, alpha set where dark", 2D) = "black" {}
        _Colour ("The dark cells' colour, sRGB, and its alpha", Vector) = (0, 0, 0, 1)
        _Side ("The cells a side", Float) = 21
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
            sampler2D _Cells;
            float _Side;
            float4 _Colour;
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
            float3 Decode(float3 c)
            {
                float3 low = c / 12.92;
                float3 high = pow((c + 0.055) / 1.055, 2.4);
                return lerp(high, low, step(c, 0.04045));
            }
            float Cell(float2 uv)
            {
                // The middle of the cell this point falls in, so the sample never mixes two cells.
                float2 middle = (min(floor(saturate(uv) * _Side), _Side - 1.0) + 0.5) / _Side;
                return tex2Dlod(_Cells, float4(middle, 0, 0)).a;
            }
            float4 frag(v2f i) : SV_Target
            {
                float2 nudge = float2(fwidth(i.uv.x), fwidth(i.uv.y)) * 0.25;
                float ink = (Cell(i.uv + float2(-nudge.x, -nudge.y)) + Cell(i.uv + float2(nudge.x, -nudge.y))
                    + Cell(i.uv + float2(-nudge.x, nudge.y)) + Cell(i.uv + float2(nudge.x, nudge.y))) * 0.25;
                float alpha = _Colour.a * ink;
                return float4(Decode(_Colour.rgb) * alpha, alpha);
            }
            ENDCG
        }
    }
}
