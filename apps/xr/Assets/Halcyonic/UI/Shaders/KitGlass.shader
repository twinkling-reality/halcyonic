// The room's one panel material, the art direction's quiet glass (handoff 2026-10-30, section 1; ADR 0040): an opaque face
// lit from above that carries every word, a see-through rim with no words, a light along the outer edge, a faint glow, two
// soft shadows, and on a moment its state edge and the bloom of its arrival. One pass, no textures, no blur: every layer is
// a signed distance or a Gaussian's closed form. KitGlass.cs sets each panel's values, instanced; the material-wide values
// come once from KitGlassMaterial, whose formulas this repeats. Lengths are in dp at the eye; colours arrive as sRGB.
// Layers over the face are mixed in sRGB, as the prototype mixes them, then decoded; layers over the room are solved for
// their reference room (the rim for the lit room, the edge for the dark room) and written premultiplied in linear light.
Shader "Halcyonic/Kit Glass"
{
    Properties
    {
        _Panel ("Width, height, corner radius and rim, in dp", Vector) = (100, 100, 24, 10)
        _Quad ("The quad's left, bottom, width and height, in dp from the panel's centre", Vector) = (-50, -50, 100, 100)
        _Tone ("A moment's tone, sRGB, and 1 where it lights the edge and blooms", Vector) = (0, 0, 0, 0)
        _ToneMid ("The state edge's middle stop, sRGB, and its alpha", Vector) = (0, 0, 0, 0)
        _Light ("Bloom strength, glow on, opacity, reserved", Vector) = (0, 1, 1, 0)

        _N7 ("n7, sRGB", Vector) = (0.855, 0.867, 0.878, 0)
        _N1 ("n1, sRGB", Vector) = (0.129, 0.145, 0.165, 0)
        _FaceTop ("The face's top, sRGB", Vector) = (0.149, 0.165, 0.188, 0)
        _FaceBottom ("The face's foot, sRGB", Vector) = (0.118, 0.133, 0.149, 0)
        _RimStops ("Rim alpha at the top, at its middle stop and at the bottom, and where the middle stop stands", Vector) = (0.20, 0.07, 0.42, 0.28)
        _EdgeStops ("Edge alpha at the top, at its middle stop and below, and where the middle stop stands", Vector) = (0.78, 0.46, 0.40, 0.26)
        _LitRoom ("The rim's reference room, sRGB", Vector) = (0.725, 0.682, 0.620, 0)
        _DarkRoom ("The edge's reference room, sRGB", Vector) = (0.118, 0.110, 0.102, 0)
        _Shadow ("Far shadow drop, blur and alpha, and the linear fit's exponent", Vector) = (12, 34, 0.34, 2.2)
        _Contact ("Near shadow drop, blur and alpha", Vector) = (2, 6, 0.22, 0)
        _Glow ("Glow blur and alpha, and the linear fit's scale and power", Vector) = (18, 0.10, 0.390, 1.21)
        _Lights ("Edge width, highlight width and alpha, bloom reach", Vector) = (2, 1, 0.06, 0.70)
        _BloomPeak ("The bloom's peak alpha", Float) = 0.11
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
                UNITY_DEFINE_INSTANCED_PROP(float4, _Tone)
                UNITY_DEFINE_INSTANCED_PROP(float4, _ToneMid)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Light)
            UNITY_INSTANCING_BUFFER_END(Props)

            float4 _N7, _N1, _FaceTop, _FaceBottom, _RimStops, _EdgeStops, _LitRoom, _DarkRoom, _Shadow, _Contact, _Glow, _Lights;
            float _BloomPeak;

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

            float3 Decode(float3 c)
            {
                float3 low = c / 12.92;
                float3 high = pow((c + 0.055) / 1.055, 2.4);
                return lerp(high, low, step(c, 0.04045));
            }

            // A rounded box's signed distance: negative inside.
            float Box(float2 p, float2 extent, float radius)
            {
                float2 q = abs(p) - extent + radius;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;
            }

            // The error function, to about 1e-4 (Winitzki's approximation).
            float Erf(float x)
            {
                float x2 = x * x;
                float t = exp(-x2 * (1.2732395 + 0.147 * x2) / (1.0 + 0.147 * x2));
                return sign(x) * sqrt(1.0 - t);
            }

            // How much of a shape blurred by a CSS blur of `blur` covers a point `d` outside its edge: blur / 2 is the sigma.
            float Blurred(float d, float blur)
            {
                float sigma = max(blur * 0.5, 1e-3);
                return 0.5 * (1.0 - Erf(d / (sigma * 1.4142136)));
            }

            // Three stops at 0, `middle` and 1 of one colour each, interpolated premultiplied as a browser does.
            float4 Stops(float t, float middle, float3 c0, float a0, float3 c1, float a1, float3 c2, float a2)
            {
                float4 p0 = float4(c0 * a0, a0), p1 = float4(c1 * a1, a1), p2 = float4(c2 * a2, a2);
                return t <= middle ? lerp(p0, p1, saturate(t / max(middle, 1e-4))) : lerp(p1, p2, saturate((t - middle) / max(1.0 - middle, 1e-4)));
            }

            // The state edge: its tone at the top, the middle stop at 30 percent, the neutral edge from 62 percent.
            float4 StateEdge(float t, float3 tone, float4 middle, float3 n7, float side)
            {
                float4 top = float4(tone, 1.0), mid = float4(middle.rgb * middle.a, middle.a), below = float4(n7 * side, side);
                if (t <= 0.30) return lerp(top, mid, t / 0.30);
                if (t <= 0.62) return lerp(mid, below, (t - 0.30) / 0.32);
                return below;
            }

            // A premultiplied sRGB layer as Unity must draw it over `ground` (sRGB) so its blend in linear light lands where the
            // prototype's sRGB blend does (KitGlassMaterial.Solve): the alpha fitted across the channels, the colour nudged where
            // `nudge` (the edge), kept where not (the rim, so it casts no tint over other rooms).
            float4 Solve(float4 layer, float3 ground, bool nudge)
            {
                float a = layer.a;
                if (a <= 1e-5) return float4(0, 0, 0, 0);
                float3 colour = layer.rgb / a;
                float3 g = Decode(ground);
                float3 target = Decode(layer.rgb + (1.0 - a) * ground);
                float3 span = Decode(colour) - g;
                float length2 = dot(span, span);
                float fitted = length2 < 1e-12 ? a : saturate(dot(target - g, span) / length2);
                float3 linearColour = nudge && fitted > 1e-6 ? saturate((target - (1.0 - fitted) * g) / fitted) : Decode(colour);
                return float4(linearColour * fitted, fitted);
            }

            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 panel = UNITY_ACCESS_INSTANCED_PROP(Props, _Panel);
                float4 area = UNITY_ACCESS_INSTANCED_PROP(Props, _Quad);
                float4 tone = UNITY_ACCESS_INSTANCED_PROP(Props, _Tone);
                float4 toneMid = UNITY_ACCESS_INSTANCED_PROP(Props, _ToneMid);
                float4 light = UNITY_ACCESS_INSTANCED_PROP(Props, _Light);

                float2 p = area.xy + i.uv * area.zw;
                float aa = max(max(fwidth(p.x), fwidth(p.y)), 1e-3);
                float2 extent = panel.xy * 0.5;
                float radius = min(panel.z, min(extent.x, extent.y));
                float rim = panel.w;
                float2 faceHalf = max(extent - rim, 1e-3);
                float faceRadius = max(radius - rim, 0.0);

                float outer = Box(p, extent, radius);
                float inner = Box(p, faceHalf, faceRadius);
                float outerCover = saturate(0.5 - outer / aa);
                float faceCover = saturate(0.5 - inner / aa);
                bool toned = tone.a > 0.5;

                // The face, opaque: lit from above, a highlight just inside its top edge, a moment's bloom; mixed in sRGB.
                float tf = saturate((faceHalf.y - p.y) / (2.0 * faceHalf.y));
                float3 face = Stops(tf, 0.36, _FaceTop.rgb, 1, _N1.rgb, 1, _FaceBottom.rgb, 1).rgb;
                float highlight = faceCover * saturate(0.5 + Box(p + float2(0, _Lights.y), faceHalf, faceRadius) / aa);
                face = lerp(face, float3(1, 1, 1), _Lights.z * highlight);
                if (toned) face = lerp(face, tone.rgb, _BloomPeak * light.x * max(0.0, 1.0 - tf / _Lights.w));
                float4 faceOut = float4(Decode(face), 1.0);

                // The rim, see-through, and over its outer 2 dp the edge light; the two draw as one layer there.
                float t = saturate((extent.y - p.y) / (2.0 * extent.y));
                float4 rimLayer = Stops(t, _RimStops.w, _N7.rgb, _RimStops.x, _N7.rgb, _RimStops.y, _N1.rgb, _RimStops.z);
                float4 edge = toned ? StateEdge(t, tone.rgb, toneMid, _N7.rgb, _EdgeStops.z)
                                    : Stops(t, _EdgeStops.w, _N7.rgb, _EdgeStops.x, _N7.rgb, _EdgeStops.y, _N7.rgb, _EdgeStops.z);
                float edgeCover = saturate(0.5 + (outer + _Lights.x) / aa);
                float4 ring = edge * edgeCover + rimLayer * (1.0 - edge.a * edgeCover);
                float4 ringOut = lerp(Solve(rimLayer, _LitRoom.rgb, false), Solve(ring, _DarkRoom.rgb, true), edgeCover);

                // Outside: the far and near shadows, and the glow round the edge, each fitted to linear light.
                float farShade = _Shadow.z * Blurred(Box(p + float2(0, _Shadow.x), extent, radius), _Shadow.y);
                float nearShade = _Contact.z * Blurred(Box(p + float2(0, _Contact.x), extent, radius), _Contact.y);
                float shade = 1.0 - pow(1.0 - farShade, _Shadow.w) * pow(1.0 - nearShade, _Shadow.w);
                float glowAlpha = light.y > 0.5 ? _Glow.y * Blurred(outer, _Glow.x) : 0.0;
                float glow = glowAlpha > 1e-6 ? _Glow.z * pow(glowAlpha, _Glow.w) : 0.0;
                float4 outside = float4(Decode(_N7.rgb) * glow, glow + (1.0 - glow) * shade);

                float4 result = faceOut * faceCover + ringOut * (outerCover - faceCover) + outside * (1.0 - outerCover);
                return result * light.z;
            }
            ENDCG
        }
    }
}
