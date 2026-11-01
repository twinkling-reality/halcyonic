Shader "Halcyonic/Kit Pearl"
{
    Properties
    {
        [PerRendererData] _Base ("Base sRGB and live scale", Vector) = (0.863, 0.890, 0.925, 2.0)
        [PerRendererData] _Under ("Under sRGB and glow scale", Vector) = (0.239, 0.271, 0.322, 1.0)
        [PerRendererData] _Tone ("State tone sRGB and dash sign", Vector) = (0.310, 0.498, 0.949, 1.0)
        [PerRendererData] _Tint ("Family hue sRGB and sheen share", Vector) = (0.310, 0.498, 0.949, 0.11)
    }
    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }
        Cull Back
        ZWrite On
        ZTest LEqual
        Blend Off

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
                float3 normal : NORMAL;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float4 color : COLOR;
                float2 uv : TEXCOORD2;
                float3 localPos : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            UNITY_INSTANCING_BUFFER_START(PerMaterial)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Base)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Under)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Tone)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Tint)
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
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.color = v.color;
                o.uv = v.uv;
                o.localPos = v.vertex.xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 baseProp = UNITY_ACCESS_INSTANCED_PROP(PerMaterial, _Base);
                float4 underProp = UNITY_ACCESS_INSTANCED_PROP(PerMaterial, _Under);
                float4 toneProp = UNITY_ACCESS_INSTANCED_PROP(PerMaterial, _Tone);
                float4 tintProp = UNITY_ACCESS_INSTANCED_PROP(PerMaterial, _Tint);

                float liveScale = saturate(baseProp.w * 0.5);
                float glowScale = saturate(underProp.w);

                float3 n = normalize(i.worldNormal);
                float3 v = normalize(_WorldSpaceCameraPos.xyz - i.worldPos);

                float3 keyDir = normalize(float3(-0.38, 0.78, -0.50));
                float3 fillDir = normalize(float3(0.58, 0.24, -0.58));
                float3 rimDir = normalize(float3(0.0, 0.55, 0.83));

                float keyDiff = saturate(dot(n, keyDir));
                float fillDiff = saturate(dot(n, fillDir));
                float rimLight = saturate(dot(n, rimDir));
                float upMix = saturate(n.y * 0.5 + 0.5);
                float fresnel = pow(1.0 - saturate(dot(n, v)), 3.0);

                // Clearcoat porcelain shell (#DCE3EC -> #F8FAFD) with 11% Fresnel/subsurface identity sheen.
                float3 crownSrgb = lerp(baseProp.rgb, float3(0.973, 0.980, 0.992), 0.78 * liveScale);
                float3 shellSrgb;
                if (i.color.r < 0.5 || i.color.a > 0.25)
                {
                    // Head dome and capsule-wing arms stay bright pearl-white (>= #CED7E2) across front/side cheeks and lower paddle tips,
                    // darkening only on the hidden underside (n.y < -0.75) that faces into the neck gap.
                    float3 brightFloor = float3(0.816, 0.851, 0.890) * liveScale;
                    float3 midPorcelain = lerp(brightFloor, baseProp.rgb, smoothstep(0.18, 0.52, upMix));
                    shellSrgb = lerp(midPorcelain, crownSrgb, smoothstep(0.24, 0.88, keyDiff * 0.76 + upMix * 0.36));
                    float underMask = smoothstep(-0.72, -0.94, n.y);
                    shellSrgb = lerp(shellSrgb, lerp(underProp.rgb, brightFloor, 0.52), underMask);
                }
                else
                {
                    // Tapered ovoid torso shades smoothly from upper shoulders (#F8FAFD) to lower bullet point while staying luminous porcelain.
                    float3 torsoFloor = lerp(underProp.rgb, baseProp.rgb, 0.45);
                    shellSrgb = lerp(torsoFloor, baseProp.rgb, smoothstep(0.08, 0.56, upMix));
                    shellSrgb = lerp(shellSrgb, crownSrgb, smoothstep(0.28, 0.90, keyDiff * 0.78 + upMix * 0.34));
                }
                shellSrgb += float3(0.035, 0.040, 0.048) * fillDiff * liveScale;

                float3 hKey = normalize(keyDir + v);
                float3 hFill = normalize(fillDir + v);
                float coatSpec = (pow(saturate(dot(n, hKey)), 38.0) * 0.18 + pow(saturate(dot(n, hFill)), 20.0) * 0.05) * liveScale;
                shellSrgb = saturate(shellSrgb + coatSpec);

                float3 identityHue = lerp(tintProp.rgb, toneProp.rgb, 0.65);
                float sheenWeight = saturate(tintProp.w) * (0.35 + 0.65 * fresnel + 0.25 * rimLight) * liveScale;
                shellSrgb = lerp(shellSrgb, identityHue, saturate(sheenWeight));

                float3 srgb = shellSrgb;

                // Concave interior upper torso collar bowl (#141C26) and emissive inner collar ring (strictly inside profIdx > peakIdx).
                if (i.color.g > 0.01)
                {
                    float collarT = saturate(i.color.g);
                    float3 wellSrgb = float3(0.078, 0.110, 0.149) * (0.55 + 0.45 * liveScale);
                    float collarGlow = smoothstep(0.22, 0.50, collarT) * (1.0 - smoothstep(0.62, 0.92, collarT));
                    float3 collarSrgb = lerp(wellSrgb, toneProp.rgb, collarGlow * 0.84 * glowScale);
                    float wellBlend = smoothstep(0.14, 0.42, collarT);
                    srgb = lerp(srgb, collarSrgb, wellBlend);
                }

                // Luminous waist belt core + soft 25% additive porcelain halo (no black outline bars on the outer silhouette).
                if (i.color.b > 0.01)
                {
                    float haloMix = smoothstep(0.04, 0.48, i.color.b) * (1.0 - smoothstep(0.54, 0.86, i.color.b));
                    float coreMix = smoothstep(0.52, 0.86, i.color.b);
                    srgb = lerp(srgb, toneProp.rgb, haloMix * 0.25 * glowScale);

                    float dashMask = 1.0;
                    if (toneProp.w < 0.0)
                    {
                        float phase = frac(i.uv.x * 14.0);
                        dashMask = smoothstep(0.42, 0.34, abs(phase - 0.5));
                    }
                    float3 seamBase = srgb * 0.68;
                    float3 activeBelt = lerp(srgb * 0.42, toneProp.rgb, 0.25 + 0.75 * glowScale);
                    activeBelt = lerp(activeBelt, float3(1.0, 1.0, 1.0), smoothstep(0.88, 1.0, i.color.b) * 0.18 * glowScale);
                    float3 beltSrgb = lerp(seamBase, activeBelt, dashMask);
                    srgb = lerp(srgb, beltSrgb, coreMix);
                }

                // Recessed gloss-black front oval visor (#030406) with chamfered bezel lip (#C9D2DC) and convex downward lower arc.
                if (i.color.r < 0.5)
                {
                    float3 dHead = normalize(float3(i.localPos.x / 0.070, i.localPos.y / 0.056, -i.localPos.z / 0.064));
                    float u = atan2(dHead.x, max(dHead.z, 1e-4));
                    float vAngle = asin(clamp(dHead.y, -1.0, 1.0));
                    float vVis = vAngle - 0.02 - 0.04 * u * u;
                    float ax = abs(u) / 0.76;
                    float ay = abs(vVis) / 0.55;
                    float fe = dHead.z > 0.0 ? pow(pow(max(ax, 1e-4), 2.15) + pow(max(ay, 1e-4), 2.15), 1.0 / 2.15) : 99.0;

                    float visorMask = 1.0 - smoothstep(0.92, 0.98, fe);
                    float lipMask = smoothstep(0.88, 0.94, fe) * (1.0 - smoothstep(0.96, 1.03, fe));

                    float3 lipSrgb = float3(0.788, 0.824, 0.863) * (0.65 + 0.35 * liveScale) + float3(0.10, 0.11, 0.12) * keyDiff * liveScale;
                    float3 visorSrgb = float3(0.012, 0.016, 0.024);

                    // Studio softbox top-left brow reflection on the gloss-black visor glass, clear of the eyes.
                    float browArc = exp(-pow((u + 0.34) / 0.22, 2.0) - pow((vVis - 0.32) / 0.075, 2.0));
                    float rimSheen = smoothstep(0.72, 0.90, fe) * saturate(vVis * 1.6 + 0.2) * 0.06;
                    visorSrgb += float3(0.18, 0.22, 0.28) * (browArc * 0.55 + rimSheen) * liveScale;

                    srgb = lerp(srgb, lipSrgb, lipMask * 0.85);
                    srgb = lerp(srgb, visorSrgb, visorMask);
                }

                return float4(DecodeChannel(saturate(srgb)), 1.0);
            }
            ENDCG
        }
    }
}
