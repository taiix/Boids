Shader "HDRP/Frosted Glass"
{
    Properties
    {
        [Header(Frost)]
        _Blur                ("Blur", Range(0,1)) = 0.5
        _BlurSpread          ("Blur Spread", Range(0,4)) = 1.5
        _DistanceBlur        ("Distance Blur", Range(0,1)) = 0.3
        _DistanceBlurRange   ("Distance Blur Range (m)", Float) = 25

        [Header(Surface)]
        _Tint                ("Tint", Color) = (0.55, 0.78, 0.82, 1)
        _TintStrength        ("Tint Strength", Range(0,1)) = 0.35
        _Milkiness           ("Milkiness", Range(0,1)) = 0.12
        _Opacity             ("Opacity", Range(0,1)) = 1

        [Header(Distortion)]
        [Toggle(_PROCEDURAL_FROST)] _ProceduralFrost ("Procedural Frost", Float) = 1
        [Normal] _NormalMap  ("Frost Normal Map", 2D) = "bump" {}
        _NormalStrength      ("Frost Strength", Range(0,4)) = 1
        _FrostTiling         ("Frost Tiling (per world unit)", Float) = 0.35
        _Distortion          ("Distortion", Range(0,0.25)) = 0.03

        [Header(Edges)]
        _RimColor            ("Rim Color", Color) = (0.80, 0.95, 1.0, 1)
        _RimPower            ("Rim Power", Range(0.5,16)) = 4
        _RimStrength         ("Rim Strength", Range(0,2)) = 0.4
        _IntersectionFade    ("Intersection Fade (m)", Float) = 1.5
        _DetailFadeDistance  ("Detail Fade Distance (m, 0 = off)", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "HighDefinitionRenderPipeline"
            "RenderType"     = "HDUnlitShader"
            "Queue"          = "Transparent"
        }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch
            #pragma multi_compile_instancing
            #pragma shader_feature_local _PROCEDURAL_FROST
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPass.cs.hlsl"
            #define SHADERPASS SHADERPASS_FORWARD_UNLIT

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariablesFunctions.hlsl"

            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float4 _RimColor;
                float4 _NormalMap_ST;
                float  _Blur;
                float  _BlurSpread;
                float  _DistanceBlur;
                float  _DistanceBlurRange;
                float  _TintStrength;
                float  _Milkiness;
                float  _Opacity;
                float  _NormalStrength;
                float  _FrostTiling;
                float  _Distortion;
                float  _RimPower;
                float  _RimStrength;
                float  _IntersectionFade;
                float  _DetailFadeDistance;
                float  _ProceduralFrost;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float3 positionRWS : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // The color pyramid is an RTHandle: only a sub-rect is valid and it shrinks per mip.
            // Scale into that rect and clamp, or high mips drag in garbage from the padding.
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
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float Fbm(float2 p)
            {
                float v = 0.0;
                float a = 0.5;
                [unroll]
                for (int i = 0; i < 3; i++)
                {
                    v += a * ValueNoise(p);
                    p *= 2.03;
                    a *= 0.5;
                }
                return v;
            }

            // A frame built from the surface normal and world position, not mesh UVs: a cube
            // flattened into a slab has wildly stretched UVs, and adjacent panes would not match.
            void SurfaceFrame(float3 N, out float3 T, out float3 B)
            {
                float3 up = abs(N.y) > 0.99 ? float3(0, 0, 1) : float3(0, 1, 0);
                T = normalize(cross(up, N));
                B = cross(N, T);
            }

            float3 FrostNormal(float3 positionAWS, float3 N)
            {
                float3 T, B;
                SurfaceFrame(N, T, B);
                float2 uv = float2(dot(positionAWS, T), dot(positionAWS, B)) * _FrostTiling;

            #ifdef _PROCEDURAL_FROST
                const float e = 0.35;
                float h  = Fbm(uv);
                float hx = Fbm(uv + float2(e, 0));
                float hy = Fbm(uv + float2(0, e));
                float2 g = float2(hx - h, hy - h) / e;
                return normalize(N - (T * g.x + B * g.y) * _NormalStrength);
            #else
                float3 ts = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv), _NormalStrength);
                return normalize(T * ts.x + B * ts.y + N * ts.z);
            #endif
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                ZERO_INITIALIZE(Varyings, output);
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionRWS = TransformObjectToWorld(input.positionOS);
                output.positionRWS = positionRWS;
                output.positionCS  = TransformWorldToHClip(positionRWS);
                output.normalWS    = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            float4 Frag(Varyings input, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Double sided: flip the normal so the inside face refracts and rims correctly.
                float3 N = normalize(input.normalWS) * (isFrontFace ? 1.0 : -1.0);
                float3 V = GetWorldSpaceNormalizeViewDir(input.positionRWS);
                float3 positionAWS = GetAbsolutePositionWS(input.positionRWS);

                float2 screenUV = input.positionCS.xy * _ScreenSize.zw;

                float glassEye = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                float sceneEye = LinearEyeDepth(LoadCameraDepth(uint2(input.positionCS.xy)), _ZBufferParams);
                float behind   = max(sceneEye - glassEye, 0.0);

                // Refraction offset: deviation of the frosted normal from the flat one, in view space.
                float3 frostN = FrostNormal(positionAWS, N);
                float3 devVS  = mul((float3x3)UNITY_MATRIX_V, frostN - N);
                float2 refrUV = saturate(screenUV + devVS.xy * _Distortion);

                // Never pull in a sample that sits in front of the glass, or foreground objects
                // smear across the pane.
                float refrEye = LinearEyeDepth(LoadCameraDepth(uint2(refrUV * _ScreenSize.xy)), _ZBufferParams);
                refrUV = (refrEye < glassEye) ? screenUV : refrUV;

                float blur01 = saturate(_Blur + saturate(behind / max(_DistanceBlurRange, 1e-4)) * _DistanceBlur);

                // _ColorPyramidLodCount is written at frame start but only assigned while the
                // pyramid is built, so it is 0 on a camera's first frame. Fall back to the count
                // the pyramid actually generates (it halves until a side drops below 8).
                float pyramidLods = _ColorPyramidLodCount > 0.5
                    ? _ColorPyramidLodCount - 1.0
                    : floor(log2(max(min(_ScreenSize.x, _ScreenSize.y) / 8.0, 1.0)));
                // Past ~7 the mip is a handful of pixels: the pane turns into a flat colour
                // rather than frosted glass, so cap it well short of the top of the chain.
                float lod = blur01 * min(pyramidLods, 7.0);

                // A ring of taps hides the blockiness of the pyramid. The radius is one texel of
                // the sampled mip, but capped: at high mips a texel is wider than the screen and
                // every tap would clamp to the edge and drag in the corners.
                float2 texel = _ScreenSize.zw * min(exp2(lod), 8.0) * _BlurSpread;
                float  angle = InterleavedGradientNoise(input.positionCS.xy, 0) * TWO_PI;
                float  sa, ca;
                sincos(angle, sa, ca);
                float2x2 rot = float2x2(ca, -sa, sa, ca);

                float3 sceneCol = SampleScenePyramid(refrUV, lod) * 0.25;
                [unroll]
                for (int t = 0; t < 6; t++)
                {
                    float  a = TWO_PI * (t / 6.0);
                    float2 o = mul(rot, float2(cos(a), sin(a))) * texel;
                    sceneCol += SampleScenePyramid(saturate(refrUV + o), lod) * 0.125;
                }

                float detailFade = _DetailFadeDistance > 0.0
                    ? saturate(1.0 - glassEye / _DetailFadeDistance)
                    : 1.0;

                float3 glass = sceneCol * lerp(1.0, _Tint.rgb, _TintStrength);
                glass = lerp(glass, _Tint.rgb, _Milkiness * detailFade);

                float fresnel = pow(saturate(1.0 - saturate(dot(N, V))), _RimPower);
                glass += _RimColor.rgb * (fresnel * _RimStrength * detailFade);

                float alpha = _Opacity;
                if (_IntersectionFade > 0.0)
                    alpha *= saturate(behind / _IntersectionFade);

                return float4(glass, saturate(alpha));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
