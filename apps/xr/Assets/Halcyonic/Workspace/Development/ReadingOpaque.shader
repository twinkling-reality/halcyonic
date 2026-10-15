// Independent literal positive reference. No Surface code, textures, lighting or camera mutation.
Shader "Halcyonic/Development Reading Opaque"
{
    Properties { _Color ("Literal colour", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "Queue"="Transparent+100" "RenderType"="Transparent" }
        Pass
        {
            Blend Off
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            struct Input { float4 vertex:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Output { float4 vertex:SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            Output vert(Input input) { Output output; UNITY_SETUP_INSTANCE_ID(input); UNITY_INITIALIZE_OUTPUT(Output,output); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output); output.vertex=UnityObjectToClipPos(input.vertex); return output; }
            float4 _Color;
            float4 frag(Output input):SV_Target { return float4(_Color.rgb,1); }
            ENDCG
        }
    }
}
