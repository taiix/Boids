using Unity.Mathematics;
using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// A school fish swims with a vertex shader (FishSwim.hlsl): a plain mesh, no Animator, no skinning.
    /// That was ~2.5-3 ms of main thread per 1000 fish, for a clip that only swings the tail. The original
    /// rig (Animator + skinned mesh) stays on the prefab, switched off, and comes back only while the fish
    /// is in a shark's mouth so its struggle clip plays exactly as authored (<see cref="Edible"/> swaps it).
    ///
    /// Purely cosmetic and local: nothing here is networked, and every player may see a different phase.
    /// </summary>
    [DisallowMultipleComponent]
    public class FishSwimRig : MonoBehaviour
    {
        [SerializeField] MeshRenderer swim;
        [SerializeField] SkinnedMeshRenderer rig;
        [SerializeField] Animator animator;

        static uint s_next;

        public bool UsingRig { get; private set; }
        /// <summary>The renderer drawing the fish right now (for shadow LOD and the like).</summary>
        public Renderer Active => UsingRig ? (Renderer)rig : swim;
        public MeshRenderer Swim => swim;

        void Awake()
        {
            // Each fish beats at its own phase and a slightly different rate, so a school doesn't move in
            // lockstep. A counter hash rather than UnityEngine.Random, so no other system's sequence shifts.
            uint h = math.hash(new uint2(s_next++, 0x9E3779B9u));
            if (swim != null) swim.SetShaderUserValue(h & 0xFFFFu);
            UseRig(false);
        }

        /// <summary>True: the authored rig draws the fish (for its eaten clip). False: the shader swim.</summary>
        public void UseRig(bool on)
        {
            UsingRig = on;
            if (swim != null) swim.enabled = !on;
            if (rig != null) rig.enabled = on;
            if (animator != null) animator.enabled = on;
        }

        public void Setup(MeshRenderer swimRenderer, SkinnedMeshRenderer rigRenderer, Animator rigAnimator)
        {
            swim = swimRenderer;
            rig = rigRenderer;
            animator = rigAnimator;
        }
    }
}
