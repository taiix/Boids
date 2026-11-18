// Seabed sand for Seabed.shadergraph (Custom Function node, File mode, function "SeabedSand").
//
// The sand texture repeats every few metres, and its blotches line up into a visible grid across
// a 300 m seabed. This hex-tiles it instead: world XZ is cut into a triangle lattice, every lattice
// vertex samples the texture with its own random rotation and offset, and each pixel blends the
// three vertices around it. No two neighbouring patches share an orientation, so there is no
// grid left to see. On top, low-frequency world noise drifts the tone, hue and ripple strength so
// the floor reads as a natural seabed from far away, not one evenly-printed carpet.
//
// Normals come out in WORLD space: the terrain mesh has no tangents, so a tangent-space normal
// map can't be lit correctly on it.

#ifndef SEABED_SAND_INCLUDED
#define SEABED_SAND_INCLUDED

#include "Assets/Shaders/SandTrail.hlsl" // furrows where fish drag along the bottom

// Integer hash (PCG): stable per lattice vertex, identical on every machine and frame.
uint SeabedPcg(uint v)
{
    uint state = v * 747796405u + 2891336453u;
    uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
    return (word >> 22u) ^ word;
}

float3 SeabedRandom3(int2 p)
{
    uint h = SeabedPcg(asuint(p.x) + SeabedPcg(asuint(p.y)));
    return float3(h & 0x3FFu, (h >> 10) & 0x3FFu, (h >> 20) & 0x3FFu) / 1023.0;
}

float SeabedValueNoise(float2 p)
{
    int2 i = int2(floor(p));
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    float a = SeabedRandom3(i).x;
    float b = SeabedRandom3(i + int2(1, 0)).x;
    float c = SeabedRandom3(i + int2(0, 1)).x;
    float d = SeabedRandom3(i + int2(1, 1)).x;
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

float2 SeabedRotate(float2 v, float s, float c)
{
    return float2(c * v.x - s * v.y, s * v.x + c * v.y);
}

// The three lattice vertices around st, and their barycentric weights. The lattice is skewed
// so its cells are equilateral triangles: the vertex regions blend into hexagons.
void SeabedTriangleGrid(float2 st, out float3 w, out int2 v1, out int2 v2, out int2 v3)
{
    const float2x2 toSkewed = float2x2(1.0, 0.0, -0.57735027, 1.15470054);
    float2 skewed = mul(toSkewed, st);
    int2 baseId = int2(floor(skewed));
    float3 t = float3(frac(skewed), 0.0);
    t.z = 1.0 - t.x - t.y;
    float s = step(0.0, -t.z);
    float s2 = 2.0 * s - 1.0;
    w = float3(-t.z * s2, s - t.y * s2, s - t.x * s2);
    v1 = baseId + int2(s, s);
    v2 = baseId + int2(s, 1.0 - s);
    v3 = baseId + int2(1.0 - s, s);
}

// Lattice vertex -> its centre in texture uv, for rotating around it.
float2 SeabedVertexCentre(int2 v, float latticeScale)
{
    const float2x2 fromSkewed = float2x2(1.0, 0.0, 0.5, 0.8660254);
    return mul(fromSkewed, float2(v)) / latticeScale;
}

// One vertex's view of the texture: rotated by up to +-Rotation half-turns around its centre,
// then shifted somewhere random. Gradients are rotated with it so mips stay seamless.
void SeabedVertexSample(UnityTexture2D baseMap, UnityTexture2D normalMap, float2 uv, float2 dx, float2 dy,
                        int2 v, float latticeScale, float rotation,
                        out float4 color, out float3 normalRot)
{
    float3 r = SeabedRandom3(v);
    float angle = (r.x * 2.0 - 1.0) * PI * rotation;
    float s, c;
    sincos(angle, s, c);

    float2 centre = SeabedVertexCentre(v, latticeScale);
    float2 uvv = SeabedRotate(uv - centre, s, c) + centre + r.yz * 16.0;
    float2 dxv = SeabedRotate(dx, s, c);
    float2 dyv = SeabedRotate(dy, s, c);

    color = SAMPLE_TEXTURE2D_GRAD(baseMap.tex, baseMap.samplerstate, uvv, dxv, dyv);
    float3 n = UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(normalMap.tex, normalMap.samplerstate, uvv, dxv, dyv), 1.0);
    // Back from this vertex's rotated texture space to world XZ.
    normalRot = float3(SeabedRotate(n.xy, -s, c), n.z);
}

// ------------------------------------------------------------------ ripples
//
// Wave ripples worked into the sand, as on a real seabed: crests a hand-span or so apart (sharp crests,
// broad troughs, one side a little steeper than the other), running roughly one way but wandering -
// bending, forking and ending where two ripple trains meet - with long low swells under them. Heights
// are worked out here (no texture), so they never tile; their slopes tilt the normal and the troughs
// hold a little shade.
//
// Not the same everywhere: across the seabed (patches of 20-50 m) the ripples run small and tight in
// one stretch, wide in another, each its own way; there are smooth patches with none, and here and there
// a second set across the first into a lattice. The crests themselves rise and dip along their length,
// break up and end. And the surroundings shape the sand (_SeabedMask, baked by SeabedRipples.cs): next to
// the reef, rocks and coral it's smoother, darker and gritty with grains and bits of shell; hollows hold
// flatter, darker sand with debris collected in it; raised ground has sharper ripples.
//
// Tuned from SeabedRipples.cs (globals). With none set (another scene, the graph preview) the defaults
// below are used - the same as that component's.

float4 _SandRipple;   // x = crest spacing (m), y = strength, z = crest sharpness (>= 1), w = 1 when set
float4 _SandRipple2;  // x = direction (degrees from +X), y = trough shade 0..1, z = swell strength, w = patchiness 0..1
float4 _SandRipple3;  // x = variety, y = flat patches, z = crest breakup, w = cross ripples (all 0..1)
float4 _SandRipple4;  // x = reef influence, y = hollow influence (0..1)

// The sand's surroundings, over the whole seabed: r = how close scenery (reef, rock, coral) is, 0..1;
// g = the ground relative to its neighbourhood, 0 = a hollow .. 0.5 = level .. 1 = raised.
TEXTURE2D(_SeabedMask);
float4 _SeabedMaskArea;   // xy = the seabed's min corner (world x, z), z = texels per metre, w = texels across (0 = none)

// Value noise and its gradient: x = 0..1, yz = d/dp. Quintic easing, so its curvature has no seams
// either (cubic left creases where ripple crests visibly kinked at the noise's cell edges).
float3 SeabedNoiseGrad(float2 p)
{
    int2 i = int2(floor(p));
    float2 f = frac(p);
    float2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    float2 du = 30.0 * f * f * (f - 1.0) * (f - 1.0);
    float a = SeabedRandom3(i).x;
    float b = SeabedRandom3(i + int2(1, 0)).x;
    float c = SeabedRandom3(i + int2(0, 1)).x;
    float d = SeabedRandom3(i + int2(1, 1)).x;
    float k1 = b - a, k2 = c - a, k3 = a - b - c + d;
    return float3(a + k1 * u.x + k2 * u.y + k3 * u.x * u.y,
                  du * float2(k1 + k3 * u.y, k2 + k3 * u.x));
}

// The surroundings at xz: x = near scenery 0..1, y = -1 (hollow) .. +1 (raised). Bilinear by hand.
float2 SeabedMaskAt(float2 xz)
{
#ifndef SHADERGRAPH_PREVIEW
    if (_SeabedMaskArea.w < 1.0)
        return float2(0.0, 0.0);
    int size = (int)_SeabedMaskArea.w;
    float2 t = (xz - _SeabedMaskArea.xy) * _SeabedMaskArea.z - 0.5;
    int2 p = int2(floor(t));
    float2 f = t - p;
    int2 p0 = clamp(p, int2(0, 0), int2(size - 1, size - 1));
    int2 p1 = clamp(p + 1, int2(0, 0), int2(size - 1, size - 1));
    float2 a = LOAD_TEXTURE2D(_SeabedMask, int2(p0.x, p0.y)).rg;
    float2 b = LOAD_TEXTURE2D(_SeabedMask, int2(p1.x, p0.y)).rg;
    float2 c = LOAD_TEXTURE2D(_SeabedMask, int2(p0.x, p1.y)).rg;
    float2 d = LOAD_TEXTURE2D(_SeabedMask, int2(p1.x, p1.y)).rg;
    float2 m = lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
    return float2(m.x, m.y * 2.0 - 1.0);
#else
    return float2(0.0, 0.0);
#endif
}

float2 SeabedDir(float angle) { return float2(cos(angle), sin(angle)); }

// One train of ripples: crests `spacing` apart across `dir`, their line bent by noise. x = height
// 0 (trough) .. 1 (crest), yz = its slope per metre for a ripple as tall as it is long (scaled later).
// breakup 0..1: the crests rise and dip along their length, break up and end.
float3 SeabedRippleTrain(float2 xz, float2 dir, float spacing, float sharp, float seed, float breakup)
{
    float k = 6.2831853 / spacing;
    // The crests' line bent at three scales: long sweeps, wiggles, and a little unevenness crest to crest.
    float s1 = spacing * 4.0, s2 = spacing * 1.6, s3 = spacing * 0.8;
    float3 w1 = SeabedNoiseGrad(xz / s1 + seed);
    float3 w2 = SeabedNoiseGrad(xz / s2 + seed * 1.7 + 13.1);
    float3 w3 = SeabedNoiseGrad(xz / s3 + seed * 2.3 + 5.7);
    // And a broad sweep over tens of metres: the way the crests run swings slowly across the seabed (up to
    // about 25 degrees). Done by bending the phase, never by turning `dir` from place to place - the phase is
    // measured from the world's origin hundreds of metres away, so even a slow turn there packs in crests.
    const float s0 = 40.0;
    float3 w0 = SeabedNoiseGrad(xz / s0 + seed * 0.37);
    float c0 = 0.24 * s0 / spacing;
    // The phase counted in whole cycles and wrapped to one: out here it runs to thousands of radians, where
    // the GPU's sin / cos are far off.
    float cycles = dot(xz, dir) / spacing + c0 * w0.x + (7.0 * w1.x + 3.5 * w2.x + 1.0 * w3.x) / 6.2831853;
    float phase = 6.2831853 * frac(cycles);
    float2 gPhase = k * dir + 6.2831853 * c0 * w0.yz / s0 + 7.0 * w1.yz / s1 + 3.5 * w2.yz / s2 + 1.0 * w3.yz / s3;

    // Leaning crests (one face steeper) and sharp crests over broad troughs.
    const float lean = 0.3;
    float ps = phase + lean * sin(phase);
    float dps = 1.0 + lean * cos(phase);
    float u = max(0.5 + 0.5 * cos(ps), 1e-4);
    float h = pow(u, sharp);
    float2 slope = (sharp * pow(u, sharp - 1.0) * (-0.5 * sin(ps)) * dps * spacing) * gPhase;

    // Crests rising and dipping along their length: noise stretched along the crest (2.4 spacings) and
    // tight across it, so a crest fades out in one place and stands up again further along.
    [branch] if (breakup > 0.001)
    {
        float2 along = float2(-dir.y, dir.x);
        float la = spacing * 2.4, lc = spacing * 0.9;
        float3 nb = SeabedNoiseGrad(float2(dot(xz, along) / la, dot(xz, dir) / lc) + seed * 0.71 + 3.3);
        float t = saturate((nb.x - 0.18) / 0.42);
        float dt = (nb.x > 0.18 && nb.x < 0.6) ? 6.0 * t * (1.0 - t) / 0.42 : 0.0;
        t = t * t * (3.0 - 2.0 * t);
        float m = lerp(1.0, 0.1 + 0.9 * t, breakup);
        float2 dm = (breakup * 0.9 * dt) * (nb.y * along / la + nb.z * dir / lc) * spacing;
        slope = slope * m + h * dm;
        h *= m;
    }
    return float3(h, slope);
}

// The ripples here. Height 0..1; Slope (m per m, world XZ) to tilt the normal by; Amount 0..1 (how much
// ripple shows); Shade (how much darker the troughs are); Near (scenery close by, 0..1, after its
// influence); Hollow / Raised (0..1, after their influence). Pixel = metres per pixel here.
void SeabedRippleField(float3 positionWS, float3 normalWS, float pixel,
                       out float height, out float2 slope, out float amount, out float shade,
                       out float near, out float hollow, out float raised)
{
    float4 r1 = _SandRipple, r2 = _SandRipple2, r3 = _SandRipple3, r4 = _SandRipple4;
    if (r1.w < 0.5)
    {
        r1 = float4(0.6, 2.4, 1.8, 0.0);
        r2 = float4(25.0, 0.3, 1.0, 0.3);
        r3 = float4(0.7, 0.4, 0.6, 0.4);
        r4 = float4(0.65, 0.7, 0.0, 0.0);
    }
    float spacing = max(r1.x, 0.05);
    float2 xz = positionWS.xz;
    float variety = saturate(r3.x), flatPatches = saturate(r3.y), breakup = saturate(r3.z), crossing = saturate(r3.w);

    // The surroundings: smoother sand by the scenery and in hollows, sharper ripples on raised ground.
    float2 mk = SeabedMaskAt(xz);
    near = mk.x * saturate(r4.x);
    hollow = smoothstep(0.3, 1.0, -mk.y) * saturate(r4.y);   // only real hollows and rises: the rest is level
    raised = smoothstep(0.3, 1.0, mk.y) * saturate(r4.y);
    float sharp = max(r1.z * (1.0 + 0.5 * raised - 0.3 * hollow), 1.0);

    // Stretches of the seabed (20-50 m) with their own ripples: the usual, small tight ones, or wide ones,
    // each running its own way. Each train keeps one size and direction; the stretches blend between them.
    float ang = radians(r2.x);
    float wSmall = smoothstep(0.58, 0.74, SeabedValueNoise(xz / 42.0 + 3.7)) * variety;
    float wWide = smoothstep(0.58, 0.74, SeabedValueNoise(xz / 36.0 + 61.2)) * variety * (1.0 - wSmall);
    float wBase = 1.0 - wSmall - wWide;

    float h = 0.0;
    float2 g = 0.0;
    [branch] if (wBase > 0.001)
    {
        float3 a = SeabedRippleTrain(xz, SeabedDir(ang), spacing, sharp, 3.1, breakup);
        float3 b = SeabedRippleTrain(xz, SeabedDir(ang + 0.5), spacing * 1.15, sharp, 57.9, breakup);
        // Small patches of one train or the other: where they meet, crests fork, break up and end.
        float ms = spacing * 5.0;
        float3 m = SeabedNoiseGrad(xz / ms + 21.7);
        float mt = saturate((m.x - 0.35) / 0.3);
        float dmt = (m.x > 0.35 && m.x < 0.65) ? 6.0 * mt * (1.0 - mt) / 0.3 : 0.0;
        mt = mt * mt * (3.0 - 2.0 * mt);
        h += wBase * lerp(a.x, b.x, mt);
        g += wBase * (lerp(a.yz, b.yz, mt) + (b.x - a.x) * dmt * m.yz / ms * spacing);
    }
    [branch] if (wSmall > 0.001)
    {
        float3 s = SeabedRippleTrain(xz, SeabedDir(ang - 0.6), spacing * 0.62, sharp * 1.15, 17.3, breakup);
        h += wSmall * s.x;
        g += wSmall * s.yz;
    }
    [branch] if (wWide > 0.001)
    {
        float3 w = SeabedRippleTrain(xz, SeabedDir(ang + 0.85), spacing * 1.55, sharp, 33.9, breakup);
        h += wWide * w.x;
        g += wWide * w.yz;
    }
    // Here and there a second set across the first: a lattice of ripples.
    float wCross = smoothstep(0.66, 0.82, SeabedValueNoise(xz / 28.0 + 44.4)) * crossing;
    [branch] if (wCross > 0.001)
    {
        float3 c = SeabedRippleTrain(xz, SeabedDir(ang + 1.45), spacing * 1.3, sharp, 71.1, breakup);
        float k = 0.75 * wCross;
        h = (h + k * c.x) / (1.0 + k);
        g = (g + k * c.yz) / (1.0 + k);
    }
    height = h;

    // How much ripple shows: patchy, smooth patches with none, none on steep ground, calmer by the scenery
    // and in hollows - and faded out where a pixel is too coarse to show them (else they shimmer).
    amount = lerp(1.0, smoothstep(0.2, 0.7, SeabedValueNoise(xz / 18.0 + 71.3)), saturate(r2.w));
    amount *= 1.0 - flatPatches * smoothstep(0.34, 0.2, SeabedValueNoise(xz / 30.0 + 12.9));
    amount *= smoothstep(0.7, 0.92, normalWS.y);
    amount *= (1.0 - 0.85 * near) * (1.0 - 0.5 * hollow);
    float finest = spacing * lerp(1.0, 0.62, wSmall);
    amount *= 1.0 - smoothstep(0.12, 0.4, pixel / finest);

    // Ripples about 15% as tall as they are long (times strength); long swells a few metres apart under them.
    slope = g * (0.15 * r1.y * amount);
    float swellSpacing = 4.5;
    float3 swell = SeabedRippleTrain(xz, SeabedDir(ang - 0.25), swellSpacing, 1.0, 91.3, 0.0);
    slope += swell.yz / swellSpacing * (0.12 * r2.z * (1.0 - smoothstep(0.1, 0.35, pixel / swellSpacing)));
    shade = saturate(r2.y);
}

// Grit: grains a few centimetres across scattered over the sand - dark grains and pale bits of shell -
// `density` of the cells holding one. Returns the colour to multiply by. Fades out where a pixel is
// bigger than a grain.
float3 SeabedGrit(float2 xz, float density, float pixel)
{
    float3 result = float3(1.0, 1.0, 1.0);
    float fade = 1.0 - smoothstep(0.015, 0.05, pixel);
    [branch] if (density > 0.001 && fade > 0.0)
    {
        float2 gp = xz / 0.05;
        int2 cell = int2(floor(gp));
        float3 r = SeabedRandom3(cell + int2(9173, 3311));
        float2 at = 0.25 + r.xy * 0.5;
        float rad = 0.1 + 0.18 * frac(r.x * 7.13 + r.y * 3.71);   // grains of all sizes
        float grain = smoothstep(rad, rad * 0.45, length(frac(gp) - at)) * step(r.z, density);
        float3 tint = r.z < density * 0.65 ? float3(0.55, 0.52, 0.5) : float3(1.25, 1.22, 1.15);
        result = lerp(result, tint, grain * fade);
    }
    return result;
}

void SeabedSand_float(UnityTexture2D BaseMap, UnityTexture2D NormalMap, float3 PositionWS, float3 NormalWS,
                      float4 Tint, float TileSize, float Rotation, float BlendContrast, float NormalStrength,
                      float PatchScale, float PatchTone, float PatchRipple,
                      out float3 Albedo, out float3 NormalOut)
{
    // Texture uv in world metres: one texture repeat per TileSize, u along +X, v along +Z.
    float2 uv = PositionWS.xz / max(TileSize, 1e-3);
    float2 dx = ddx(uv);
    float2 dy = ddy(uv);

    // About 3.5 hexes per texture repeat: small enough that a patch never shows a whole blotch.
    const float latticeScale = 3.4641016; // 2 * sqrt(3)
    float3 w;
    int2 v1, v2, v3;
    SeabedTriangleGrid(uv * latticeScale, w, v1, v2, v3);

    float4 c1, c2, c3;
    float3 n1, n2, n3;
    SeabedVertexSample(BaseMap, NormalMap, uv, dx, dy, v1, latticeScale, Rotation, c1, n1);
    SeabedVertexSample(BaseMap, NormalMap, uv, dx, dy, v2, latticeScale, Rotation, c2, n2);
    SeabedVertexSample(BaseMap, NormalMap, uv, dx, dy, v3, latticeScale, Rotation, c3, n3);

    // Favour the brighter sample where patches overlap, then sharpen: a plain average of three
    // textures washes out into a flat, blurry grey.
    float3 lum = float3(dot(c1.rgb, float3(0.2126, 0.7152, 0.0722)),
                        dot(c2.rgb, float3(0.2126, 0.7152, 0.0722)),
                        dot(c3.rgb, float3(0.2126, 0.7152, 0.0722)));
    float3 W = w * lerp(1.0, lum / max(dot(lum, w), 1e-4), 0.5);
    W = pow(max(W, 1e-5), BlendContrast);
    W /= W.x + W.y + W.z;

    float3 color = c1.rgb * W.x + c2.rgb * W.y + c3.rgb * W.z;
    float3 n = n1 * W.x + n2 * W.y + n3 * W.z;

    // Drifts across the seabed: lighter and darker sand, a touch warmer or cooler, and stretches
    // where the ripples flatten out.
    float2 p = PositionWS.xz / max(PatchScale, 1e-3);
    float tone = SeabedValueNoise(p) * 0.6 + SeabedValueNoise(p * 2.03 + 17.1) * 0.3 + SeabedValueNoise(p * 4.1 + 5.3) * 0.1;
    tone = smoothstep(0.15, 0.85, tone);
    float hue = SeabedValueNoise(p * 0.61 + 41.3);
    float ripple = SeabedValueNoise(p * 0.77 + 93.7);

    color *= lerp(1.0 - PatchTone, 1.0 + PatchTone, tone);
    color *= lerp(float3(1.03, 1.0, 0.95), float3(0.96, 1.0, 1.04), hue);

    // Wave ripples: shade in the troughs.
    float2 fw = fwidth(PositionWS.xz);
    float pixel = max(fw.x, fw.y);
    float rippleHeight, rippleAmount, rippleShade, near, hollow, raised;
    float2 rippleSlope;
    SeabedRippleField(PositionWS, NormalWS, pixel, rippleHeight, rippleSlope, rippleAmount, rippleShade, near, hollow, raised);
    color *= lerp(1.0, lerp(1.0 - rippleShade, 1.0, rippleHeight), rippleAmount);

    // The surroundings: darker, gritty sand by the scenery; darker sand with debris in the hollows; a little
    // lighter on raised ground.
    color *= (1.0 - 0.16 * near) * (1.0 - 0.12 * hollow) * (1.0 + 0.06 * raised);
    color *= SeabedGrit(PositionWS.xz, 0.05 + 0.35 * near + 0.25 * hollow, pixel);
    Albedo = saturate(color * Tint.rgb);

    // Detail normal onto the surface normal (a top-down "whiteout" blend), in world space. The texture's
    // own bumps are toned down where the ripples are strong, so the ripples read cleanly.
    n.xy *= NormalStrength * lerp(1.0 - PatchRipple, 1.0, ripple) * lerp(1.0, 0.6, rippleAmount);
    n.xy -= rippleSlope;
    float3 t = float3(n.xy + NormalWS.xz, abs(n.z) * NormalWS.y);
    NormalOut = normalize(t.xzy);

    // Furrows dragged through the sand by fish (SandTrail.hlsl).
    SandTrail(PositionWS, NormalWS, Albedo, NormalOut);
}

#endif
