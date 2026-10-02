// Every flat shape of the interface (ADR 0023): a rounded rectangle, from a sharp corner to a
// pill, with a fill, an edge inside its outline that can be solid or dashed, and a halftone of dots
// for a state that is only the last one known, and, for the menu's glass (ADR 0026), a light from the
// top edge fading out and a sheen just inside it. Edges are anti-aliased in the shader, so shapes stay
// smooth at any size without MSAA. Surface.cs sets the properties per renderer; they are instanced,
// so shapes of one material draw together. Colours arrive linear, as Surface.cs converts them.
Shader "Halcyonic/Glaze Surface"
{
    Properties
    {
        _Fill ("Fill, straight alpha", Color) = (1, 1, 1, 1)
        _Edge ("Edge, straight alpha", Color) = (0, 0, 0, 0)
        _Shape ("Width, height, corner radius and edge width", Vector) = (1, 1, 0.1, 0)
        _Pattern ("Dash period and duty, halftone pitch and dot radius", Vector) = (0, 0, 0, 0)
        _Glass ("Glow opacity and reach, sheen opacity and width", Vector) = (0, 0, 0, 0)
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

            #define HALF_PI 1.5707963

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Fill)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Edge)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Shape)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Pattern)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Glass)
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

            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 fill = UNITY_ACCESS_INSTANCED_PROP(Props, _Fill);
                float4 edge = UNITY_ACCESS_INSTANCED_PROP(Props, _Edge);
                float4 shape = UNITY_ACCESS_INSTANCED_PROP(Props, _Shape);
                float4 pattern = UNITY_ACCESS_INSTANCED_PROP(Props, _Pattern);
                float4 glass = UNITY_ACCESS_INSTANCED_PROP(Props, _Glass);

                // The signed distance to the outline in the shape's own units, negative inside.
                float2 size = max(shape.xy, 1e-5);
                float radius = clamp(shape.z, 0.0, 0.5 * min(size.x, size.y));
                float2 p = (i.uv - 0.5) * size;
                float2 a = abs(p);
                float2 inner = 0.5 * size - radius;
                float2 q = a - inner;
                float d = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;

                // Where along the outline a point lies, from the top's middle around to the side's,
                // the same in each quarter, so dashes meet evenly at both axes.
                float theta = atan2(q.y, max(q.x, 1e-6));
                float along = a.x <= inner.x ? a.x
                    : (a.y <= inner.y ? inner.x + radius * HALF_PI + (inner.y - a.y) : inner.x + radius * (HALF_PI - theta));
                float quarter = inner.x + inner.y + radius * HALF_PI;

                // A halftone of dots, pitch and radius in the shape's units.
                float pitch = max(pattern.z, 1e-4);
                float2 cell = (frac(p / pitch + 0.5) - 0.5) * pitch;
                float dotDistance = length(cell) - pattern.w;

                // The glass: down from the top edge, a light fading out by its reach, and a sheen in a
                // band its width deep, just inside the hairline, along the top's straight run.
                float fromTop = 0.5 * size.y - p.y;

                // Derivatives here, outside any branch, where they are defined.
                float pixel = max(fwidth(d), 1e-6);
                float topPixel = max(fwidth(fromTop), 1e-6);
                float xPixel = max(fwidth(p.x), 1e-6);
                float alongPixel = max(fwidth(along), 1e-6);
                float dotPixel = max(fwidth(dotDistance), 1e-6);

                float cover = saturate(0.5 - d / pixel);
                float fillAlpha = fill.a * lerp(1.0, saturate(0.5 - dotDistance / dotPixel), step(1e-5, pattern.z));

                // The edge: a band inside the outline, its width shape.w, cut into dashes when asked.
                float band = step(1e-6, shape.w) * saturate(0.5 + (d + shape.w) / pixel);
                float count = max(1.0, round(quarter / max(pattern.x, 1e-4)));
                float period = quarter / count;
                float t = frac(along / period + 0.5 * pattern.y) * period;
                float dash = saturate(0.5 - (abs(t - 0.5 * pattern.y * period) - 0.5 * pattern.y * period) / alongPixel);
                float edgeMask = band * lerp(1.0, dash, step(1e-5, pattern.x));

                float reach = saturate(fromTop / max(glass.y, 1e-5));
                float glow = glass.x * step(1e-6, glass.y) * (1.0 - reach * reach * (3.0 - 2.0 * reach));
                float sheenTop = 1.15 * glass.w;
                float sheenBand = saturate(0.5 + (fromTop - sheenTop) / topPixel) * saturate(0.5 - (fromTop - sheenTop - glass.w) / topPixel);
                float sheenRun = saturate(0.5 - (a.x - inner.x) / xPixel);
                float sheen = glass.z * step(1e-6, glass.w) * sheenBand * sheenRun;

                // The fill, the glow over it, the edge, then the sheen, each in premultiplied alpha.
                float3 color = fill.rgb * fillAlpha;
                float alpha = fillAlpha;
                color = color * (1.0 - glow) + glow;
                alpha = alpha * (1.0 - glow) + glow;
                float edgeAlpha = edge.a * edgeMask;
                color = color * (1.0 - edgeAlpha) + edge.rgb * edgeAlpha;
                alpha = alpha * (1.0 - edgeAlpha) + edgeAlpha;
                color = color * (1.0 - sheen) + sheen;
                alpha = alpha * (1.0 - sheen) + sheen;
                return float4(color, alpha) * cover;
            }
            ENDCG
        }
    }
}
