Shader "Halcyonic/Kit Figure Light"
{
    Properties
    {
        [PerRendererData] _Tone ("Tone sRGB and level", Vector) = (0.349, 0.812, 0.945, 1)
        [PerRendererData] _GroundParams ("AO scale, AO alpha, ring pulse, scanline mix", Vector) = (1.0, 0.52, 1.0, 1.0)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float kind : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            UNITY_INSTANCING_BUFFER_START(PerMaterial)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Tone)
                UNITY_DEFINE_INSTANCED_PROP(float4, _GroundParams)
            UNITY_INSTANCING_BUFFER_END(PerMaterial)

            float3 DecodeChannel(float3 srgb)
            {
                float3 low = srgb / 12.92;
                float3 high = pow((srgb + 0.055) / 1.055, 2.4);
                return lerp(low, high, step(0.04045, srgb));
            }

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.kind = v.color.r * 2.0;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 tone = UNITY_ACCESS_INSTANCED_PROP(PerMaterial, _Tone);
                float4 groundParams = UNITY_ACCESS_INSTANCED_PROP(PerMaterial, _GroundParams);

                // Kind 0: Tilted oval LED eye or happy arc with 7 horizontal scanline bands.
                // The centre (uv = 0.5, 0.5) holds the exact state sRGB tone for pixel-readback verification.
                if (i.kind < 0.5)
                {
                    float2 p = (i.uv - 0.5) * 2.0;
                    float scanWave = 0.5 + 0.5 * cos(p.y * 7.0 * 3.14159265);
                    float scanMask = smoothstep(0.18, 0.52, abs(p.y)) * saturate(groundParams.w);
                    float3 eyeSrgb = tone.rgb * lerp(1.0, 0.74 + 0.26 * scanWave, scanMask);
                    return float4(DecodeChannel(saturate(eyeSrgb)), 1.0);
                }

                // Kind 1: Soft visor LED bloom behind each eye.
                if (i.kind < 1.5)
                {
                    float2 d = (i.uv - 0.5) * 2.0;
                    float r = length(d);
                    float falloff = exp(-r * r * 4.2) * saturate(1.0 - r);
                    float a = saturate(tone.w * falloff * 0.65);
                    return float4(DecodeChannel(tone.rgb) * a, a);
                }

                // Kind 2: 3-layer spatial ground projection evaluated in ROBOT6 prototype units (0.484 proto = 0.184 m across).
                float2 q = (i.uv - 0.5) * 0.484;
                float rFloor = length(q);
                float outerBound = 1.0 - smoothstep(0.212, 0.218, rFloor);
                if (outerBound <= 1e-4) return float4(0.0, 0.0, 0.0, 0.0);

                float aa = max(fwidth(rFloor), 0.0020);
                float aoScale = max(0.5, groundParams.x);
                float aoStrength = saturate(groundParams.y);
                float ringPulse = max(0.2, groundParams.z);
                float lightLevel = saturate(tone.w * ringPulse);

                // Layer 1: Tight ambient-occlusion contact shadow (#05070A, alpha 0.46) directly under the lower torso bullet tip.
                float rAo = rFloor / (0.072 * aoScale);
                float ao = exp(-rAo * rAo * 2.8) * smoothstep(1.0, 0.15, rAo) * aoStrength;

                // Layer 2: Precision concentric inner ring (r = 0.150, width 0.0030) with 55% white core highlight on the front arc,
                // plus broken outer ring (r = 0.198, width 0.0022), 4 cardinal micro-ticks (0, 90, 180, 270 deg), and center origin pip.
                float dInner = abs(rFloor - 0.150);
                float innerRing = 1.0 - smoothstep(0.0018, 0.0018 + aa, dInner);
                float innerHalo = exp(-dInner / 0.0075) * 0.14;
                float frontArc = smoothstep(-0.15, 0.88, -q.y / max(rFloor, 1e-4));

                float dOuter = abs(rFloor - 0.198);
                float2 axy = abs(q);
                float dAxis = min(axy.x, axy.y);
                float angle = atan2(q.y, q.x);
                float diagBreak = smoothstep(-0.65, -0.30, cos(4.0 * angle));
                float cardGap = smoothstep(0.012, 0.020, dAxis);
                float outerRing = (1.0 - smoothstep(0.0014, 0.0014 + aa, dOuter)) * diagBreak * cardGap * 0.62;

                float tickSpan = smoothstep(0.185 - aa, 0.185 + aa, rFloor) * (1.0 - smoothstep(0.211 - aa, 0.211 + aa, rFloor));
                float ticks = (1.0 - smoothstep(0.0016, 0.0016 + aa, dAxis)) * tickSpan * 0.82;

                float pip = (1.0 - smoothstep(0.0014, 0.0014 + aa, abs(rFloor - 0.014))) * 0.30;

                // Layer 3: Compact specular floor reflection strictly inside the inner ring (r <= 0.135), alpha capped <= 0.28.
                float insideMask = 1.0 - smoothstep(0.118, 0.138, rFloor);
                float beltBounce = exp(-pow((rFloor - 0.078) / 0.030, 2.0)) * 0.25;
                float coreBounce = exp(-pow(rFloor / 0.050, 2.0)) * 0.04;
                float specRefl = (beltBounce + coreBounce) * insideMask;

                float ringPattern = innerRing * (0.56 + 0.44 * frontArc) + innerHalo * insideMask + outerRing + ticks + pip;
                float lightRaw = specRefl + ringPattern;
                float lightAlpha = saturate(lightRaw * lightLevel) * outerBound;

                float whiteShare = saturate((innerRing * 0.55 * frontArc + ticks * 0.35) / max(lightRaw, 1e-3));
                float3 lightSrgb = lerp(tone.rgb, float3(1.0, 1.0, 1.0), whiteShare);

                float aoOnly = ao * (1.0 - saturate(lightAlpha * 1.15)) * outerBound;
                float outAlpha = saturate(lightAlpha + aoOnly);

                float3 lightLin = DecodeChannel(saturate(lightSrgb));
                float3 aoLin = DecodeChannel(float3(0.020, 0.027, 0.039));
                float3 outLin = lightLin * lightAlpha + aoLin * aoOnly;

                return float4(outLin, outAlpha);
            }
            ENDCG
        }
    }
}
