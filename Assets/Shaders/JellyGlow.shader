Shader "HDRP/Jelly Glow"
{
    // Jellyfish for the jellyfish-swarm world event: an additive, see-through body that glows at its rim
    // (where you look through the most jelly) and pulses faintly, giving off its own light so the swarm
    // reads at a distance underwater. Depth-tested, so rocks hide it.
    Properties
    {
        _Tint        ("Tint", Color) = (0.85, 0.45, 1.0, 1)
        _Glow        ("Glow", Range(0, 8)) = 1.2
        _Core        ("Core (facing you)", Range(0, 1)) = 0.12
        _Pulse       ("Pulse speed", Float) = 3
        _FarFadeStart("Far Fade Start (m)", Float) = 35
        _FarFadeEnd  ("Far Fade End (m)", Float) = 80
    }

    SubShader
    {
        Tags
        {
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
                float  _Core;
                float  _Pulse;
                float  _FarFadeStart;
                float  _FarFadeEnd;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 viewWS     : TEXCOORD1;
                float  phase      : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionRWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(positionRWS);
                output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                output.viewWS     = -positionRWS; // camera-relative rendering: the camera is at the origin
                // Each jellyfish pulses on its own beat (from where it is).
                float3 origin = GetAbsolutePositionWS(TransformObjectToWorld(float3(0, 0, 0)));
                output.phase = frac(sin(dot(origin, float3(12.9898, 78.233, 37.719))) * 43758.5453) * 6.2832;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 v = normalize(input.viewWS);
                float rim = 1.0 - abs(dot(n, v));
                float body = lerp(_Core, 1.0, rim * rim);
                float pulse = 0.75 + 0.25 * sin(_Time.y * _Pulse + input.phase);

                float eye  = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                float farK = 1.0 - smoothstep(_FarFadeStart, max(_FarFadeEnd, _FarFadeStart + 1e-3), eye);

                return float4(_Tint.rgb * _Glow * body * pulse * farK, 0.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
