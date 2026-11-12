// A person's world as it was mixed for them: one picture all round, drawn on a shell shaped by the picture's own
// depth so near things stand nearer. The picture is the scene's layers mixed by the person's description
// (WorldMix.shader), made again only when the mix changes.
Shader "Halcyonic/World/Panorama"
{
    Properties
    {
        _PanoMixed ("The mixed world", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }
        Cull Off
        ZWrite On

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "HalcyonicWorld.cginc"

            sampler2D _PanoMixed;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
                float distance : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Where a direction from the shell's centre falls in the panorama, with gradients that stay small
            // across the seam behind the viewer (the u of the far side of the wrap is used there).
            float2 PanoUv(float3 d, out float2 dx, out float2 dy)
            {
                d = normalize(d);
                float u = atan2(d.x, d.z) * 0.15915494 + 0.5;
                float v = asin(clamp(d.y, -1.0, 1.0)) * 0.31830989 + 0.5;
                float u2 = frac(u + 0.5);
                float2 a = float2(u, v);
                float2 b = float2(u2, v);
                float2 dxa = ddx(a), dya = ddy(a), dxb = ddx(b), dyb = ddy(b);
                bool wrap = abs(dxa.x) + abs(dya.x) > abs(dxb.x) + abs(dyb.x) + 1e-6;
                dx = wrap ? dxb : dxa;
                dy = wrap ? dyb : dya;
                return a;
            }

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                o.distance = length(v.vertex.xyz);
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float2 dx, dy;
                float2 uv = PanoUv(i.dir, dx, dy);
                half3 c = tex2Dgrad(_PanoMixed, uv, dx, dy).rgb;
                // The arrival: the picture opens outward from the person, near things first.
                c *= WorldArrived(i.distance);
                return half4(c, 1.0);
            }
            ENDCG
        }
    }
}
