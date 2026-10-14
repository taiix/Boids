Shader "HDRP/Light Shaft"
{
    // Sunbeams for UnderwaterAmbience. Each shaft is a quad that turns around its own axis to face
    // the camera, laid along the refracted sun direction. Additive. The brightness comes from the
    // colour pyramid (the scene behind the beam, already exposed), so a beam reads the same in a
    // bright shallow and a murky deep, and for either role's water, without per-scene tuning.
    Properties
    {
        _Tint        ("Tint", Color) = (1.0, 0.97, 0.86, 1)
        _Gain        ("Gain", Range(0, 4)) = 0.55
        _Streaks     ("Inner Streaks", Range(0, 1)) = 0.45

        [Header(Fades)]
        _SoftFade    ("Soft Contact (m)", Float) = 2.5
        _NearFade    ("Near Camera Fade (m)", Float) = 4
        _FarFadeStart("Far Fade Start (m)", Float) = 25
        _FarFadeEnd  ("Far Fade End (m)", Float) = 60
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

            // Light only adds; leave the destination alpha alone.
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
                float  _Streaks;
                float  _SoftFade;
                float  _NearFade;
                float  _FarFadeStart;
                float  _FarFadeEnd;
            CBUFFER_END

            // Built by UnderwaterAmbience.cs in world space:
            //   uv.x across the beam (-1..1), uv.y along it (0 bottom .. 1 top), uv.z seed, uv.w strength
            //   normal = the beam's axis, so beams seen end-on can fade out instead of turning into slivers
            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 uv         : TEXCOORD0;
                float3 axisWS     : TEXCOORD1;
                float3 positionRWS: TEXCOORD2;
            };

            float3 SampleScenePyramid(float2 uv, float lod)
            {
                float2 scaled = uv * _ColorPyramidUvScaleAndLimitCurrentFrame.xy;
                scaled = clamp(scaled, 0.0, _ColorPyramidUvScaleAndLimitCurrentFrame.zw);
                return SAMPLE_TEXTURE2D_X_LOD(_ColorPyramidTexture, s_trilinear_clamp_sampler, scaled, lod).rgb;
            }

            float Hash11(float p)
            {
                p = frac(p * 0.1031);
                p *= p + 33.33;
                p *= p + p;
                return frac(p);
            }

            float Noise1(float x)
            {
                float i = floor(x);
                float f = frac(x);
                float u = f * f * (3.0 - 2.0 * f);
                return lerp(Hash11(i), Hash11(i + 1.0), u);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionRWS = TransformObjectToWorld(input.positionOS);
                output.positionCS  = TransformWorldToHClip(positionRWS);
                output.positionRWS = positionRWS;
                output.axisWS      = TransformObjectToWorldDir(input.normalOS);
                output.uv          = input.uv;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float across = input.uv.x;
                float along  = input.uv.y;
                float seed   = input.uv.z * 97.0;

                // Soft across the width, with a few slow streaks inside so a beam isn't a flat ribbon.
                float w = saturate(1.0 - abs(across));
                float profile = w * w * (3.0 - 2.0 * w);
                float t = _Time.y;
                float streaks = Noise1(across * 3.5 + seed + t * 0.07) * 0.6 + Noise1(across * 9.0 - seed - t * 0.11) * 0.4;
                profile *= lerp(1.0, streaks * 1.6, _Streaks);

                // Brightest overhead, gone before it reaches the floor and before its top end.
                float lengthK = smoothstep(0.0, 0.35, along) * (1.0 - smoothstep(0.75, 1.0, along));

                // Fade where the beam meets geometry, right at the camera, with distance (the water
                // has swallowed it there anyway), and when it is seen end-on.
                float eye      = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                float sceneEye = LinearEyeDepth(LoadCameraDepth(uint2(input.positionCS.xy)), _ZBufferParams);
                float soft  = saturate((sceneEye - eye) / max(_SoftFade, 1e-3));
                float nearK = smoothstep(0.5, 0.5 + max(_NearFade, 1e-3), eye);
                float farK  = 1.0 - smoothstep(_FarFadeStart, max(_FarFadeEnd, _FarFadeStart + 1e-3), eye);
                float3 V    = normalize(-input.positionRWS);
                float sideK = 1.0 - pow(saturate(abs(dot(V, normalize(input.axisWS)))), 6.0);

                float a = profile * lengthK * soft * nearK * farK * sideK * input.uv.w;

                // The surrounding water light, heavily blurred, is what the beam brightens.
                float2 screenUV = input.positionCS.xy * _ScreenSize.zw;
                float3 ambient  = SampleScenePyramid(screenUV, 5.0);
                float  level    = dot(ambient, float3(0.3333, 0.3333, 0.3333));

                return float4(_Tint.rgb * level * _Gain * a, 0.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
