// A person's world mixed from its scene's layers (ADR 0049, decision 9), into one panorama the shell then shows.
// Each layer is one of the scene's lights alone, composited through the scene's air as it was made, in linear
// light, stored as twelve stops below its scale on a log curve (so the dark land keeps its steps in eight bits). The mix weights each light by the description's brightness, tint and how
// far night has come; lights each place's own light by whether its project is at work; thins or thickens the air
// from its amount at each point; then gives the sum the scene's own view transform, a table the film renderer
// made, so the headset shows what the render would. It runs when the mix changes, never every frame.
Shader "Halcyonic/World/Mix"
{
    Properties
    {
        _L0 ("Light 0", 2D) = "black" {}
        _L1 ("Light 1", 2D) = "black" {}
        _L2 ("Light 2", 2D) = "black" {}
        _L3 ("Light 3", 2D) = "black" {}
        _L4 ("Light 4", 2D) = "black" {}
        _L5 ("Light 5", 2D) = "black" {}
        _L6 ("Light 6", 2D) = "black" {}
        _L7 ("Light 7", 2D) = "black" {}
        _P0 ("Places 0 to 3", 2D) = "black" {}
        _P1 ("Places 4 to 7", 2D) = "black" {}
        _AirDay ("The air by day, and its amount", 2D) = "black" {}
        _AirNight ("The air by night", 2D) = "black" {}
        _View ("The view transform, 33 slices of 33 by 33", 2D) = "white" {}
    }
    SubShader
    {
        ZTest Always
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _L0, _L1, _L2, _L3, _L4, _L5, _L6, _L7;
            sampler2D _P0, _P1, _AirDay, _AirNight, _View;
            // Each light's weight: its tint times its brightness now times its scale, in linear light.
            float4 _LW[8];
            // Each place's light: its colour times how far it is lit times its scale.
            float4 _PW[8];
            float4 _AirDayW;
            float4 _AirNightW;
            // How thick the air is against the world as made: 1 as made.
            float _Thickness;

            // The view transform's input range, in stops: the film renderer's own.
            static const float Low = -12.47393;
            static const float High = 4.026069;

            // A stored value back to linear light, against its layer's scale: twelve stops, and 0 is none.
            float3 Light(float3 v)
            {
                return (v > 1.0 / 512.0) ? exp2(v * 12.0 - 12.0) : 0.0;
            }

            float4 Light4(float4 v)
            {
                return (v > 1.0 / 512.0) ? exp2(v * 12.0 - 12.0) : 0.0;
            }

            float3 View(float3 x)
            {
                float3 u = saturate((log2(max(x, 1e-10)) - Low) / (High - Low));
                float3 s = u * 32.0;
                float b0 = floor(s.b);
                float b1 = min(b0 + 1.0, 32.0);
                float2 rg = s.rg + 0.5;
                float3 a = tex2Dlod(_View, float4((b0 * 33.0 + rg.x) / 1089.0, rg.y / 33.0, 0, 0)).rgb;
                float3 b = tex2Dlod(_View, float4((b1 * 33.0 + rg.x) / 1089.0, rg.y / 33.0, 0, 0)).rgb;
                return lerp(a, b, s.b - b0);
            }

            float4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
                float3 c = _LW[0].rgb * Light(tex2D(_L0, uv).rgb)
                    + _LW[1].rgb * Light(tex2D(_L1, uv).rgb)
                    + _LW[2].rgb * Light(tex2D(_L2, uv).rgb)
                    + _LW[3].rgb * Light(tex2D(_L3, uv).rgb)
                    + _LW[4].rgb * Light(tex2D(_L4, uv).rgb)
                    + _LW[5].rgb * Light(tex2D(_L5, uv).rgb)
                    + _LW[6].rgb * Light(tex2D(_L6, uv).rgb)
                    + _LW[7].rgb * Light(tex2D(_L7, uv).rgb);
                // Each place's light alone, as its brightness, four to a picture.
                float4 p0 = Light4(tex2D(_P0, uv));
                float4 p1 = Light4(tex2D(_P1, uv));
                c += _PW[0].rgb * p0.r + _PW[1].rgb * p0.g + _PW[2].rgb * p0.b + _PW[3].rgb * p0.a
                    + _PW[4].rgb * p1.r + _PW[5].rgb * p1.g + _PW[6].rgb * p1.b + _PW[7].rgb * p1.a;
                // The air: what it lets through, as made, was 1 - f; thicker or thinner lets through 1 - k f.
                // The air's amount rides in the alpha, plain 0 to 1.
                float4 air = tex2D(_AirDay, uv);
                float f = air.a;
                c *= saturate(1.0 - _Thickness * f) / max(1.0 - f, 0.02);
                c += _AirDayW.rgb * Light(air.rgb) + _AirNightW.rgb * Light(tex2D(_AirNight, uv).rgb);
                return float4(View(max(c, 0.0)), 1.0);
            }
            ENDCG
        }
    }
}
