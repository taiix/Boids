Shader "HDRP/Blood In Water"
{
    // Particle shader for BloodInWater. Blood doesn't glow underwater, it absorbs: this tints the
    // scene behind each puff (kills green/blue, keeps red) and adds a little red scattered from
    // the light around it. Both come from the color pyramid, which is already exposed, so the
    // cloud reads the same in a bright shallow and a dark deep without any per-scene tuning.
    Properties
    {
        [HDR] _Transmittance ("Transmittance", Color) = (0.9, 0.08, 0.06, 1)
        _Albedo              ("Scatter Color", Color) = (0.8, 0.04, 0.03, 1)
        _Scatter             ("Scatter Strength", Range(0,4)) = 1.6
        _Density             ("Density", Range(0,1)) = 0.9
        _Breakup             ("Edge Breakup", Range(0,1)) = 0.55

        [Header(Fades)]
        _SoftFade            ("Soft Contact (m)", Float) = 0.5
        _NearFade            ("Near Camera Fade (m)", Float) = 1.5
        _FarFadeStart        ("Far Fade Start (m)", Float) = 15
        _FarFadeEnd          ("Far Fade End (m)", Float) = 40
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

            // Premultiplied alpha; leave the destination alpha alone.
            Blend One OneMinusSrcAlpha, Zero One
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
                float4 _Transmittance;
                float4 _Albedo;
                float  _Scatter;
                float  _Density;
                float  _Breakup;
                float  _SoftFade;
                float  _NearFade;
                float  _FarFadeStart;
                float  _FarFadeEnd;
            CBUFFER_END

            // Vertex streams are set by BloodInWater.cs: UV in xy, StableRandom.x in z, AgePercent in w.
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

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
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
                float2 p    = input.uv.xy * 2.0 - 1.0;
                float  seed = input.uv.z * 61.7;
                float  age  = input.uv.w;

                // A soft round puff with a ragged rim. The noise eats in from the edge, and harder
                // as the particle ages, so clouds come apart into wisps instead of shrinking discs.
                float2 q = input.uv.xy * 2.2 + seed;
                float  n = ValueNoise(q + age * 0.8) * 0.6 + ValueNoise(q * 2.7 + 7.3 - age * 0.5) * 0.4;
                float  falloff = saturate(1.0 - length(p));
                falloff *= falloff; // no visible disc edge; overlapping puffs build the cloud
                float  erode = _Breakup * (0.6 + 0.4 * age);
                float  density = saturate(falloff * 1.8 - (1.0 - n) * erode * 1.2);
                density = density * density * (3.0 - 2.0 * density);

                // Fade where the puff meets geometry (seabed, the shark's own head), right in front
                // of the camera (the orbit camera swims through the cloud behind the shark), and with
                // distance, where the water's own fog would have swallowed it.
                float eye      = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                float sceneEye = LinearEyeDepth(LoadCameraDepth(uint2(input.positionCS.xy)), _ZBufferParams);
                float soft = saturate((sceneEye - eye) / max(_SoftFade, 1e-3));
                float nearK = smoothstep(0.3, 0.3 + max(_NearFade, 1e-3), eye);
                float farK  = 1.0 - smoothstep(_FarFadeStart, max(_FarFadeEnd, _FarFadeStart + 1e-3), eye);

                float a = saturate(density * input.color.a * _Density * soft * nearK * farK);

                // Behind is sampled with a slight blur so the puff looks like murky water, not a filter.
                float2 screenUV = input.positionCS.xy * _ScreenSize.zw;
                float3 behind   = SampleScenePyramid(screenUV, 1.0);
                float3 ambient  = SampleScenePyramid(screenUV, 4.0);

                // Under blue water, real blood has almost no red light to reflect and reads black.
                // Players read "blood" from red, so all of the surrounding light feeds the red
                // scatter - still dimmer in a dark trench than a sunlit shallow.
                float3 blood = behind * _Transmittance.rgb * input.color.rgb
                             + (ambient.r + ambient.g + ambient.b) * _Albedo.rgb * _Scatter;

                return float4(blood * a, a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
