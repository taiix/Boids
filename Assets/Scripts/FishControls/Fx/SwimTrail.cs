using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// What the shark's dash and lunge leave behind: a fizz of fine bubbles torn off its tail
    /// (<see cref="Dash"/>), sized to the body, and on a lunge streaks of water torn off its fins (<see cref="Streaks"/>).
    /// A fish sprinting leaves a lighter wake of bubbles, and its own player hears the whoosh as it kicks off.
    /// Also measures bodies for the other effects (<see cref="BodySize"/>).
    ///
    /// Purely visual and on every machine: the dash is announced by the shark's owner (FishPlayer relays it) so
    /// everyone sees the same burst. FishPlayer adds it to each body.
    /// </summary>
    public class SwimTrail : MonoBehaviour
    {
        [Tooltip("Bubbles per second torn off the tail during a dash, for a fish-sized body (bigger makes more).")]
        [SerializeField] float dashBubblesPerSecond = 170f;
        [Tooltip("Bubble size during a dash (metres, fish-sized body; scaled up a little for the shark).")]
        [SerializeField] Vector2 dashBubbleSize = new Vector2(0.02f, 0.06f);
        [Tooltip("Seconds bubbles keep coming after the dash itself ends.")]
        [SerializeField] float dashBubbleTail = 0.15f;
        [Tooltip("Bubbles per second off a fish swimming at full sprint (none at cruise speed).")]
        [SerializeField] float sprintBubblesPerSecond = 45f;
        [Tooltip("Water streaks per second off each fin during a lunge.")]
        [SerializeField] float streaksPerSecond = 70f;

        float _length = 1f, _width = 0.4f;   // body size, measured from its meshes
        float _bubbleDebt, _dashUntil = -1f;
        float _wakeDebt, _streakDebt, _streakUntil = -1f;
        bool _sprinting, _isShark;
        FishMotor _motor;
        Renderer _body;

        void Start()
        {
            Measure();
            _motor = GetComponent<FishMotor>();
            _isShark = TryGetComponent(out SharkAbilities _);
        }

        // The body's size in its own space (nose along +Z), from its meshes.
        void Measure()
        {
            Vector3 size = BodySize(transform, out _body);
            _length = Mathf.Max(0.3f, size.z);
            _width = Mathf.Max(0.15f, Mathf.Min(size.x, size.y));
        }

        /// <summary>A body's meshes measured in its own space (x across, y up, z nose to tail) - unlike the
        /// world bounds, the same whichever way it faces. Also hands back its main renderer.</summary>
        public static Vector3 BodySize(Transform body, out Renderer main)
        {
            main = null;
            bool any = false;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            foreach (var r in body.GetComponentsInChildren<Renderer>())
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                if (main == null) main = r;
                Transform space = r is SkinnedMeshRenderer smr && smr.rootBone != null ? smr.rootBone : r.transform;
                Bounds lb = r.localBounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = lb.center + Vector3.Scale(lb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var local = body.InverseTransformPoint(space.TransformPoint(corner));
                    min = Vector3.Min(min, local);
                    max = Vector3.Max(max, local);
                    any = true;
                }
            }
            return any ? Vector3.Scale(max - min, body.lossyScale) : Vector3.one;
        }

        /// <summary>A dash or lunge just started: tear bubbles off the tail for <paramref name="seconds"/>.</summary>
        public void Dash(float seconds) => _dashUntil = Mathf.Max(_dashUntil, Time.time + seconds + dashBubbleTail);

        /// <summary>A lunge just started: tear streaks of water off the fins for <paramref name="seconds"/>.</summary>
        public void Streaks(float seconds) => _streakUntil = Mathf.Max(_streakUntil, Time.time + seconds);

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            Vector3 pos = transform.position;

            // Nothing in the air, nor from a body that's gone (escaped, eaten, hidden).
            bool active = pos.y < WaterFx.SurfaceY - 0.2f && (_body == null || _body.enabled);
            if (active) SprintWake(pos, dt);
            if (active && Time.time <= _streakUntil) FinStreaks(pos, dt); else _streakDebt = 0f;
            if (!active || Time.time > _dashUntil) { _bubbleDebt = 0f; return; }

            // A fine fizz along the body and off the tail, left hanging in the water behind it.
            float sizeScale = Mathf.Clamp(_length / 1.2f, 1f, 1.8f);
            _bubbleDebt += dashBubblesPerSecond * sizeScale * dt;
            while (_bubbleDebt >= 1f)
            {
                _bubbleDebt -= 1f;
                Vector3 along = pos - transform.forward * (_length * Random.Range(0.2f, 0.55f));
                Vector3 jitter = Random.insideUnitSphere * (_width * 0.22f);
                WaterFx.Bubble(along + jitter, Random.insideUnitSphere * 0.35f,
                               Random.Range(dashBubbleSize.x, dashBubbleSize.y) * sizeScale, Random.Range(1.2f, 2.2f));
            }
        }

        // A fish going faster than it can cruise: a light wake of bubbles off the tail (every machine, from how the
        // body actually moves), and - for its own player only - a whoosh as it kicks into the sprint.
        void SprintWake(Vector3 pos, float dt)
        {
            if (_isShark || _motor == null) return;
            float cruise = _motor.MaxSpeed, speed = _motor.Speed;
            float k = Mathf.InverseLerp(cruise * 1.15f, Mathf.Max(cruise * 1.2f, _motor.BoostSpeed), speed);

            bool sprinting = k > 0.05f;
            if (sprinting && !_sprinting && GameFeel.IsViewTarget(transform) && GameAudio.Lib != null)
                GameAudio.Play(GameAudio.Pick(GameAudio.Lib.swishes), 0.45f, 0.08f);
            // A little hysteresis, so hovering at the threshold doesn't whoosh over and over.
            _sprinting = sprinting || (_sprinting && speed > cruise * 1.05f);

            if (k <= 0f || Time.time <= _dashUntil) { _wakeDebt = 0f; return; }
            _wakeDebt += sprintBubblesPerSecond * k * dt;
            while (_wakeDebt >= 1f)
            {
                _wakeDebt -= 1f;
                Vector3 at = pos - transform.forward * (_length * Random.Range(0.35f, 0.6f)) + Random.insideUnitSphere * (_width * 0.2f);
                WaterFx.Bubble(at, -transform.forward * 0.4f + Random.insideUnitSphere * 0.2f,
                               Random.Range(dashBubbleSize.x, dashBubbleSize.y) * 0.8f, Random.Range(0.9f, 1.6f));
            }
        }

        // Streaks peeling off both fins as the shark tears forward: water flung back and out, stretched along its flight.
        void FinStreaks(Vector3 pos, float dt)
        {
            _streakDebt += streaksPerSecond * dt;
            Vector3 fwd = transform.forward, right = transform.right, up = transform.up;
            while (_streakDebt >= 1f)
            {
                _streakDebt -= 1f;
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 fin = pos + right * (side * _width * 0.55f) - up * (_width * 0.15f)
                                + fwd * (_length * Random.Range(-0.05f, 0.15f));
                    Vector3 vel = -fwd * Random.Range(4f, 8f) + right * (side * Random.Range(0.6f, 1.6f)) + Random.insideUnitSphere * 0.4f;
                    GameFeel.Streak(fin, vel, Random.Range(0.05f, 0.1f), Random.Range(0.25f, 0.45f));
                }
            }
        }
    }
}
