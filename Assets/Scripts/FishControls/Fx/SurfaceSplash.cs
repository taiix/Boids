using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// A splash when a body leaps out of the water and another when it crashes back in, sized to the body and
    /// to how fast it hit. On every machine, from each body's own movement - nothing networked. FishPlayer adds
    /// it to each body.
    /// </summary>
    public class SurfaceSplash : MonoBehaviour
    {
        [Tooltip("How far past the surface the body's middle has to get before it counts as out / back in " +
                 "(stops waves and bobbing at the surface from splashing all the time).")]
        [SerializeField] float hysteresis = 0.35f;
        [Tooltip("Slowest crossing (m/s, up or down) that still splashes.")]
        [SerializeField] float minSpeed = 1.5f;
        [Tooltip("Seconds before the same body can splash again (bobbing along the surface makes one splash).")]
        [SerializeField] float cooldown = 0.8f;

        float _size = 1f, _nextSplash;
        bool _above, _known;
        Vector3 _lastPos;
        Renderer _body;

        void Start()
        {
            Vector3 size = SwimTrail.BodySize(transform, out _body);
            _size = Mathf.Clamp(Mathf.Max(size.x, size.y) * 1.2f, 0.6f, 4f);
            _lastPos = transform.position;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            Vector3 pos = transform.position;
            Vector3 velocity = dt > 0f ? (pos - _lastPos) / dt : Vector3.zero;
            _lastPos = pos;
            if (_body != null && !_body.enabled) return; // gone (escaped, eaten)

            // Only ask the water for its exact (wavy) height when the body is anywhere near it.
            float surface = WaterFx.SurfaceY;
            if (Mathf.Abs(pos.y - surface) < 3f) surface = WaterFx.WaterHeightAt(pos, out _, withDisplacement: false);
            if (!_known) { _above = pos.y > surface; _known = true; return; }

            bool cross = _above ? pos.y < surface - hysteresis : pos.y > surface + hysteresis;
            if (!cross) return;
            _above = !_above;
            if (velocity.sqrMagnitude > 60f * 60f) return; // teleported, not a leap
            if (Mathf.Abs(velocity.y) < minSpeed || Time.time < _nextSplash) return;
            _nextSplash = Time.time + cooldown;

            float strength = Mathf.Clamp(Mathf.Abs(velocity.y) / 6f, 0.6f, 1.6f);
            WaterFx.Splash(new Vector3(pos.x, surface, pos.z), velocity, _size * strength);
            var lib = GameAudio.Lib;
            if (lib != null) GameAudio.PlayAt(GameAudio.Pick(lib.splashes), new Vector3(pos.x, surface, pos.z), 0.45f * strength, 45f);
        }
    }
}
