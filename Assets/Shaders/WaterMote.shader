Shader "HDRP/Water Mote"
{
    // Drifting specks for UnderwaterAmbience (marine snow / suspended sand). Additive, soft round
    // dots that catch the light around them. Like LightShaft.shader, the brightness comes from the
    // colour pyramid, so the specks never glow brighter than the water they float in.
    Properties
    {
        _Tint        ("Tint", Color) = (0.9, 1.0, 0.97, 1)
        _Gain        ("Gain", Range(0, 8)) = 2.2

        [Header(Fades)]
        _NearFade    ("Near Camera Fade (m)", Float) = 0.6
        _FarFadeStart("Far Fade Start (m)", Float) = 10
        _FarFadeEnd  ("Far Fade End (m)", Float) = 22
    }

    SubShader
    {
        Tags
        {
            // Must be exactly "HDRenderPipeline" - see FrostedGlass.shader.
            "RenderPipeline" = "HDRenderPipeline"
            "RenderType"     = "HDUnlitShader"
            "Queue"          = "Transparent"
        }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }

            Blend One One, Zero One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPass.cs.hlsl"
            #define SHADERPASS SHADERPASS_FORWARD_UNLIT

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariablesFunctions.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float  _Gain;
                float  _NearFade;
                float  _FarFadeStart;
                float  _FarFadeEnd;
            CBUFFER_END

            // Vertex streams are set by UnderwaterAmbience.cs: UV in xy, StableRandom.x in z, AgePercent in w.
            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color      : COLOR;
                float4 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                float4 uv         : TEXCOORD0;
            };

            float3 SampleScenePyramid(float2 uv, float lod)
            {
                float2 scaled = uv * _ColorPyramidUvScaleAndLimitCurrentFrame.xy;
                scaled = clamp(scaled, 0.0, _ColorPyramidUvScaleAndLimitCurrentFrame.zw);
                return SAMPLE_TEXTURE2D_X_LOD(_ColorPyramidTexture, s_trilinear_clamp_sampler, scaled, lod).rgb;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionRWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(positionRWS);
                output.color      = input.color;
                output.uv         = input.uv;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 p   = input.uv.xy * 2.0 - 1.0;
                float  r   = saturate(1.0 - length(p));
                float  dot_ = r * r;

                // In and out over its life, and some specks sparkle more than others.
                float age   = input.uv.w;
                float lifeK = smoothstep(0.0, 0.15, age) * (1.0 - smoothstep(0.75, 1.0, age));
                float sparkle = lerp(0.35, 1.0, input.uv.z);

                float eye   = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                float nearK = smoothstep(0.15, 0.15 + max(_NearFade, 1e-3), eye);
                float farK  = 1.0 - smoothstep(_FarFadeStart, max(_FarFadeEnd, _FarFadeStart + 1e-3), eye);

                float a = dot_ * lifeK * sparkle * nearK * farK * input.color.a;

                float2 screenUV = input.positionCS.xy * _ScreenSize.zw;
                float3 ambient  = SampleScenePyramid(screenUV, 4.0);
                float  level    = dot(ambient, float3(0.3333, 0.3333, 0.3333));

                return float4(_Tint.rgb * input.color.rgb * level * _Gain * a, 0.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
