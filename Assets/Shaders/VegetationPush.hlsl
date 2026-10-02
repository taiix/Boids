// Plant bending for swimmers (see VegetationPushMap.cs). Used by M_Seabed as a Custom Function node,
// after its wind sway: plants are shoved out of a swimmer's way instead of being swum through.
//
// _VegPushMap is a top-down map around the camera. Each texel: rg = which way and how far (m) a plant
// standing there must move to clear the body at its widest, b = height of the body's axis, a = body radius.
// Each vertex clears the body's surface at its own height (the body is narrower above and below its
// middle), the stalk above the body stays leaning over, and the base stays rooted.
#ifndef VEGETATION_PUSH_INCLUDED
#define VEGETATION_PUSH_INCLUDED

TEXTURE2D(_VegPushMap);
SAMPLER(sampler_VegPushMap);
float4 _VegPushArea;    // xy = map centre (world x,z), z = map size (m), w = 1 while the map is live
float4 _VegPushParams;  // x = largest push (m), y = how far above a body a stalk stays pushed (m),
                        // z = height over which the base stiffens (m)

void VegetationPush_float(float3 PositionOS, float Strength, out float3 Out)
{
    Out = PositionOS;
#ifndef SHADERGRAPH_PREVIEW
    if (_VegPushArea.w < 0.5 || Strength <= 0.0)
        return;

    float3 posWS = GetAbsolutePositionWS(TransformObjectToWorld(PositionOS));
    float3 rootWS = GetAbsolutePositionWS(TransformObjectToWorld(float3(0.0, 0.0, 0.0)));

    float2 uv = (posWS.xz - _VegPushArea.xy) / _VegPushArea.z + 0.5;
    if (any(uv < 0.0) || any(uv > 1.0))
        return;

    float4 m = SAMPLE_TEXTURE2D_LOD(_VegPushMap, sampler_VegPushMap, uv, 0);
    float need = length(m.rg);
    if (need < 0.001)
        return;
    float2 dir = m.rg / need;
    float r = max(m.a, 0.01);
    float dy = posWS.y - m.b;

    // Clear the body's surface at this height: it's narrower above and below its middle.
    float push = need - (r - sqrt(max(0.0, r * r - dy * dy)));
    // Above the body the stalk stays leaning over (it was shoved lower down), easing back higher up.
    if (dy > 0.0)
        push = max(push, need * saturate(1.0 - (dy - r) / max(_VegPushParams.y, 0.01)));
    push = max(push, 0.0);

    float above = posWS.y - rootWS.y;
    push *= saturate(above / max(_VegPushParams.z, 0.01)) * Strength;   // the base stays rooted
    push = min(push, _VegPushParams.x);

    // Lean rather than slide: a stalk pushed over also comes down a little.
    float dip = min(push * 0.5, push * push / (2.0 * max(above, 0.5)));
    float3 offsetWS = float3(dir.x * push, -dip, dir.y * push);
    Out = PositionOS + TransformWorldToObjectDir(offsetWS, false);
#endif
}

#endif // VEGETATION_PUSH_INCLUDED
