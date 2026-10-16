Shader "HDRP/Plankton Glow"
{
    // Bioluminescent plankton for the nightfall world event: soft round additive dots like
    // WaterMote.shader (same particle vertex streams: UV in xy, StableRandom.x in z, AgePercent in w),
    // but they give off their own light - a fixed brightness instead of the colour pyramid's - so they
    // glow brightest exactly when the reef goes dark.
    Properties
    {
        _Tint        ("Tint", Color) = (0.35, 1.0, 0.85, 1)
        _Glow        ("Glow", Range(0, 8)) = 1.6

        [Header(Fades)]
        _NearFade    ("Near Camera Fade (m)", Float) = 0.3
        _FarFadeStart("Far Fade Start (m)", Float) = 30
        _FarFadeEnd  ("Far Fade End (m)", Float) = 70
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
                float  _Glow;
                float  _NearFade;
                float  _FarFadeStart;
                float  _FarFadeEnd;
            CBUFFER_END

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

                // Flares up as it's stirred, then fades out over its life.
                float age   = input.uv.w;
                float lifeK = smoothstep(0.0, 0.05, age) * (1.0 - smoothstep(0.3, 1.0, age));
                float sparkle = lerp(0.5, 1.0, input.uv.z);

                float eye   = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                float nearK = smoothstep(0.1, 0.1 + max(_NearFade, 1e-3), eye);
                float farK  = 1.0 - smoothstep(_FarFadeStart, max(_FarFadeEnd, _FarFadeStart + 1e-3), eye);

                float a = dot_ * lifeK * sparkle * nearK * farK * input.color.a;
                return float4(_Tint.rgb * input.color.rgb * _Glow * a, 0.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
