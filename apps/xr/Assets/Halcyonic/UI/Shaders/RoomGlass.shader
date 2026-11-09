// The redesign's surface (DESIGN's FINISH-3, 2026-11-05): a translucent tonal layer, a vertical gradient from its top to
// its foot with its own alpha, a hairline in its own gradient, squircle corners (a superellipse of exponent 4), an optional
// soft shadow, an optional lighter sentence bar, and one sheen band crossing every glass surface together in the plane's coordinates. No textures and no blur:
// signed distances and a Gaussian's closed form. RoomSurface.cs sets each surface's values, instanced; the sheen comes once
// a frame from RoomSheen as globals. Lengths are in dp at the eye; colours arrive as sRGB and mix in sRGB, then decode.
Shader "Halcyonic/Room Glass"
{
    Properties
    {
        _Panel ("Width, height, corner radius in dp, and 1 for a squircle", Vector) = (100, 100, 34, 1)
        _Quad ("The quad's left, bottom, width and height, in dp from the panel's centre", Vector) = (-50, -50, 100, 100)
        _FillTop ("The face's top, sRGB", Vector) = (0.231, 0.286, 0.314, 1)
        _FillFoot ("The face's foot, sRGB", Vector) = (0.180, 0.227, 0.251, 1)
        _EdgeTop ("The edge line's top, sRGB, and its width in dp", Vector) = (0.333, 0.380, 0.404, 1)
        _EdgeFoot ("The edge line's foot, sRGB, and 1 where it shows", Vector) = (0.286, 0.329, 0.349, 1)
        _Shade ("Shadow drop, blur and alpha in dp, and 1 where it shows", Vector) = (10, 28, 0.22, 1)
        _Inner ("A lighter inner bar: its top from the panel's top and its height in dp, its corner in dp, and its inset from the sides in dp where it shows (0: none)", Vector) = (0, 0, 0, 0)
        _InnerColour ("The inner bar's colour, sRGB, and its alpha", Vector) = (1, 1, 1, 0)
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
                UNITY_DEFINE_INSTANCED_PROP(float4, _FillTop)
                UNITY_DEFINE_INSTANCED_PROP(float4, _FillFoot)
                UNITY_DEFINE_INSTANCED_PROP(float4, _EdgeTop)
                UNITY_DEFINE_INSTANCED_PROP(float4, _EdgeFoot)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Shade)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Inner)
                UNITY_DEFINE_INSTANCED_PROP(float4, _InnerColour)
            UNITY_INSTANCING_BUFFER_END(Props)

            // The sheen, once a frame for every surface (RoomSheen): the plane's origin and its right and up in the world,
            // and the band's centre across the plane in metres, its width in metres and its strength now (0 at rest).
            float4 _RoomSheenOrigin, _RoomSheenRight, _RoomSheenUp, _RoomSheenBand;

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
                float3 world : TEXCOORD1;
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
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            float3 Decode(float3 c)
            {
                float3 low = c / 12.92;
                float3 high = pow((c + 0.055) / 1.055, 2.4);
                return lerp(high, low, step(c, 0.04045));
            }

            // A rounded box's signed distance, its corners circles or squircles (|x|^4 + |y|^4 = r^4): negative inside.
            float Shape(float2 p, float2 extent, float radius, float squircle)
            {
                float2 q = abs(p) - extent + radius;
                float2 m = max(q, 0.0);
                float corner = squircle > 0.5 ? pow(pow(m.x, 4.0) + pow(m.y, 4.0), 0.25) : length(m);
                return corner + min(max(q.x, q.y), 0.0) - radius;
            }

            float Erf(float x)
            {
                float x2 = x * x;
                float t = exp(-x2 * (1.2732395 + 0.147 * x2) / (1.0 + 0.147 * x2));
                return sign(x) * sqrt(1.0 - t);
            }

            float Blurred(float d, float blur)
            {
                float sigma = max(blur * 0.5, 1e-3);
                return 0.5 * (1.0 - Erf(d / (sigma * 1.4142136)));
            }

            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 panel = UNITY_ACCESS_INSTANCED_PROP(Props, _Panel);
                float4 area = UNITY_ACCESS_INSTANCED_PROP(Props, _Quad);
                float4 fillTop = UNITY_ACCESS_INSTANCED_PROP(Props, _FillTop);
                float4 fillFoot = UNITY_ACCESS_INSTANCED_PROP(Props, _FillFoot);
                float4 edgeTop = UNITY_ACCESS_INSTANCED_PROP(Props, _EdgeTop);
                float4 edgeFoot = UNITY_ACCESS_INSTANCED_PROP(Props, _EdgeFoot);
                float4 shade = UNITY_ACCESS_INSTANCED_PROP(Props, _Shade);
                float4 inner = UNITY_ACCESS_INSTANCED_PROP(Props, _Inner);
                float4 innerColour = UNITY_ACCESS_INSTANCED_PROP(Props, _InnerColour);

                float2 p = area.xy + i.uv * area.zw;
                float aa = max(max(fwidth(p.x), fwidth(p.y)), 1e-3);
                float2 extent = panel.xy * 0.5;
                float radius = min(panel.z, min(extent.x, extent.y));

                float outer = Shape(p, extent, radius, panel.w);
                float cover = saturate(0.5 - outer / aa);
                float tf = saturate((extent.y - p.y) / max(2.0 * extent.y, 1e-4));
                float3 face = lerp(fillTop.rgb, fillFoot.rgb, tf);
                // The face's own alpha (the frost's, 1 while it is opaque), top to foot.
                float faceAlpha = lerp(fillTop.a, fillFoot.a, tf);
                // A lighter inner bar across the panel, its inset from the sides its own (the one sentence's, at the panel's foot).
                if (inner.w > 0.0)
                {
                    float barTop = extent.y - inner.x;
                    float2 barCentre = float2(0, barTop - inner.y * 0.5);
                    float2 barExtent = float2(max(extent.x - inner.w, 0.0), inner.y * 0.5);
                    float barD = Shape(p - barCentre, barExtent, min(inner.z, min(barExtent.x, barExtent.y)), panel.w);
                    float barCover = saturate(0.5 - barD / aa);
                    face = lerp(face, innerColour.rgb, innerColour.a * barCover);
                }
                // The edge: a line of its own colour just inside the outline.
                float edge = edgeFoot.a > 0.5 ? cover * saturate(0.5 + (outer + edgeTop.a) / aa) : 0.0;
                face = lerp(face, lerp(edgeTop.rgb, edgeFoot.rgb, tf), edge);
                // The sheen: one band tilted 20 degrees from vertical, in the plane's metres, white at its strength.
                float3 rel = i.world - _RoomSheenOrigin.xyz;
                float across = dot(rel, _RoomSheenRight.xyz) * 0.9396926 - dot(rel, _RoomSheenUp.xyz) * 0.3420201;
                float offset = (across - _RoomSheenBand.x) / max(_RoomSheenBand.y, 1e-4);
                float band = abs(offset) < 0.5 ? cos(offset * 3.14159265) : 0.0;
                face = lerp(face, float3(1, 1, 1), _RoomSheenBand.z * band * band);
                // Premultiplied, so a translucent frost composites over passthrough as it is (BUILD-SPEC-v4 1.2's device note).
                float4 faceOut = float4(Decode(face) * faceAlpha, faceAlpha);

                float4 outside = float4(0, 0, 0, 0);
                if (shade.w > 0.5)
                {
                    float s = shade.z * Blurred(Shape(p + float2(0, shade.x), extent, radius, panel.w), shade.y);
                    outside = float4(0, 0, 0, s);
                }
                return faceOut * cover + outside * (1.0 - cover);
            }
            ENDCG
        }
    }
}
