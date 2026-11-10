// The working field (DESIGN's BUILD-SPEC-v4 2.3 and 1.2): light points over a soft lifted ground, round the working
// character and the place its output will take, fading in size and brightness to nothing at its edge. One quad the
// field's own size, no texture, every pixel a formula of its place and the clock:
//   the ground, a dark lift (#0A0C0D at 22 percent, half that 60 percent out, none at the edge) under a white lift at
//     5 percent, so the points read over a white wall;
//   a hairline grid every 36 dp, white at 7 percent, fading with the field;
//   points every 9 dp, 3.0 dp across, one in twelve lit at 4.6 dp; alpha 0.40 to 0.90, each twinkling over its own
//     1.8 to 3.6 s, a band of light crossing every 6 s; smaller and fainter over the outer 55 percent.
// The page mixes these in sRGB and this buffer blends in linear light, so each pixel is mixed here as the page mixes it
// and then given the colour and alpha that show exactly the page's value over the design's dark room (about #1C1C1C)
// and over a white wall: fit to pure black instead, a linear blend hides a dim room more than the page does.
// RoomField.cs sets the size, the clock and how far into view it is, and mirrors these formulas for its checks.
Shader "Halcyonic/Room Field"
{
    Properties
    {
        _Field ("The field's width and height in dp", Vector) = (794, 375, 0, 0)
        _Clock ("The field's clock in seconds", Float) = 0
        _Shown ("How far into view the field is, 0 to 1", Float) = 1
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
            #pragma target 3.5
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Field)
                UNITY_DEFINE_INSTANCED_PROP(float, _Clock)
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

            float Smooth(float v)
            {
                v = saturate(v);
                return v * v * (3.0 - 2.0 * v);
            }

            // A point's own numbers from its place in the grid: whole-number arithmetic, so RoomField.cs gets the same ones.
            uint Mix(uint h)
            {
                h ^= h >> 16;
                h *= 0x7feb352du;
                h ^= h >> 15;
                h *= 0x846ca68bu;
                h ^= h >> 16;
                return h;
            }

            // White at an alpha laid over what is there, as the page lays it: premultiplied, in sRGB.
            float4 White(float4 under, float alpha)
            {
                return float4(under.rgb * (1.0 - alpha) + alpha, under.a * (1.0 - alpha) + alpha);
            }

            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 field = UNITY_ACCESS_INSTANCED_PROP(Props, _Field);
                float clock = UNITY_ACCESS_INSTANCED_PROP(Props, _Clock);
                float shown = saturate(UNITY_ACCESS_INSTANCED_PROP(Props, _Shown));

                // This pixel in dp from the field's middle, y up, and how far out it is: 0 at the middle, 1 at the edge.
                float2 size = max(field.xy, 1.0);
                float2 reach = size * 0.5;
                float2 p = (i.uv - 0.5) * size;
                float aa = max(max(fwidth(p.x), fwidth(p.y)), 1e-3);
                float rho = length(p / reach);
                float edge = Smooth((1.0 - rho) / 0.55);

                // The ground: the dark lift, then the white lift over it.
                float dark = rho < 0.6 ? lerp(0.22, 0.11, rho / 0.6) : lerp(0.11, 0.0, saturate((rho - 0.6) / 0.4));
                float4 mixed = float4(float3(10.0, 12.0, 13.0) / 255.0 * dark, dark);
                mixed = White(mixed, 0.05 * saturate(1.0 - rho));

                // The hairline grid, 1 dp lines every 36 dp, under the points.
                float2 off = abs(p - 36.0 * round(p / 36.0));
                float2 line2 = saturate(0.5 - (off - 0.5) / aa);
                mixed = White(mixed, 0.07 * edge * line2.x);
                mixed = White(mixed, 0.07 * edge * line2.y);

                // The point this pixel belongs to: the nearest of the 9 dp grid.
                float2 cell = round(p / 9.0);
                float2 at = cell * 9.0;
                float pointEdge = Smooth((1.0 - length(at / reach)) / 0.55);
                if (pointEdge > 0.01)
                {
                    uint h = Mix((uint(int(cell.x) + 32768) * 73856093u) ^ (uint(int(cell.y) + 32768) * 19349663u));
                    uint h2 = Mix(h + 0x9e3779b9u);
                    float k = float(h & 0xffffu) / 65535.0;
                    float lit = float(h >> 16) / 65535.0 < 0.08 ? 1.0 : 0.0;
                    float phase = float(h2 & 0xffffu) / 65535.0 * 6.2831853;
                    float period = 1.8 + float(h2 >> 16) / 65535.0 * 1.8;

                    // The band: across the field from its lower left to its upper right, once every 6 s.
                    float along = ((at.x + reach.x) / size.x + (at.y + reach.y) / size.y) * 0.5;
                    float centre = frac(clock / 6.0) * 1.7 - 0.35;
                    float band = Smooth(1.0 - abs(along - centre) / 0.4);
                    float twinkle = 0.5 + 0.5 * sin(clock * 6.2831853 / period + phase);
                    float alpha = lit > 0.5 ? 0.6 + 0.4 * twinkle : 0.40 + 0.50 * (0.45 * k + 0.55 * twinkle);
                    alpha *= (0.55 + 0.45 * band) * pointEdge;
                    float radius = (lit > 0.5 ? 4.6 : 3.0) * 0.5 * (0.45 + 0.55 * pointEdge);
                    float cover = saturate(0.5 - (length(p - at) - radius) / aa);
                    mixed = White(mixed, alpha * cover);
                }

                // Fading in or out, the whole field together.
                mixed *= shown;

                // What shows the page's value over the dark room and over white, blended in linear light: how much of
                // the room passes, from the two, then the colour that lands on the page's value over the dark room.
                const float room = 0.11;
                float through = 1.0 - mixed.a;
                float grey = dot(mixed.rgb, float3(1.0, 1.0, 1.0)) / 3.0;
                float passes = saturate((Decode1(saturate(grey + through)) - Decode1(grey + through * room)) / (1.0 - Decode1(room)));
                float3 overRoom = float3(Decode1(mixed.r + through * room), Decode1(mixed.g + through * room), Decode1(mixed.b + through * room));
                return float4(max(overRoom - passes * Decode1(room), 0.0), 1.0 - passes);
            }
            ENDCG
        }
    }
}
