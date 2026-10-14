using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// The water reacting to the shark's violent eating: every snap of the jaws and every hard shake of the
    /// head throws out a cloud of bubbles and a ring of buckling water (a refraction shockwave), and near the
    /// seabed it kicks up sand. While it holds a struggling player the jaws stream bubbles constantly.
    ///
    /// Timed off the shark's own animation (Shark_DevourViolent / Shark_ThrashLoop), so it lands exactly on the
    /// beats every player sees, on every machine, with nothing networked. FishPlayer adds it to shark bodies.
    /// </summary>
    public class SharkEatFx : MonoBehaviour
    {
        [Tooltip("Animator state of the one-shot eat and its jaw snaps (seconds into the clip).")]
        [SerializeField] string devourState = "DevourViolent";
        [SerializeField] float[] devourSnaps = { 0.17f, 0.58f, 0.98f };
        [Tooltip("Animator state of the looping shake while holding a player, its chomps and its head-shake " +
                 "extremes (fractions of one loop).")]
        [SerializeField] string thrashState = "Thrash";
        [SerializeField] float[] thrashChomps = { 0.2f, 0.7f };
        [SerializeField] float[] thrashShakes = { 0.0625f, 0.3125f, 0.5625f, 0.8125f };
        [Tooltip("Bubbles per second streaming from the jaws while holding a fish.")]
        [SerializeField] float holdBubblesPerSecond = 45f;
        [Tooltip("Shockwave size at a jaw snap (metres across); head shakes are a bit smaller.")]
        [SerializeField] float snapShockwaveSize = 4.5f;

        Animator _animator;
        Transform _mouth;
        int _devourHash, _thrashHash;
        float _lastDevourTime = -1f, _lastThrashPhase = -1f, _bubbleDebt;

        void Start()
        {
            _animator = GetComponentInChildren<Animator>();
            if (TryGetComponent(out SharkAbilities jaws)) _mouth = jaws.MouthAnchor;
            if (_mouth == null) _mouth = transform;
            _devourHash = Animator.StringToHash(devourState);
            _thrashHash = Animator.StringToHash(thrashState);
        }

        void Update()
        {
            if (_animator == null || !_animator.isActiveAndEnabled) return;
            var state = _animator.GetCurrentAnimatorStateInfo(0);

            if (state.shortNameHash == _devourHash)
            {
                float t = state.normalizedTime * state.length;
                if (t < _lastDevourTime) _lastDevourTime = -1f; // played again
                foreach (float at in devourSnaps)
                    if (_lastDevourTime < at && t >= at) Snap(at == devourSnaps[0] ? 1.3f : 1f);
                _lastDevourTime = t;
            }
            else _lastDevourTime = -1f;

            if (state.shortNameHash == _thrashHash)
            {
                float u = Mathf.Repeat(state.normalizedTime, 1f);
                if (_lastThrashPhase >= 0f)
                {
                    foreach (float at in thrashChomps) if (Crossed(_lastThrashPhase, u, at)) Snap(0.9f);
                    foreach (float at in thrashShakes) if (Crossed(_lastThrashPhase, u, at)) Shake();
                }
                _lastThrashPhase = u;

                // Air and blood whipped out of the jaws the whole time.
                _bubbleDebt += holdBubblesPerSecond * Time.deltaTime;
                while (_bubbleDebt >= 1f)
                {
                    _bubbleDebt -= 1f;
                    WaterFx.Bubble(_mouth.position + Random.insideUnitSphere * 0.4f, Random.insideUnitSphere * 1.5f,
                                   Random.Range(0.015f, 0.05f), Random.Range(1.2f, 2.2f));
                }
            }
            else
            {
                _lastThrashPhase = -1f;
                _bubbleDebt = 0f;
            }
        }

        // Did the loop's phase pass `at` this frame (wrapping round the end of the loop)?
        static bool Crossed(float from, float to, float at) =>
            from <= to ? from < at && to >= at : from < at || to >= at;

        // The jaws slam: a big burst of bubbles, the water buckling outward, sand if it's on the bottom.
        void Snap(float strength)
        {
            Vector3 mouth = _mouth.position;
            WaterFx.BubbleBurst(mouth, Mathf.RoundToInt(55 * strength), 0.5f, 3.5f * strength, new Vector2(0.015f, 0.07f));
            WaterFx.Shockwave(mouth, snapShockwaveSize * strength, 0.4f);
            WaterFx.Shockwave(mouth + transform.forward * 0.5f, snapShockwaveSize * 0.6f * strength, 0.3f);
            KickSand(mouth, 1.6f * strength);
        }

        // A hard swing of the head: water shoved aside along the body.
        void Shake()
        {
            Vector3 head = _mouth.position - transform.forward * 0.8f;
            WaterFx.BubbleBurst(head, 20, 0.6f, 2.2f, new Vector2(0.015f, 0.05f), -transform.right * 0.5f);
            WaterFx.Shockwave(head, snapShockwaveSize * 0.75f, 0.35f);
            KickSand(head, 1.2f);
        }

        static void KickSand(Vector3 at, float radius)
        {
            if (WaterFx.SeabedHeight(at, out float ground) && at.y - ground < 2.5f)
                SandTrails.Kick(new Vector3(at.x, ground, at.z), radius);
        }
    }
}
