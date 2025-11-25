Shader "Hidden/Outline/SelectionMask"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white"{}
        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100
        ColorMask R
        ZWrite Off
        ZTest LEqual

        Pass
        {
            Name "SelectionMask"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local_fragment _ALPHATEST_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            float4 _BaseMap_ST;
            float _Cutoff;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                #if defined(_ALPHATEST_ON)
                    half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a;
                    clip(alpha - _Cutoff);
                #endif
                return half4(1, 0, 0, 1);
            }
            ENDHLSL
        }
    }
}
