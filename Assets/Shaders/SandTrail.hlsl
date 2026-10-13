// Furrows in the sand where fish have dragged along the bottom (see SandTrails.cs). Applied by
// SeabedSand.hlsl on top of the sand's own colour and normal.
//
// _SandTrailMap covers the whole seabed: r = when each spot was last disturbed (seconds since the scene
// loaded), g = how deep the furrow was then. A furrow is a shallow dip - its sides tilt the normal so it
// catches the light like a groove - with darker, smoothed-over sand in it, and it fades back to plain
// sand over the fade time.
#ifndef SAND_TRAIL_INCLUDED
#define SAND_TRAIL_INCLUDED

TEXTURE2D(_SandTrailMap);
float4 _SandTrailArea;    // xy = seabed's min corner (world x,z), z = texels per metre, w = 1 while live
float4 _SandTrailParams;  // x = now (s), y = fade time (s), z = how strongly the sides tilt, w = darkening (0..1)
float4 _SandTrailInfo;    // x = texels across, y = how much lighter the sand pushed up along the edges is

float SandTrailTexelDepth(int2 p)
{
    int size = (int)_SandTrailInfo.x;
    p = clamp(p, int2(0, 0), int2(size - 1, size - 1));
    float2 m = LOAD_TEXTURE2D(_SandTrailMap, p).rg;
    float f = saturate(1.0 - (_SandTrailParams.x - m.x) / max(_SandTrailParams.y, 0.01));
    return m.y * f * f;
}

// Depth at a texel-space position, blended between texels after working out each one's fade (blending
// the stored times themselves would cut the furrow's edges into texel-sized steps).
float SandTrailDepth(float2 t)
{
    t -= 0.5;
    int2 p = int2(floor(t));
    float2 f = t - p;
    float d00 = SandTrailTexelDepth(p);
    float d10 = SandTrailTexelDepth(p + int2(1, 0));
    float d01 = SandTrailTexelDepth(p + int2(0, 1));
    float d11 = SandTrailTexelDepth(p + int2(1, 1));
    return lerp(lerp(d00, d10, f.x), lerp(d01, d11, f.x), f.y);
}

// SurfaceNormalWS: the ground's own normal; Albedo / NormalWS: the sand's, changed where there's a furrow.
void SandTrail(float3 PositionWS, float3 SurfaceNormalWS, inout float3 Albedo, inout float3 NormalWS)
{
#ifndef SHADERGRAPH_PREVIEW
    if (_SandTrailArea.w < 0.5)
        return;
    float2 t = (PositionWS.xz - _SandTrailArea.xy) * _SandTrailArea.z;
    if (any(t < 0.0) || any(t > _SandTrailInfo.x))
        return;

    float d = SandTrailDepth(t);
    if (d < 1e-3)
        return; // almost all of the seabed

    // The furrow's sides: slope of the dip, per metre.
    float dl = SandTrailDepth(t - float2(1.0, 0.0));
    float dr = SandTrailDepth(t + float2(1.0, 0.0));
    float dd = SandTrailDepth(t - float2(0.0, 1.0));
    float du = SandTrailDepth(t + float2(0.0, 1.0));
    float2 slope = float2(dr - dl, du - dd) * (_SandTrailArea.z * 0.5);

    // Disturbed sand: the ripples smoothed over, darker down the middle, and lighter along the edges where
    // it was pushed up - a dark core with light rims reads as a groove even under bright caustics.
    float3 n = normalize(lerp(NormalWS, SurfaceNormalWS, saturate(d * 0.8)));
    NormalWS = normalize(n + float3(slope.x, 0.0, slope.y) * _SandTrailParams.z);
    float rim = saturate(length(slope) * 0.35) * (1.0 - d);
    Albedo *= (1.0 - _SandTrailParams.w * d) * (1.0 + _SandTrailInfo.y * rim);
#endif
}

#endif // SAND_TRAIL_INCLUDED
