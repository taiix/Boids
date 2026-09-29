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
    Albedo = saturate(color * Tint.rgb);

    // Detail normal onto the surface normal (a top-down "whiteout" blend), in world space.
    n.xy *= NormalStrength * lerp(1.0 - PatchRipple, 1.0, ripple);
    float3 t = float3(n.xy + NormalWS.xz, abs(n.z) * NormalWS.y);
    NormalOut = normalize(t.xzy);
}

#endif
