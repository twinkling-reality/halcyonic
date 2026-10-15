// Meta's documented multiplicative passthrough-window operation: destination RGBA times .15.
// This deliberate negative occupies only its labeled synthetic patch, after normal virtual content.
Shader "Halcyonic/Development Reading Mask"
{
    Properties { _Color ("Literal mask alpha", Color) = (0,0,0,.15) }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" }
        Pass
        {
            Blend Zero SrcAlpha
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
            float4 frag(Output input):SV_Target { return float4(0,0,0,_Color.a); }
            ENDCG
        }
    }
}
