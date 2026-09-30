// The body of a workstream character: glossy plastic with two painted eyes. While work runs the
// orb's satin flow moves across the surface; a failed character cracks; fog covers a character
// whose state is unknown; and the last known state ghosts into a halftone of dots.
//
// Cost on a Meta Quest 3: one pass, no keywords, about 150 arithmetic operations and at most four
// texture reads per pixel. The noise comes from a small tileable texture that CharacterNoise bakes
// at startup, instead of the per-pixel fractal noise of the lookbook it ports, which costs about
// fifteen times more (docs/internal/architecture/XR_CLIENT.md).
//
// Object space is in body units: the body is about one unit in radius and faces +z, the person.
// The per-character values come from a MaterialPropertyBlock that CharacterView sets every frame;
// _HalcyonicNoise and _HalcyonicKeyLight are globals.
Shader "Halcyonic/Character Body"
{
    Properties
    {
        _BodyColor ("Body color, sRGB", Vector) = (0.36, 0.61, 0.96, 1)
        _Clock ("Character clock in seconds", Float) = 0
        _Flow ("Satin flow", Range(0, 1)) = 0
        _FlowSpeed ("Flow speed", Float) = 0.55
        _Crack ("Cracks", Range(0, 1)) = 0
        _Fog ("Fog", Range(0, 1)) = 0
        _Saturation ("Saturation", Range(0, 1)) = 1
        _Ghost ("Halftone ghost", Range(0, 1)) = 0
        _EyeKind ("Eyes: 0 open, 1 closed, 2 crossed, 3 flat", Float) = 0
        _EyeOpen ("Eye opening", Float) = 1
        _EyeInk ("Eye opacity", Range(0, 1)) = 1
        _Look ("Gaze offset", Vector) = (0, 0, 0, 0)
        _EyeLayout ("Eye spacing, height and size", Vector) = (0.3, 0.12, 1, 0)
    }

    SubShader
    {
        // Transparent so the silhouette and the halftone can have soft edges without MSAA; the body
        // still writes depth, so the ring behind it is hidden and hands in front of it sort.
        Tags { "Queue" = "Transparent-10" "RenderType" = "Transparent" "IgnoreProjector" = "True" "ForceNoShadowCasting" = "True" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _HalcyonicNoise;
            float4 _HalcyonicKeyLight;

            float4 _BodyColor;
            float _Clock;
            float _Flow;
            float _FlowSpeed;
            float _Crack;
            float _Fog;
            float _Saturation;
            float _Ghost;
            float _EyeKind;
            float _EyeOpen;
            float _EyeInk;
            float4 _Look;
            float4 _EyeLayout;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 body : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float3 toEye : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.pos = UnityWorldToClipPos(world);
                o.body = v.vertex.xyz;
                o.normal = UnityObjectToWorldNormal(v.normal);
                // Per eye: computed here, where the stereo eye index is set up.
                o.toEye = _WorldSpaceCameraPos.xyz - world;
                return o;
            }

            // Explicit level of detail: implicit derivatives are undefined inside the effect branches.
            // The noise is magnified on screen, so its top level is the right one.
            float4 Noise(float2 uv)
            {
                return tex2Dlod(_HalcyonicNoise, float4(uv, 0.0, 0.0));
            }

            float Segment(float2 p, float2 a, float2 b)
            {
                float2 pa = p - a;
                float2 ba = b - a;
                float h = saturate(dot(pa, ba) / dot(ba, ba));
                return length(pa - ba * h);
            }

            // Distance to one eye's ink, in eye units around the eye's center.
            float EyeDistance(float2 p)
            {
                if (_EyeKind < 0.5)
                {
                    // Open: a rounded upright capsule.
                    return Segment(p, float2(0.0, -0.05), float2(0.0, 0.05)) - 0.085;
                }
                if (_EyeKind < 1.5)
                {
                    // Closed: a shallow arc that sags like a lid at rest, the bottom of a circle of
                    // radius 0.11 whose center sits 0.07 above the eye, 55 degrees to each side.
                    float2 q = float2(abs(p.x), 0.07 - p.y);
                    float2 edge = float2(0.819, 0.574);
                    float d = edge.y * q.x > edge.x * q.y ? length(q - edge * 0.11) : abs(length(q) - 0.11);
                    return d - 0.021;
                }
                if (_EyeKind < 2.5)
                {
                    // Crossed out.
                    return min(
                        Segment(p, float2(-0.07, -0.07), float2(0.07, 0.07)),
                        Segment(p, float2(-0.07, 0.07), float2(0.07, -0.07))) - 0.021;
                }
                // Flat.
                return Segment(p, float2(-0.075, 0.0), float2(0.075, 0.0)) - 0.02;
            }

            float Coverage(float d)
            {
                return saturate(0.5 - d / max(fwidth(d), 1e-5));
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.normal);
                float3 v = normalize(i.toEye);
                float3 l = _HalcyonicKeyLight.xyz;
                float nv = saturate(dot(n, v));
                float3 tint = _BodyColor.rgb;

                // Wrapped diffuse from the key light, a little lighter on top.
                float wrap = saturate((dot(n, l) + 0.45) / 1.45);
                float3 color = tint * (0.52 + 0.58 * wrap) * lerp(0.86, 1.08, n.y * 0.5 + 0.5);

                // The orb's satin flow: thin bright bands over a warped noise field that drifts across
                // the body.
                if (_Flow > 0.001)
                {
                    float t = _Clock * _FlowSpeed;
                    float2 p = i.body.xy * 0.3;
                    // The noise tiles, so the drift wraps without a seam and stays precise in long sessions.
                    float2 warp = Noise(p * 0.8 + frac(float2(0.031, 0.017) * t)).rg - 0.5;
                    float f = Noise(p + warp * 0.8 + frac(float2(-0.021, 0.029) * t)).b;
                    float band = smoothstep(0.6, 1.0, sin(f * 16.0 + t * 2.0) * 0.5 + 0.5);
                    color = lerp(color, color * 1.3 + 0.12, band * 0.6 * _Flow);
                }

                // Fog drifting over a character whose state is unknown.
                if (_Fog > 0.001)
                {
                    float2 p = i.body.xy * 0.32;
                    float fog = Noise(p + frac(float2(0.02, 0.008) * _Clock)).b * 0.65
                        + Noise(p * 2.1 + frac(float2(-0.012, 0.017) * _Clock)).b * 0.35;
                    color = lerp(color, float3(0.78, 0.80, 0.84), smoothstep(0.3, 0.75, fog) * 0.85 * _Fog);
                }

                // Cracks along the edges of a baked Voronoi pattern.
                if (_Crack > 0.001)
                {
                    float edge = Noise(i.body.xy * 0.3 + float2(0.37, 0.61)).a;
                    color = lerp(color, tint * 0.18, (1.0 - smoothstep(0.02, 0.14, edge)) * _Crack);
                }

                // Gloss: a sharp and a broad highlight and a rim.
                float3 h = normalize(l + v);
                float nh = saturate(dot(n, h));
                float rim = 1.0 - nv;
                color += pow(nh, 70.0) * 0.95 + pow(nh, 10.0) * 0.12;
                color += rim * rim * rim * lerp(tint, float3(1.0, 1.0, 1.0), 0.6) * 0.45;

                // Two eyes on the face, over the gloss so no highlight ever washes them out.
                float2 face = i.body.xy - _Look.xy * float2(0.07, 0.055) - float2(0.0, _EyeLayout.y);
                float2 squash = float2(1.0, 1.0 / max(_EyeOpen, 0.08)) / _EyeLayout.z;
                float2 left = (face + float2(_EyeLayout.x, 0.0)) * squash;
                float2 right = (face - float2(_EyeLayout.x, 0.0)) * squash;
                float front = saturate(i.body.z * 8.0);
                float ink = Coverage(min(EyeDistance(left), EyeDistance(right))) * front * _EyeInk;
                float2 glintAt = float2(0.028, 0.075);
                float glint = Coverage(min(length(left - glintAt), length(right - glintAt)) - 0.032)
                    * step(_EyeKind, 0.5) * front * _EyeInk;
                color = lerp(color, float3(0.09, 0.10, 0.13), ink);
                color = lerp(color, float3(1.0, 1.0, 1.0), glint);

                float grey = dot(color, float3(0.299, 0.587, 0.114));
                color = lerp(float3(grey, grey, grey), color, _Saturation);

                // A soft silhouette, about a pixel wide, in place of multisampling.
                float alpha = saturate(nv / max(fwidth(nv) * 1.5, 1e-5));

                // The halftone ghost: dots fixed to the body, so both eyes see the same pattern. Always
                // computed, because derivatives are only defined outside branches.
                float2 cell = frac(i.body.xy / 0.09) - 0.5;
                float radius = lerp(0.75, 0.16 + 0.34 * saturate(grey), _Ghost);
                alpha *= lerp(1.0, Coverage(length(cell) - radius) * 0.85, _Ghost);
                // The eyes stay whole, so a ghost still shows the state it was last known in.
                alpha = max(alpha, saturate(ink + glint) * 0.9);
                clip(alpha - 0.004);

                color = max(color, 0.0);
            #ifndef UNITY_COLORSPACE_GAMMA
                color = GammaToLinearSpace(color);
            #endif
                return float4(color, alpha);
            }
            ENDCG
        }
    }
}
