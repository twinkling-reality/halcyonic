// One of DESIGN's glyphs (BUILD-SPEC-v4 1.4's table), drawn from its geometry: a 24-unit grid, y down, strokes 2.2 units
// wide with round caps and joins, and no fill unless the glyph names one. Every shape is a line, an arc, a circle, a
// rounded rectangle's outline or a filled rounded rectangle, each a signed distance here; a glyph is at most ten of them,
// and what is nearer than half the stroke to any of them is ink. One quad the glyph's own box, no texture, one colour.
// RoomGlyphs.cs holds every glyph's shapes and sets them; the colour arrives as sRGB and is decoded here.
Shader "Halcyonic/Room Glyph"
{
    Properties
    {
        _Colour ("The glyph's colour, sRGB, and its alpha", Vector) = (1, 1, 1, 1)
        _Stroke ("The stroke's width in units, and how many shapes the glyph has", Vector) = (2.2, 0, 0, 0)
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

            float4 _Colour;
            float4 _Stroke;

            // Each shape, two vectors. _ShapeB.x says which:
            //   1 a line: A = its two ends (x, y, x, y);
            //   2 an arc: A = its centre, its radius and the sine of half its sweep; B = (2, the unit direction of its
            //     middle, the cosine of half its sweep); half a sweep of 180 degrees is a whole circle;
            //   3 a rounded rectangle's outline, 4 a filled one: A = its centre and its half size; B.y = its corner.
            float4 _ShapeA[10];
            float4 _ShapeB[10];

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

            float Line(float2 p, float2 a, float2 b)
            {
                float2 e = b - a;
                float2 w = p - a;
                return length(w - e * saturate(dot(w, e) / max(dot(e, e), 1e-6)));
            }

            float Arc(float2 p, float2 centre, float radius, float2 middle, float sinHalf, float cosHalf)
            {
                float2 q = p - centre;
                float along = dot(q, middle);
                float across = abs(q.x * middle.y - q.y * middle.x);
                if (cosHalf * across > sinHalf * along) return length(float2(across, along) - float2(sinHalf, cosHalf) * radius);
                return abs(length(q) - radius);
            }

            float Box(float2 p, float2 centre, float2 extent, float radius)
            {
                float2 q = abs(p - centre) - extent + radius;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;
            }

            float4 frag(v2f i) : SV_Target
            {
                // This pixel on the 24-unit grid, y down, as the glyphs are given.
                float2 p = float2(i.uv.x, 1.0 - i.uv.y) * 24.0;
                float aa = max(max(fwidth(p.x), fwidth(p.y)), 1e-3);
                float nearest = 1e3;
                float filled = 1e3;
                int count = (int)_Stroke.y;
                for (int index = 0; index < 10; index++)
                {
                    if (index >= count) break;
                    float4 a = _ShapeA[index];
                    float4 b = _ShapeB[index];
                    if (b.x < 1.5) nearest = min(nearest, Line(p, a.xy, a.zw));
                    else if (b.x < 2.5) nearest = min(nearest, Arc(p, a.xy, a.z, b.yz, a.w, b.w));
                    else if (b.x < 3.5) nearest = min(nearest, abs(Box(p, a.xy, a.zw, b.y)));
                    else filled = min(filled, Box(p, a.xy, a.zw, b.y));
                }
                float ink = max(saturate(0.5 - (nearest - _Stroke.x * 0.5) / aa), saturate(0.5 - filled / aa));
                float alpha = _Colour.a * ink;
                return float4(Decode(_Colour.rgb) * alpha, alpha);
            }
            ENDCG
        }
    }
}
