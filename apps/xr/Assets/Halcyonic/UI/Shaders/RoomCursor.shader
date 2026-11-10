// The agent's cursor and its label's ground (DESIGN's BUILD-SPEC-v4 2.3 and 1.2), one quad:
//   the arrow, about 26 by 33 dp: a four-cornered pointer filled #59CFF1 with a #141416 edge 2.24 dp wide, round at its
//     corners, its tip 2.4 dp in from the cursor's own corner;
//   the label's ground, the darker voice glass: a squircle of corner 14 dp, #181B1E at the page's 0.84, with a 1 dp
//     hairline of white at 10 percent just inside its edge, its top left 26 dp right of and 30 dp below that corner.
// Signed distances only, no texture. Lengths are dp from the cursor's corner, x right and y up, so the arrow hangs below
// it. The page mixes its alphas in sRGB and this buffer blends in linear light: each pixel is mixed as the page mixes it,
// then given the colour and alpha that show the page's value over the design's dark room and over white.
// RoomCursor.cs sets the label's box, the quad and how far into view it is; the words are drawn over it by their label.
Shader "Halcyonic/Room Cursor"
{
    Properties
    {
        _Quad ("The quad's left, bottom, width and height, in dp from the cursor's corner", Vector) = (-4, -74, 300, 78)
        _Label ("The label's left and top, its width and height, in dp", Vector) = (26, -30, 200, 40)
        _Corner ("The label's corner in dp", Float) = 14
        _Shown ("How far into view the cursor is, 0 to 1", Float) = 1
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
                UNITY_DEFINE_INSTANCED_PROP(float4, _Quad)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Label)
                UNITY_DEFINE_INSTANCED_PROP(float, _Corner)
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

            // A squircle-cornered box's signed distance (|x|^4 + |y|^4 = r^4 at the corners): negative inside.
            float Squircle(float2 p, float2 extent, float radius)
            {
                float2 q = abs(p) - extent + radius;
                float2 m = max(q, 0.0);
                return pow(pow(m.x, 4.0) + pow(m.y, 4.0), 0.25) + min(max(q.x, q.y), 0.0) - radius;
            }

            // One side of the arrow: the squared distance to it, and whether a ray from p crosses it.
            void Side(float2 p, float2 a, float2 b, inout float nearest, inout float flip)
            {
                float2 e = b - a;
                float2 w = p - a;
                float2 d = w - e * saturate(dot(w, e) / dot(e, e));
                nearest = min(nearest, dot(d, d));
                bool up = p.y >= a.y;
                bool under = p.y < b.y;
                bool left = e.x * w.y > e.y * w.x;
                if ((up && under && left) || (!up && !under && !left)) flip = -flip;
            }

            // The arrow's signed distance in dp: negative inside. Its four corners, from the tip round.
            float Arrow(float2 p)
            {
                float2 tip = float2(2.4, -2.4);
                float2 foot = float2(3.2, -35.2);
                float2 notch = float2(12.16, -26.56);
                float2 wing = float2(28.16, -25.6);
                float nearest = dot(p - tip, p - tip);
                float flip = 1.0;
                Side(p, tip, wing, nearest, flip);
                Side(p, foot, tip, nearest, flip);
                Side(p, notch, foot, nearest, flip);
                Side(p, wing, notch, nearest, flip);
                return flip * sqrt(nearest);
            }

            // A colour at an alpha laid over what is there, as the page lays it: premultiplied, in sRGB.
            float4 Over(float4 under, float3 colour, float alpha)
            {
                return float4(under.rgb * (1.0 - alpha) + colour * alpha, under.a * (1.0 - alpha) + alpha);
            }

            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 area = UNITY_ACCESS_INSTANCED_PROP(Props, _Quad);
                float4 label = UNITY_ACCESS_INSTANCED_PROP(Props, _Label);
                float corner = UNITY_ACCESS_INSTANCED_PROP(Props, _Corner);
                float shown = saturate(UNITY_ACCESS_INSTANCED_PROP(Props, _Shown));

                float2 p = area.xy + i.uv * area.zw;
                float aa = max(max(fwidth(p.x), fwidth(p.y)), 1e-3);
                float4 mixed = float4(0, 0, 0, 0);

                // The label's ground: #181B1E at 0.84, then the hairline, white at 10 percent, 1 dp inside its edge.
                if (label.z > 0.0)
                {
                    float2 extent = label.zw * 0.5;
                    float2 centre = float2(label.x + extent.x, label.y - extent.y);
                    float d = Squircle(p - centre, extent, min(corner, min(extent.x, extent.y)));
                    float cover = saturate(0.5 - d / aa);
                    mixed = Over(mixed, float3(24.0, 27.0, 30.0) / 255.0, 0.84 * cover);
                    float line2 = cover * saturate(0.5 + (d + 1.0) / aa);
                    mixed = Over(mixed, float3(1.0, 1.0, 1.0), 0.10 * line2);
                }

                // The arrow: its fill, then its edge centred on its outline, 1.12 dp either side.
                float arrow = Arrow(p);
                mixed = Over(mixed, float3(89.0, 207.0, 241.0) / 255.0, saturate(0.5 - arrow / aa));
                mixed = Over(mixed, float3(20.0, 20.0, 22.0) / 255.0, saturate(0.5 - (abs(arrow) - 1.12) / aa));

                mixed *= shown;

                // What shows the page's value over the dark room and over white, blended in linear light.
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
