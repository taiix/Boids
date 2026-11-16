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

        [Header(Hex Force Field)]
        [Toggle(_HEX_FIELD)] _HexField ("Hex Force Field (replaces the frost)", Float) = 0
        [HDR] _HexColor      ("Line Color", Color) = (0.45, 1.1, 1.4, 1)
        _HexSize             ("Cell Size (m)", Float) = 2.5
        _HexLineWidth        ("Line Width (cell fraction)", Range(0.005,0.2)) = 0.035
        _HexBaseAlpha        ("Film Opacity", Range(0,0.5)) = 0.05
        _HexIdle             ("Lines At Rest", Range(0,1)) = 0.04
        _HexPulseStrength    ("Pulse Strength", Range(0,2)) = 0.35
        _HexPulseSpeed       ("Pulse Rise Speed (m/s)", Float) = 5
        _HexPulseSpacing     ("Pulse Spacing (m)", Float) = 45
        _HexPulseWidth       ("Pulse Width (m)", Float) = 5
        _HexProximity        ("Proximity Reach (m)", Float) = 7
        _HexProximityStrength("Proximity Strength", Range(0,4)) = 1.6
        _HexFadeDistance     ("Line Fade Distance (m)", Float) = 70

        [Header(Hex Motion and Shape)]
        _HexDrift            ("Drift (cells per second, xy)", Vector) = (0.04, 0.1, 0, 0)
        _HexWiggle           ("Wiggle (cells)", Range(0,0.5)) = 0.12
        _HexWiggleSpeed      ("Wiggle Speed", Float) = 0.8
        _HexRoundness        ("Shape (0 hexagon, 1 bubble)", Range(0,1)) = 0.3
        _HexMorph            ("Shape Breathing", Range(0,1)) = 0.6
        _HexFineScale        ("Fine Lattice Density", Float) = 3
        _HexFineStrength     ("Fine Lattice Strength", Range(0,1)) = 0.35

        [Header(Hex Interaction)]
        _HexMerge            ("Cell Merge Near Bodies", Range(0,1)) = 0.8
        _HexBulge            ("Bulge Near Bodies (cells)", Range(0,2)) = 0.6
        _HexRipple           ("Ripple Strength", Range(0,2)) = 0.6
        _HexRippleSpeed      ("Ripple Speed (m/s)", Float) = 4
        _HexRippleLength     ("Ripple Wavelength (m)", Float) = 3
        _HexShock            ("Impact Strength", Range(0,4)) = 2
        _HexShockSpeed       ("Impact Ring Speed (m/s)", Float) = 14
        _HexShockLife        ("Impact Ring Life (s)", Float) = 1.6

        [Header(Hex Seams)]
        _HexSeam             ("Seam Glow Where It Meets Things", Range(0,4)) = 1.5
        _HexSeamWidth        ("Seam Width (m)", Float) = 1.5
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

        HLSLINCLUDE
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

            // Bodies near the walls (players, shark, wildlife), set globally by ArenaWalls.
            // xyz is the world position, w the body radius.
            #define MAX_HEX_POKES 16
            float4 _HexPokes[MAX_HEX_POKES];
            float  _HexPokeCount;

            // Recent hits on the walls, also set by ArenaWalls: xyz where, w when (in _HexNow time).
            #define MAX_HEX_IMPACTS 8
            float4 _HexImpacts[MAX_HEX_IMPACTS];
            float  _HexImpactCount;
            float  _HexNow;

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
                float4 _HexColor;
                float  _HexField;
                float  _HexSize;
                float  _HexLineWidth;
                float  _HexBaseAlpha;
                float  _HexIdle;
                float  _HexPulseStrength;
                float  _HexPulseSpeed;
                float  _HexPulseSpacing;
                float  _HexPulseWidth;
                float  _HexProximity;
                float  _HexProximityStrength;
                float  _HexFadeDistance;
                float4 _HexDrift;
                float  _HexWiggle;
                float  _HexWiggleSpeed;
                float  _HexRoundness;
                float  _HexMorph;
                float  _HexFineScale;
                float  _HexFineStrength;
                float  _HexMerge;
                float  _HexBulge;
                float  _HexRipple;
                float  _HexRippleSpeed;
                float  _HexRippleLength;
                float  _HexShock;
                float  _HexShockSpeed;
                float  _HexShockLife;
                float  _HexSeam;
                float  _HexSeamWidth;
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

            // Nearest cell of a hex lattice whose cells are 1 unit flat-to-flat: xy is the offset
            // from the cell centre, zw the cell id.
            float4 HexCell(float2 p)
            {
                const float2 s = float2(1.0, 1.7320508);
                float4 c = floor(float4(p, p - float2(0.5, 1.0)) / s.xyxy) + 0.5;
                float4 h = float4(p - c.xy * s, p - (c.zw + 0.5) * s);
                return dot(h.xy, h.xy) < dot(h.zw, h.zw) ? float4(h.xy, c.xy) : float4(h.zw, c.zw + 0.5);
            }

            // The pane's thickness axis in world space (its local Z), and half its thickness.
            float3 PaneAxis()
            {
                float4x4 m = GetObjectToWorldMatrix();
                return normalize(float3(m._m02, m._m12, m._m22));
            }

            // Distance to the border of a cell whose shape sits between a hexagon (round = 0) and a
            // circle (round = 1). 0 on the border, positive inside, negative in the gaps circles leave.
            float CellEdge(float2 l, float roundness)
            {
                float2 a = abs(l);
                float hex = max(dot(a, float2(0.5, 0.8660254)), a.x);
                return 0.5 - lerp(hex, length(l), roundness);
            }

            // Anti-aliased outline of a cell border, faded out before the lattice gets finer than a few
            // pixels, where it would only shimmer.
            float CellLine(float edge, float width, float2 p)
            {
                float fw = max(fwidth(edge), 1e-5);
                float lineMask = 1.0 - smoothstep(width, width + fw * 1.5, abs(edge));
                float cellPixels = 1.0 / max(length(float2(ddx(p.x), ddy(p.x))), 1e-5);
                return lineMask * saturate((cellPixels - 6.0) / 12.0);
            }

            // The force-field look: an almost clear film over two drifting, wobbling lattices of cells
            // that breathe between hexagons and bubbles. Bodies close to the wall push it out like a
            // membrane, merge the cells around them into one glowing patch and send ripples out; a
            // body hitting the wall fast sends a bright shockwave ring across it.
            float4 HexField(float3 positionAWS, float3 flatN, float3 N, float3 V, float glassEye, float behind, float contact)
            {
                // Built from the pane's own thickness axis, not the face normal: the front and back faces
                // (and the seam pass) then all draw the very same lattice, moving the same way.
                float3 T, B;
                SurfaceFrame(PaneAxis(), T, B);
                float size = max(_HexSize, 0.05);
                float2 p = float2(dot(positionAWS, T), dot(positionAWS, B)) / size;
                float t = _TimeParameters.x;

                // --- seams: wherever the wall meets something, rocks or a body pushing through ---
                // `contact` covers the scenery (baked field or screen search); the depth gap behind this
                // pixel, turned into a distance from the pane, catches anything else, moving or not.
                float dynGap = behind * max(abs(dot(flatN, V)), 0.25);
                float seam = max(contact, 1.0 - saturate(dynGap / max(_HexSeamWidth, 0.05)));
                float seamCore = seam * seam * seam;

                // --- bodies near the wall: bulge, merge glow, ripples ---
                float2 push = 0.0;
                float near = 0.0;
                float ripple = 0.0;
                int count = (int)_HexPokeCount;
                for (int i = 0; i < count; i++)
                {
                    float3 body = _HexPokes[i].xyz;
                    float2 q = float2(dot(body, T), dot(body, B)) / size;
                    float2 away = p - q;
                    float r = length(away);                                   // in cells, along the wall
                    float d = length(float3(away * size, dot(body - positionAWS, flatN)));  // metres
                    float reach = _HexProximity + _HexPokes[i].w;
                    float k = smoothstep(reach, 0.0, d);
                    near = max(near, k);

                    // Pushed out from under the body, strongest a cell or two from it.
                    push += away / max(r, 1e-3) * (k * k * _HexBulge * saturate(r));

                    // Rings running out from the body across the wall.
                    float wave = sin((d - t * _HexRippleSpeed) * TWO_PI / max(_HexRippleLength, 0.1));
                    ripple += pow(saturate(wave), 6.0) * smoothstep(reach * 2.2, reach * 0.3, d);
                }
                near *= _HexProximityStrength;
                ripple *= _HexRipple;

                // --- impacts: a ring that runs out from where something hit the wall ---
                float shock = 0.0;
                int impacts = (int)_HexImpactCount;
                for (int j = 0; j < impacts; j++)
                {
                    float age = _HexNow - _HexImpacts[j].w;
                    if (age < 0.0 || age > _HexShockLife) continue;
                    float3 at = _HexImpacts[j].xyz;
                    float2 q = float2(dot(at, T), dot(at, B)) / size;
                    float2 away = p - q;
                    float d = length(away) * size;
                    float front = age * _HexShockSpeed;
                    float fade = 1.0 - age / max(_HexShockLife, 0.01);
                    float ringK = exp(-pow((d - front) / 1.2, 2.0)) * fade * fade;
                    shock += ringK;
                    push += away / max(length(away), 1e-3) * ringK * 0.35;
                    // a flash where it hit
                    shock += exp(-d * d * 0.15) * pow(fade, 6.0);
                }
                shock *= _HexShock;

                // --- the lattice moves: drift, a slow wobble, and whatever the bodies push ---
                float ws = t * _HexWiggleSpeed;
                float2 wobble = float2(sin(p.y * 0.9 + ws) + 0.5 * sin(p.y * 2.3 - ws * 1.7 + p.x * 0.7),
                                       sin(p.x * 0.8 - ws * 1.2) + 0.5 * sin(p.x * 2.1 + ws * 1.4 - p.y * 0.6));
                float2 pa = p + _HexDrift.xy * t + wobble * (_HexWiggle * (1.0 + seam * 2.0)) + push;

                float4 cellA = HexCell(pa);
                float hashA = Hash21(cellA.zw);
                // Every cell breathes between a hexagon and a bubble at its own pace; near a body they
                // swell round.
                float roundness = saturate(_HexRoundness + _HexMorph * 0.5 * sin(t * 0.7 + hashA * 6.2832) + near * 0.4);
                float edgeA = CellEdge(cellA.xy, roundness);
                float lineA = CellLine(edgeA, _HexLineWidth, pa);

                // A finer lattice turned 30 degrees and drifting the other way, laid over the first.
                float2 pb = pa * max(_HexFineScale, 1.0);
                pb = float2(pb.x * 0.8660254 - pb.y * 0.5, pb.x * 0.5 + pb.y * 0.8660254) - _HexDrift.yx * t * 1.7;
                float4 cellB = HexCell(pb);
                float lineB = CellLine(CellEdge(cellB.xy, 1.0 - roundness), _HexLineWidth * 1.4, pb);

                float distFade = saturate(1.0 - glassEye / max(_HexFadeDistance, 1.0));
                lineA *= distFade;
                lineB *= distFade;

                float twinkle = 0.75 + 0.25 * sin(t * 1.7 + hashA * 40.0);

                // Bands of energy rising up the walls; same height everywhere, so they stay in step
                // across the corners.
                float band = frac((positionAWS.y - t * _HexPulseSpeed) / max(_HexPulseSpacing, 0.1));
                float halfW = 0.5 * _HexPulseWidth / max(_HexPulseSpacing, 0.1);
                float pulse = smoothstep(halfW, 0.0, abs(band - 0.5)) * _HexPulseStrength;

                // Close to a body the borders dissolve, so its cells merge into one glowing patch whose
                // outline is the brightest part.
                float merge = saturate(max(near, seam * 0.8) * _HexMerge);
                float rim = saturate(near * 2.0) * (1.0 - merge);
                float energy = ripple + shock;

                float lineGlow = lineA * (_HexIdle * twinkle + pulse + near * (1.0 - merge) + rim + energy)
                               * (1.0 - merge * 0.85);
                float fineGlow = lineB * _HexFineStrength * (_HexIdle * 0.6 + pulse * 0.5 + near + energy * 0.6);
                float fill = merge * merge * 0.35 * (0.7 + 0.3 * sin(t * 3.0 + hashA * 6.2832))
                           + energy * 0.06 * distFade;

                // The seam itself: a soft glow that washes over whatever the wall meets, so the two read
                // as one surface, with a bright line where they actually cross.
                float seamGlow = (seam * seam * 0.15 + seamCore * 0.3) * _HexSeam * (0.85 + 0.15 * twinkle);
                lineGlow += lineA * seam * 0.6 * _HexSeam;

                float fresnel = pow(saturate(1.0 - saturate(dot(N, V))), _RimPower) * _RimStrength;
                float glow = lineGlow + fineGlow + fill + seamGlow;

                // The film and rim dissolve into the scenery the way the frost does; the light stays, so
                // the join glows instead of just stopping.
                float filmFade = 1.0 - seam * _ContactBlend;
                float film = (_HexBaseAlpha + fresnel) * filmFade;
                float coverage = max(saturate(film + glow), 1e-3);

                // The blend multiplies colour by alpha, so divide the light out by it: the lines then add
                // their full (HDR, bloom-feeding) brightness while the film only tints what is behind.
                float3 rgb = (_HexColor.rgb * glow + _RimColor.rgb * fresnel * filmFade) / coverage
                           + _Tint.rgb * _TintStrength;

                return float4(rgb, coverage * _Opacity);
            }


            // Seam pass: the glass can only light pixels where the glass itself is visible, but where the
            // wall meets the seabed, a rock or a body, that surface usually hides the glass right at the
            // join. This pass covers the whole pane regardless of depth, finds the surface under each pixel,
            // and if it lies within the seam width of the glass, lights it with the same glow and lattice,
            // so the wall and what it touches blend into one another.
            float4 FragSeam(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            #ifndef _HEX_FIELD
                clip(-1.0);
                return 0;
            #else
                float deviceDepth = LoadCameraDepth(uint2(input.positionCS.xy));
            #if UNITY_REVERSED_Z
                clip(deviceDepth - 1e-7);                  // nothing drawn there: open water
            #else
                clip(0.9999999 - deviceDepth);
            #endif
                float3 sceneRWS = ScenePositionAt(input.positionCS.xy);

                float4x4 m = GetObjectToWorldMatrix();
                float3 origin = float3(m._m03, m._m13, m._m23);
                float3 ax = float3(m._m00, m._m10, m._m20);
                float3 ay = float3(m._m01, m._m11, m._m21);
                float3 az = float3(m._m02, m._m12, m._m22);
                float3 n = normalize(az);
                float halfT = length(az) * 0.5;

                float3 rel = sceneRWS - origin;
                float across = dot(rel, n);
                float camAcross = dot(GetCurrentViewPosition() - origin, n);
                // Only the camera's side: past the glass, the surface shows through it and the main pass
                // already lights it.
                clip(across * sign(camAcross) + halfT * 0.5);

                float width = max(_HexSeamWidth, 0.05) * 1.5;
                float gap = max(abs(across) - halfT, 0.0);
                float k = 1.0 - saturate(gap / width);
                k *= k;

                // Only beside the pane itself, fading out past its ends and top.
                float u = abs(dot(rel, normalize(ax))) - length(ax) * 0.5;
                float v = abs(dot(rel, normalize(ay))) - length(ay) * 0.5;
                k *= saturate(1.0 - max(u, v) / width);
                clip(k - 1e-3);

                // The same lattice as the glass, carried on across the surface it meets.
                float3 posA = GetAbsolutePositionWS(sceneRWS);
                float3 T, B;
                SurfaceFrame(n, T, B);
                float size = max(_HexSize, 0.05);
                // Fold the lattice off the glass onto the surface: distance out from the pane carries on
                // as distance down it, so the cells bend round the join instead of smearing into stripes.
                float2 p = float2(dot(posA, T), dot(posA, B) - gap) / size;
                float t = _TimeParameters.x;
                float ws = t * _HexWiggleSpeed;
                float2 wobble = float2(sin(p.y * 0.9 + ws) + 0.5 * sin(p.y * 2.3 - ws * 1.7 + p.x * 0.7),
                                       sin(p.x * 0.8 - ws * 1.2) + 0.5 * sin(p.x * 2.1 + ws * 1.4 - p.y * 0.6));
                float2 pa = p + _HexDrift.xy * t + wobble * (_HexWiggle * 3.0);
                float4 cell = HexCell(pa);
                float lineMask = CellLine(CellEdge(cell.xy, _HexRoundness), _HexLineWidth * 1.5, pa);

                float eye = length(sceneRWS - GetCurrentViewPosition());
                float distFade = saturate(1.0 - eye / max(_HexFadeDistance, 1.0));
                float core = exp(-gap * gap * 8.0);
                float shimmer = 0.85 + 0.15 * sin(t * 2.0 + Hash21(cell.zw) * 6.2832);

                float glow = (k * 0.1 + lineMask * k * 0.45 + core * 0.18) * _HexSeam * shimmer
                           * lerp(0.35, 1.0, distFade);
                return float4(_HexColor.rgb * glow, 0.0);
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
                float3 flatN = normalize(input.normalWS);
                float3 N = flatN * (isFrontFace ? 1.0 : -1.0);
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

            #ifdef _HEX_FIELD
                return HexField(positionAWS, flatN, N, V, glassEye, behind, contact);
            #endif

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
            #pragma shader_feature_local _HEX_FIELD
            #pragma vertex Vert
            #pragma fragment Frag

            ENDHLSL
        }

        // Lights whatever the wall meets (see FragSeam). Additive, and ignores depth on purpose.
        Pass
        {
            Name "SeamGlow"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend One One
            ZWrite Off
            ZTest Always
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch
            #pragma multi_compile_instancing
            #pragma shader_feature_local _HEX_FIELD
            #pragma vertex Vert
            #pragma fragment FragSeam
            ENDHLSL
        }
    }

    Fallback Off
}
