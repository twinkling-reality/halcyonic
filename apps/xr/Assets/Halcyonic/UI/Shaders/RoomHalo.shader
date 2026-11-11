// The redesign's action (DESIGN's BUILD-SPEC-v4 1.4, FINISH-2 and FINISH-3 2026-11-05): no box and no edge, ever. A faint
// halo of the panel's foot tint #343A3F round the action's core, solid inside, half at its edge, gone 12 dp out (alpha A x (1 - smoothstep(-12, 12, d))
// on the core's 24 dp squircle); a small squircle chip at the core's left that holds the glyph; and, on Confirm while its
// lock fills, a 2 dp ring whole outside the chip, filling clockwise from 12 o'clock. One quad, signed distances only. The values
// arrive linear (the spec's linear-space alphas); colours as sRGB, decoded here. RoomButton.cs sets each action's values.
Shader "Halcyonic/Room Halo"
{
    Properties
    {
        _Core ("The core's width, height and corner in dp, and the halo's alpha", Vector) = (120, 48, 24, 0.81)
        _Quad ("The quad's left, bottom, width and height, in dp from the core's centre", Vector) = (-72, -36, 144, 72)
        _Chip ("The chip's centre x and y, its size and corner, in dp", Vector) = (-40, 0, 32, 12)
        _ChipColour ("The chip's colour, sRGB, and its alpha", Vector) = (0.29, 0.32, 0.345, 1)
        _HaloColour ("The backing's colour, sRGB, and how far its edge feathers each way in dp", Vector) = (0.204, 0.227, 0.247, 6)
        _Ring ("The ring's radius and width in dp, its fill 0 to 1, and 1 where it shows", Vector) = (19, 2, 0, 0)
        _Mark ("An answer's mark at the chip's centre: its size and corner in dp, its line's width, and the line's alpha", Vector) = (22, 11, 2, 0)
        _Fade ("The content's opacity: 1, or 0.45 while disabled", Float) = 1
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
                UNITY_DEFINE_INSTANCED_PROP(float4, _Core)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Quad)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Chip)
                UNITY_DEFINE_INSTANCED_PROP(float4, _ChipColour)
                UNITY_DEFINE_INSTANCED_PROP(float4, _HaloColour)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Ring)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Mark)
                UNITY_DEFINE_INSTANCED_PROP(float, _Fade)
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

            float3 Decode(float3 c)
            {
                float3 low = c / 12.92;
                float3 high = pow((c + 0.055) / 1.055, 2.4);
                return lerp(high, low, step(c, 0.04045));
            }

            // A squircle-cornered box's signed distance (|x|^4 + |y|^4 = r^4 at the corners): negative inside.
            float Squircle(float2 p, float2 extent, float radius)
            {
                float2 q = abs(p) - extent + radius;
                float2 m = max(q, 0.0);
                return pow(pow(m.x, 4.0) + pow(m.y, 4.0), 0.25) + min(max(q.x, q.y), 0.0) - radius;
            }

            // A box with squircle corners, or a circle where its corner is half its size.
            float Rounded(float2 p, float size, float corner)
            {
                return corner >= size * 0.5 - 0.01 ? length(p) - size * 0.5 : Squircle(p, float2(size, size) * 0.5, corner);
            }

            // Source over, premultiplied.
            float4 Over(float4 under, float3 colour, float alpha)
            {
                return float4(colour * alpha, alpha) + under * (1.0 - alpha);
            }

            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 core = UNITY_ACCESS_INSTANCED_PROP(Props, _Core);
                float4 area = UNITY_ACCESS_INSTANCED_PROP(Props, _Quad);
                float4 chip = UNITY_ACCESS_INSTANCED_PROP(Props, _Chip);
                float4 chipColour = UNITY_ACCESS_INSTANCED_PROP(Props, _ChipColour);
                float4 ring = UNITY_ACCESS_INSTANCED_PROP(Props, _Ring);
                float fade = UNITY_ACCESS_INSTANCED_PROP(Props, _Fade);

                float2 p = area.xy + i.uv * area.zw;
                float aa = max(max(fwidth(p.x), fwidth(p.y)), 1e-3);
                float4 result = float4(0, 0, 0, 0);

                // The halo: #343A3F, solid inside the core, half at its edge, gone 12 dp out.
                if (core.w > 0.0)
                {
                    float2 extent = core.xy * 0.5;
                    float d = Squircle(p, extent, min(core.z, min(extent.x, extent.y)));
                    float4 haloColour = UNITY_ACCESS_INSTANCED_PROP(Props, _HaloColour);
                    float feather = max(haloColour.a, 0.5);
                    float halo = core.w * (1.0 - smoothstep(-feather, feather, d));
                    result = Over(result, Decode(haloColour.rgb), halo);
                }

                // The chip, its glyph drawn over it by the words' layer.
                float2 c = p - chip.xy;
                float chipD = Rounded(c, chip.z, min(chip.w, chip.z * 0.5));
                float chipCover = saturate(0.5 - chipD / aa);
                result = Over(result, Decode(chipColour.rgb), chipColour.a * chipCover * fade);

                // An answer's mark not chosen: the outline alone, a circle for one of several, a squircle box for any number.
                float4 mark = UNITY_ACCESS_INSTANCED_PROP(Props, _Mark);
                if (mark.w > 0.0)
                {
                    float markD = Rounded(c, mark.x, min(mark.y, mark.x * 0.5));
                    float stroke = saturate(0.5 - (abs(markD + mark.z * 0.5) - mark.z * 0.5) / aa);
                    result = Over(result, float3(1, 1, 1), mark.w * stroke * fade);
                }

                // Confirm's lock: a ring whole outside the chip, its track white at 34 percent, filled in white clockwise from 12 o'clock.
                if (ring.w > 0.5)
                {
                    float r = length(c);
                    float band = saturate(0.5 - (abs(r - ring.x) - ring.y * 0.5) / aa);
                    float angle = atan2(c.x, c.y);
                    float turn = frac(angle / 6.2831853 + 1.0);
                    float filled = turn <= ring.z ? 1.0 : 0.0;
                    result = Over(result, float3(1, 1, 1), 0.34 * 0.5 * band * fade);
                    // The ring fills in white, apart from the accent of the chip it stands round.
                    result = Over(result, float3(1, 1, 1), filled * band * fade);
                }
                return result;
            }
            ENDCG
        }
    }
}
