Shader "Hidden/FullScreen/Outline"
{
    Properties
    {
        _BlitTexture("Source", 2D) = "white"{}
        _SelectionMaskTexture("Selection Mask", 2D) = "black"{}
        _OcclusionMaskTexture("Occlusion Mask", 2D) = "black"{}
        _Thickness("Thickness", Float) = 1
        _OutlineColor("Outline Color", Color) = (0, 1, 0, 1)
        _DepthThreshold("Depth Threshold", Float) = 1.5
        _NormalThreshold("Normal Threshold", Float) = 0.4
        _ColorThreshold("Color Threshold", Float) = 0.2
        _DebugMode("Debug Mode", Int) = 0
        _FadeParams("Fade Params", Vector) = (0, 50, 0, 10)
        _TintAmount("Line takes the object colour", Range(0,1)) = 0
        _TintDarken("Object colour darkening", Range(0,1)) = 0.4
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100
        ZWrite Off
        Cull Off
        ZTest Always

        Pass
        {
            Name "OutlinePass"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile_local _ ALGO_SOBEL ALGO_ROBERTS
            #pragma multi_compile_local _ OUTLINE_FULL OUTLINE_SELECTION OUTLINE_MIXED
            #pragma multi_compile_local _ USE_DEPTH
            #pragma multi_compile_local _ USE_NORMALS
            #pragma multi_compile_local _ USE_COLOR
            #pragma multi_compile_local _ USE_DISTANCE_FADE
            #pragma multi_compile_local _ USE_HEIGHT_FADE
            #pragma multi_compile_local _ USE_OCCLUSION_MASK

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_SelectionMaskTexture);
            SAMPLER(sampler_SelectionMaskTexture);
            TEXTURE2D(_OcclusionMaskTexture); // Reuse sampler from Selection or LinearClamp

            float _Thickness;
            float4 _OutlineColor;
            float _DepthThreshold;
            float _NormalThreshold;
            float _ColorThreshold;
            int _DebugMode;
            float4 _FadeParams;
            float _TintAmount;
            float _TintDarken;
            // 1 when the outline mask also rides in the scene colour's alpha (OutlineAlphaMask.hlsl):
            // the outlined crowd writes alpha 0 instead of being redrawn into the mask texture.
            float _AlphaMask;

            float SelMask(float2 uv)
            {
                float m = SAMPLE_TEXTURE2D(_SelectionMaskTexture, sampler_LinearClamp, uv).r;
                if (_AlphaMask > 0.5) m = max(m, 1.0 - SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv).a);
                return m;
            }

            // 2026-10-02 cartoon line: the colour of the selected object next to this edge pixel,
            // darkened and a little more saturated (green leaf -> moss line, red -> maroon line).
            half3 ObjectLineColour(float2 uv, float2 delta)
            {
                float2 dirs[4] = { float2(1, 0), float2(-1, 0), float2(0, 1), float2(0, -1) };
                float best = 0; float2 off = 0;
                [unroll] for (int i = 0; i < 4; i++)
                {
                    float m = SelMask(uv + dirs[i] * delta);
                    if (m > best) { best = m; off = dirs[i]; }
                }
                half3 c = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + off * delta * 1.5).rgb;
                half l = dot(c, half3(0.2126, 0.7152, 0.0722));
                c = lerp(l.xxx, c, 1.35);
                return saturate(c) * _TintDarken;
            }

            float GetLuminance(float3 color)
            {
                return dot(color, float3(0.2126, 0.7152, 0.0722));
            }

            float3 GetSample(float2 uv)
            {
                float3 result = float3(0, 0, 0);
                #if defined(USE_DEPTH)
                    // Eye-space metres keep the edge response stable when the camera far clip changes.
                    result.x = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                #endif
                #if defined(USE_NORMALS)
                    result.y = dot(SampleSceneNormals(uv), float3(1, 1, 1));
                #endif
                #if defined(USE_COLOR)
                    result.z = GetLuminance(SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv).rgb);
                #endif
                return result;
            }

            float CalculateEdge(float2 uv, float2 delta)
            {
                float nearestDepth = 1.0;

                #if defined(ALGO_SOBEL)
                    float3 s0 = GetSample(uv + float2(-delta.x, -delta.y));
                    float3 s1 = GetSample(uv + float2(0, -delta.y));
                    float3 s2 = GetSample(uv + float2(delta.x, -delta.y));
                    float3 s3 = GetSample(uv + float2(-delta.x, 0));
                    float3 s5 = GetSample(uv + float2(delta.x, 0));
                    float3 s6 = GetSample(uv + float2(-delta.x, delta.y));
                    float3 s7 = GetSample(uv + float2(0, delta.y));
                    float3 s8 = GetSample(uv + float2(delta.x, delta.y));

                    float3 gx = s2 + s8 + 2 * s5 - s0 - s6 - 2 * s3;
                    float3 gy = s6 + s8 + 2 * s7 - s0 - s2 - 2 * s1;
                    float3 sq = gx * gx + gy * gy;

                    #if defined(USE_DEPTH)
                        nearestDepth = max(
                            min(min(min(s0.x, s1.x), min(s2.x, s3.x)),
                                min(min(s5.x, s6.x), min(s7.x, s8.x))),
                            0.001);
                    #endif
                #else
                    float3 s1 = GetSample(uv + float2(-delta.x, -delta.y));
                    float3 s2 = GetSample(uv + float2(delta.x, delta.y));
                    float3 s3 = GetSample(uv + float2(-delta.x, delta.y));
                    float3 s4 = GetSample(uv + float2(delta.x, -delta.y));
                    float3 d1 = s1 - s2;
                    float3 d2 = s3 - s4;
                    float3 sq = d1 * d1 + d2 * d2;

                    #if defined(USE_DEPTH)
                        nearestDepth = max(
                            min(min(s1.x, s2.x), min(s3.x, s4.x)),
                            0.001);
                    #endif
                #endif

                float e = 0;
                #if defined(USE_DEPTH)
                    // Convert the kernel magnitude to a relative depth discontinuity.
                    // _DepthThreshold now reads as a percentage (10 = 10%), independent
                    // of far clip and consistent between Roberts and Sobel.
                    float depthMagnitude = sqrt(sq.x);
                    #if defined(ALGO_SOBEL)
                        depthMagnitude *= 0.25;
                    #else
                        depthMagnitude *= 0.70710678;
                    #endif
                    float relativeDepthDelta = depthMagnitude / nearestDepth;
                    e = max(e, step(_DepthThreshold * 0.01, relativeDepthDelta));
                #endif
                #if defined(USE_NORMALS)
                    e = max(e, step(_NormalThreshold, sqrt(sq.y)));
                #endif
                #if defined(USE_COLOR)
                    e = max(e, step(_ColorThreshold, sqrt(sq.z)));
                #endif
                return e;
            }

            float CalculateSelectionEdge(float2 uv, float2 delta, float centerMask)
            {
                float s1 = SelMask(uv + float2(delta.x, 0));
                float s2 = SelMask(uv + float2(-delta.x, 0));
                float s3 = SelMask(uv + float2(0, delta.y));
                float s4 = SelMask(uv + float2(0, -delta.y));
                float diff = abs(centerMask - s1) + abs(centerMask - s2) + abs(centerMask - s3) + abs(centerMask - s4);
                return step(0.1, diff);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;

                if (_DebugMode == 1)
                {
                    // Raw Linear01Depth compresses a 10-30 m gameplay area into a nearly
                    // black sliver when far clip is 1000 m. Remap eye depth to the configured
                    // gameplay fade range and add subtle bands so depth zones are readable.
                    float eyeDepth = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                    float debugNear = _ProjectionParams.y;
                    float debugFar = max(_FadeParams.y, debugNear + 1.0);
                    float depth01 = saturate((eyeDepth - debugNear) / (debugFar - debugNear));
                    depth01 = pow(depth01, 0.65);
                    float bandedDepth = floor(depth01 * 16.0) / 15.0;
                    float visibleDepth = lerp(depth01, bandedDepth, 0.35);
                    return float4(visibleDepth.xxx, 1);
                }
                    if (_DebugMode == 2) return float4(SampleSceneNormals(uv) * 0.5 + 0.5, 1);
                    if (_DebugMode == 3) return SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);
                    if (_DebugMode == 5) return SelMask(uv).xxxx;
                    if (_DebugMode == 6) return SAMPLE_TEXTURE2D(_OcclusionMaskTexture, sampler_LinearClamp, uv);
                    if (_DebugMode == 7) return SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv).aaaa;

                float2 delta = _BlitTexture_TexelSize.xy * _Thickness;
                float edge = 0;
                float centerMask = 0;

                #if defined(USE_OCCLUSION_MASK)
                    float occlusion = SAMPLE_TEXTURE2D(
                        _OcclusionMaskTexture, sampler_LinearClamp, uv).r;
                    if (occlusion > 0.5)
                        return SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);
                #endif

                #if defined(OUTLINE_SELECTION) || defined(OUTLINE_MIXED)
                    centerMask = SelMask(uv);
                #endif

                #if defined(OUTLINE_FULL) || defined(OUTLINE_MIXED)
                    edge = CalculateEdge(uv, delta);
                #endif

                #if defined(OUTLINE_SELECTION) || defined(OUTLINE_MIXED)
                    float selEdge = CalculateSelectionEdge(uv, delta, centerMask);
                    edge = max(edge, selEdge);
                    edge *= (1.0 - step(0.01, centerMask));
                #endif

                #if defined(USE_DISTANCE_FADE) || defined(USE_HEIGHT_FADE)
                    float rawDepth = SampleSceneDepth(uv);
                #endif

                #if defined(USE_DISTANCE_FADE)
                    float linearDist = LinearEyeDepth(rawDepth, _ZBufferParams);
                    float distFactor = 1.0 - saturate((linearDist - _FadeParams.x) / (_FadeParams.y - _FadeParams.x));
                    edge *= distFactor;
                #endif

                #if defined(USE_HEIGHT_FADE)
                    float3 worldPos = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
                    float heightFactor = saturate((worldPos.y - _FadeParams.z) / (_FadeParams.w - _FadeParams.z));
                    edge *= heightFactor;
                #endif

                if (_DebugMode == 4) return float4(edge, edge, edge, 1);

                half4 sceneColor = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);
                half4 lineColor = _OutlineColor;
                #if defined(OUTLINE_SELECTION) || defined(OUTLINE_MIXED)
                    if (_TintAmount > 0.001 && edge > 0.001)
                        lineColor.rgb = lerp(_OutlineColor.rgb, ObjectLineColour(uv, delta), _TintAmount);
                #endif
                return lerp(sceneColor, lineColor, edge * _OutlineColor.a);
            }
            ENDHLSL
        }
    }
}
