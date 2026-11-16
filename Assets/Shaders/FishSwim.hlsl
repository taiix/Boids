#ifndef FISH_SWIM_HLSL
#define FISH_SWIM_HLSL

// The school fish's swim cycle, in the vertex stage. The authored clip (swim.anim) only swings one tail
// joint about a fixed axis, so it is exactly linear-blend skinning with one moving bone:
//
//     position = lerp(restPose, Rotate(tailPose, angle(t)), tailWeight)
//
// The baked mesh (Tools > Fish > Bake Swim Mesh) carries the rest pose in POSITION, the vertex as the tail
// bone holds it at the clip's middle angle in TEXCOORD2.xy + TEXCOORD3.x, and its tail weight in
// TEXCOORD3.y (HDRP's mesh attributes are float2 per texcoord). The pivot,
// axis and swing come from the rig in FishSwimRig.hlsl, written by the same bake.
//
// A second, procedural mode serves every other species (Tools > Fish > Build Species, FishSpeciesBuilder.cs):
// their rigs move many bones, so instead of replaying a clip the tail swings about a hinge. The mesh is laid
// out nose along +Z, back along +Y, one unit long and centred, and each vertex holds
//     TEXCOORD2 = (s, hinge)    s: 0 at the nose .. 1 at the tail tip; the hinge's s (rigid in front of it)
//     TEXCOORD3 = (rate, 2 + swing)   tail beats per second, the tail's swing in radians
// The "2 +" tells it apart from the rig mode, whose TEXCOORD3.y is a weight in 0..1. A player fish drives its
// own stroke from its speed instead of the clock (bit 16 of the user value, see SpeciesSwimDriver.cs).
//
// Per fish, MeshRenderer.SetShaderUserValue carries the phase (bits 0-7) and a tail-beat rate scale
// (bits 8-15) so a school doesn't beat in lockstep; it keeps the SRP Batcher on, unlike a property block.

#include "Assets/Shaders/FishSwimRig.hlsl"

float3 FishSwimRotate(float3 p, float3 axis, float angle)
{
    float s, c;
    sincos(angle, s, c);
    return p * c + cross(axis, p) * s + axis * dot(axis, p) * (1.0 - c);
}

AttributesMesh ApplyMeshModification(AttributesMesh input, float3 timeParameters)
{
    float w = input.uv3.y;
    if (w > 1.5)
    {
        // Procedural species: the front of the body stays rigid and everything behind the hinge swings round
        // it as a tail - a rotation, not a sideways shear, so the body's markings never slide or wobble
        // across it (a shear wave down the whole body did, and stripes looked like they were jiggling).
        uint user   = unity_RendererUserValue;
        float s     = input.uv2.x;
        float hinge = input.uv2.y;
        float theta = w - 2.0;
        float cycles;
        if (user & 0x10000u)
        {
            // Driven by a script (a player fish, SpeciesSwimDriver): bits 0-15 are the stroke's phase, which it
            // advances at a rate set by how fast the fish swims, and bits 17-24 scale the swing (127 = as baked).
            cycles = (user & 0xFFFFu) / 65536.0;
            theta *= ((user >> 17) & 0xFFu) / 127.0;
        }
        else
        {
            float phase = (user & 0xFFu) / 255.0;
            float rate  = input.uv3.x * lerp(0.85, 1.15, ((user >> 8) & 0xFFu) / 255.0);
            cycles = timeParameters.x * rate + phase;
        }
        float d = s - hinge;
        if (d > 0.0)
        {
            // Eased in just behind the hinge, and the tail fin trails the stroke a little.
            float angle = theta * smoothstep(0.0, 0.2, d) * sin(6.28318530718 * (cycles - 0.35 * d));
            float pivotZ = 0.5 - hinge;
            float sn, cs;
            sincos(angle, sn, cs);
            float x = input.positionOS.x, z = input.positionOS.z - pivotZ;
            input.positionOS.x = x * cs + z * sn;
            input.positionOS.z = z * cs - x * sn + pivotZ;
        }
    }
    else if (w > 0.0)
    {
        uint user  = unity_RendererUserValue;
        float phase = (user & 0xFFu) / 255.0;
        float rate  = FISH_SWIM_RATE * lerp(0.85, 1.15, ((user >> 8) & 0xFFu) / 255.0);
        float angle = FISH_SWIM_AMPLITUDE * cos(6.28318530718 * (timeParameters.x * rate + phase));
        float3 tailPose = float3(input.uv2, input.uv3.x);
        float3 tail = FishSwimRotate(tailPose - FISH_SWIM_PIVOT, FISH_SWIM_AXIS, angle) + FISH_SWIM_PIVOT;
        input.positionOS = lerp(input.positionOS, tail, w);
    }
    return input;
}

#endif
