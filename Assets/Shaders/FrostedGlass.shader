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
        _IntersectionFade    ("Contact Width (m)", Float) = 1.5
        _ContactBlend        ("Contact Blend", Range(0,1)) = 1
        _ContactNormalMerge  ("Contact Normal Merge", Range(0,1)) = 0.8
        _DetailFadeDistance  ("Detail Fade Distance (m, 0 = off)", Float) = 0
    }

    SubShader
    {
        Tags
        {
            // Must be exactly "HDRenderPipeline" - that is the id HDRP registers. Anything else
            // (e.g. "HighDefinitionRenderPipeline") matches no active pipeline asset, so the build
            // strips every SubShader and the material renders pink, while the editor still shows it.
            "RenderPipeline" = "HDRenderPipeline"
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
            // Note: "d3d11" here covers both DX11 and DX12 players - "d3d12" is not a valid token
            // for only_renderers and Unity warns if you add it.
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
            // Note: HDRP does not bind _NormalBufferTexture during the transparent forward pass —
            // reading it here returns a constant. The scene normal is reconstructed from depth
            // derivatives below instead.

            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);

            // Baked distance field of the scenery around this pane, fed per-renderer by
            // GlassSdfVolume. Declared outside UnityPerMaterial because it arrives through a
            // MaterialPropertyBlock, which is per-renderer rather than per-material.
            TEXTURE3D(_SdfTex);
            SAMPLER(sampler_SdfTex);
            float4x4 _SdfWorldToUvw;
            float    _HasSdf;

            // Wall-segment description of this pane and its neighbours, fed per-renderer by
            // GlassPaneNetwork. xyz is a centreline endpoint; w carries half thickness on A and
            // half height on B.
            #define MAX_PANE_NEIGHBOURS 8
            float4 _OwnSegA;
            float4 _OwnSegB;
            float4 _OtherSegA[MAX_PANE_NEIGHBOURS];
            float4 _OtherSegB[MAX_PANE_NEIGHBOURS];
            float  _OtherCount;
            float  _CornerRadius;
            float  _PaneNetworkOn;

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
                float  _ContactBlend;
                float  _ContactNormalMerge;
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

            // Distance to one wall segment: a slab of given thickness and height swept along a
            // centreline. The cross-section is round in plan and flat in height, so a trimmed end
            // carries a round cap — which is what makes two segments meeting at a point produce a
            // rounded outer corner without any corner geometry.
            float SdWallSegment(float3 p, float3 a, float3 b, float halfThickness, float halfHeight, float corner)
            {
                float3 ab = b - a;
                float  t  = saturate(dot(p - a, ab) / max(dot(ab, ab), 1e-6));
                float3 d  = p - (a + ab * t);

                // Rounding insets the extents and adds the radius back, so the outer size is
                // unchanged — growing instead would re-inflate a stub past the point it was
                // trimmed to. A wall end can be no rounder than a half-circle of its own
                // half thickness, so the radius is capped there.
                float r = min(corner, min(halfThickness, halfHeight));
                float2 q = float2(length(d.xz) - (halfThickness - r), abs(d.y) - (halfHeight - r));
                return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r;
            }

            // True distance from a world point to the baked scenery, in metres.
            //
            // This is the information screen space cannot supply: the depth buffer only knows the
            // surface along each ray, so it cannot tell how far the glass is from terrain that is
            // off to one side, which is exactly the case at an intersection seen at a grazing
            // angle. Returns a negative sentinel when there is no field or the point is outside it.
            float SampleContactSdf(float3 positionAWS)
            {
                if (_HasSdf < 0.5) return -1.0;
                float3 uvw = mul(_SdfWorldToUvw, float4(positionAWS, 1.0)).xyz;
                if (any(uvw < 0.0) || any(uvw > 1.0)) return -1.0;
                return abs(SAMPLE_TEXTURE3D_LOD(_SdfTex, sampler_SdfTex, uvw, 0).r);
            }

            // World position of the opaque surface under a pixel, from the depth buffer alone.
            float3 ScenePositionAt(float2 positionSS)
            {
                float2 sp = clamp(positionSS, float2(0.0, 0.0), _ScreenSize.xy - 1.0);
                float deviceDepth = LoadCameraDepth(uint2(sp));
                return ComputeWorldSpacePosition((sp + 0.5) * _ScreenSize.zw, deviceDepth, UNITY_MATRIX_I_VP);
            }

            // Scene normal from depth derivatives. Each axis takes whichever neighbour is closer
            // in depth, so the frame does not smear across a silhouette where the two sides
            // belong to completely different surfaces.
            float3 ReconstructSceneNormal(float2 positionSS, float3 centre, float3 V)
            {
                float3 px = ScenePositionAt(positionSS + float2(1, 0));
                float3 mx = ScenePositionAt(positionSS - float2(1, 0));
                float3 py = ScenePositionAt(positionSS + float2(0, 1));
                float3 my = ScenePositionAt(positionSS - float2(0, 1));

                float3 dx = length(px - centre) < length(centre - mx) ? px - centre : centre - mx;
                float3 dy = length(py - centre) < length(centre - my) ? py - centre : centre - my;

                float3 n = cross(dy, dx);
                float len = length(n);
                if (len < 1e-9) return V;                 // degenerate, fall back to facing us
                n /= len;
                return dot(n, V) < 0.0 ? -n : n;          // always point back toward the camera
            }

            // Nearest opaque surface to this point, searched in a screen-space neighbourhood.
            //
            // A single depth tap only knows what lies along this pixel's own ray, so where
            // terrain occludes the pane the tap returns something far beyond it and the join is
            // missed. The search radius has to cover `width` world units at this depth, which is
            // why the pixel cap is generous — clamping it tightly silently limits how far the
            // contact band can ever reach, regardless of the width that was asked for.
            float NearestSurfaceDistance(float2 positionSS, float3 posRWS, float eyeDepth, float width)
            {
                float pixelsPerMeter = _ScreenSize.y * 0.5 * UNITY_MATRIX_P._m11 / max(eyeDepth, 1e-3);
                float radius = clamp(width * pixelsPerMeter, 1.0, 256.0);

                float best = width;
                float jitter = InterleavedGradientNoise(positionSS, 0) * TWO_PI;

                [unroll]
                for (int i = 0; i < 16; i++)
                {
                    float  a = jitter + TWO_PI * (i / 16.0);
                    // Vary the ring radius so the taps cover the disc rather than only its rim.
                    float  r = radius * (0.25 + 0.75 * frac(i * 0.61803399));
                    float3 p = ScenePositionAt(positionSS + float2(cos(a), sin(a)) * r);
                    best = min(best, distance(p, posRWS));
                }
                return best;
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

                // --- pane network: make overlapping panes read as one continuous wall ---
                if (_PaneNetworkOn > 0.5)
                {
                    const float kSkin = 0.02;

                    // Beyond this pane's trimmed end: the stub that used to poke out past the joint.
                    float own = SdWallSegment(positionAWS, _OwnSegA.xyz, _OwnSegB.xyz,
                                              _OwnSegA.w, _OwnSegB.w, _CornerRadius);
                    clip(kSkin - own);

                    // Buried inside a neighbour: an interior face of the merged wall, which is
                    // what shows up as the two panes crossing through each other.
                    float others = 1e9;
                    int count = (int)_OtherCount;
                    for (int s = 0; s < count; s++)
                    {
                        others = min(others, SdWallSegment(positionAWS,
                            _OtherSegA[s].xyz, _OtherSegB[s].xyz,
                            _OtherSegA[s].w, _OtherSegB[s].w, _CornerRadius));
                    }
                    clip(others + kSkin);
                }

                float2 screenUV = input.positionCS.xy * _ScreenSize.zw;

                float glassEye = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                float sceneDepthRaw = LoadCameraDepth(uint2(input.positionCS.xy));
                float sceneEye = LinearEyeDepth(sceneDepthRaw, _ZBufferParams);
                float behind   = max(sceneEye - glassEye, 0.0);

                // --- contact merge: make the intersection a shared edge, not a cut ---
                float3 sceneP = ScenePositionAt(input.positionCS.xy);
                float3 sceneN = ReconstructSceneNormal(input.positionCS.xy, sceneP, V);

                // Treat the surface under this pixel as a plane and measure the perpendicular
                // distance to it. Unlike a screen-space radius search this has no reach limit,
                // so a wide contact band costs the same as a narrow one, and it stays correct at
                // grazing angles where a raw depth difference blows up.
                float planeDist = abs(dot(input.positionRWS - sceneP, sceneN));

                // Prefer the baked field wherever it covers this point: it is the only source
                // that knows the real 3D distance. The screen-space pair is the fallback for
                // panes with no bake, and is only trustworthy when the glass runs roughly
                // parallel to the surface it meets.
                float sdfDist = SampleContactSdf(positionAWS);
                float nearest = sdfDist >= 0.0
                    ? sdfDist
                    : min(planeDist, NearestSurfaceDistance(input.positionCS.xy, input.positionRWS,
                                                            glassEye, _IntersectionFade));

                float contact = _IntersectionFade > 0.0
                    ? smoothstep(0.0, 1.0, 1.0 - saturate(nearest / _IntersectionFade))
                    : 0.0;

                // Bend the glass normal into the surface it meets, so shading, refraction and the
                // rim all run continuously across the join instead of stopping dead at it.
                N = normalize(lerp(N, sceneN, contact * _ContactNormalMerge));

                // Refraction offset: deviation of the frosted normal from the flat one, in view space.
                float3 frostN = FrostNormal(positionAWS, N);
                float3 devVS  = mul((float3x3)UNITY_MATRIX_V, frostN - N);
                // Stop distorting as the join is approached, or the surface appears to slide
                // against itself right where the two are supposed to read as one.
                float2 refrUV = saturate(screenUV + devVS.xy * (_Distortion * (1.0 - contact)));

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
                // Sharpen into the join: a blurred surface next to a sharp one reads as two
                // separate things no matter how well the alpha is faded.
                blur01 *= 1.0 - contact;
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

                // Dissolve into the surface across the contact band so the two share one edge.
                float alpha = _Opacity * (1.0 - contact * _ContactBlend);

                return float4(glass, saturate(alpha));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
