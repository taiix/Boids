using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// The life in a body's model, on top of its swimming - purely visual, on every machine, never the collider:
    ///   * <b>Squash and stretch</b>: the model stretches nose-to-tail as it surges forward and squashes as it pulls
    ///     up hard, on a springy wobble, keeping its volume. Read off how the body actually moves, so every machine
    ///     sees it on everyone; <see cref="Punch"/> adds a kick (the shark's jaws clamping, a lunge's coil).
    ///   * <b>Flips</b> (<see cref="Flip"/>): a quick roll - a fish's happy flip when it eats or a task is done near it.
    ///   * <b>Idle personality</b>: a body that rests a while does something now and then - a fish flicks, looks
    ///     around or barrel-rolls; the shark stretches lazily.
    ///   * <b>Bumps</b>: hitting the seabed or a rock at speed kicks up a puff (only where the body is simulated,
    ///     i.e. the machine driving it).
    /// FishPlayer adds it to each body.
    /// </summary>
    [DefaultExecutionOrder(100)] // after FishBodyAnimator, which sets the model's rotation each frame
    public class BodyJuice : MonoBehaviour
    {
        [Tooltip("Most the model stretches or squashes (fraction of its length).")]
        [SerializeField] float maxStretch = 0.12f;
        [Tooltip("Stretch per m/s^2 of forward acceleration.")]
        [SerializeField] float stretchPerAccel = 0.006f;
        [Tooltip("Spring stiffness and damping of the wobble back to normal.")]
        [SerializeField] float stiffness = 260f;
        [SerializeField] float damping = 20f;

        Transform _model;
        FishMotor _motor;
        FishPlayer _player;
        bool _shark;
        Vector3 _baseScale;
        int _lengthAxis, _upAxis;         // the model's own axes along the body and up through it
        Quaternion _base, _written;       // its rotation before our touch, and what we last wrote
        bool _wrote;

        float _s, _sv;                    // the stretch (+) / squash (-) spring and its velocity
        float _lastSpeed = -1f;
        float _pausedUntil;

        float _flipStart = -10f, _flipSeconds = 0.55f, _flipDir = 1f;
        float _restFor, _nextIdle;
        int _idleKind;
        float _idleStart = -10f;
        float _lastHunger = -1f;
        float _nextBump;

        static readonly Vector3[] Axes = { Vector3.right, Vector3.up, Vector3.forward };

        void Start()
        {
            _motor = GetComponent<FishMotor>();
            _player = GetComponent<FishPlayer>();
            _shark = TryGetComponent(out SharkAbilities _);
            if (_shark) maxStretch = Mathf.Min(maxStretch, 0.06f); // a five-metre shark wobbling like a fish looks rubbery

            // The model: the body's child holding its main mesh - and the mesh's bones with it. A skinned mesh is
            // drawn from its bones, so moving the renderer's own object (the fish's svgMesh2, whose bones are its
            // siblings) shows nothing; the child above both does.
            SwimTrail.BodySize(transform, out Renderer main);
            if (main != null)
            {
                _model = main.transform;
                while (_model.parent != null && _model.parent != transform) _model = _model.parent;
            }
            if (_model == null || _model == transform) { enabled = false; return; }

            _baseScale = _model.localScale;
            _lengthAxis = Dominant(_model.InverseTransformDirection(transform.forward));
            _upAxis = Dominant(_model.InverseTransformDirection(transform.up));
            if (_upAxis == _lengthAxis) _upAxis = (_lengthAxis + 1) % 3;
            _nextIdle = Time.time + Random.Range(3f, 6f);
        }

        void OnEnable() => MatchManager.TaskDone += OnTaskDone;
        void OnDisable() => MatchManager.TaskDone -= OnTaskDone;

        static int Dominant(Vector3 v)
        {
            Vector3 a = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
            return a.x >= a.y && a.x >= a.z ? 0 : a.y >= a.z ? 1 : 2;
        }

        /// <summary>Kick the spring: + stretches the body, - squashes it.</summary>
        public void Punch(float amount) => _sv += amount * 18f;

        /// <summary>Hold the model as it is for <paramref name="seconds"/> (hit stop).</summary>
        public void Pause(float seconds) => _pausedUntil = Mathf.Max(_pausedUntil, Time.unscaledTime + seconds);

        /// <summary>A quick full roll along the body.</summary>
        public void Flip(float seconds = 0.55f)
        {
            if (Time.time - _flipStart < _flipSeconds) return;
            _flipStart = Time.time;
            _flipSeconds = seconds;
            _flipDir = Random.value < 0.5f ? -1f : 1f;
        }

        // A task finished near a fish: it does a little flip.
        void OnTaskDone(Vector3 where, string what)
        {
            if (_shark || _player == null || _player.IsOut) return;
            if ((transform.position - where).sqrMagnitude < 12f * 12f) Flip();
        }

        void LateUpdate()
        {
            if (_model == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // Whoever owns the model's rotation (FishBodyAnimator) writes it fresh each frame; if nobody did, take
            // ours back off first so it doesn't pile up.
            Quaternion current = _model.localRotation;
            _base = _wrote && current == _written ? _base : current;

            if (Time.unscaledTime < _pausedUntil)
            {
                Write(_base * Extra(0f));
                return;
            }

            // ---- squash and stretch from how hard the body is surging or braking
            float speed = _motor != null ? _motor.Speed : 0f;
            float forwardSpeed = _motor != null ? Vector3.Dot(_motor.Velocity, transform.forward) : 0f;
            float accel = _lastSpeed >= 0f ? (forwardSpeed - _lastSpeed) / dt : 0f;
            _lastSpeed = forwardSpeed;
            // Braking squashes less than surging stretches: coasting down from a burst shouldn't flatten the body.
            float target = Mathf.Clamp(accel * stretchPerAccel * (accel < 0f ? 0.4f : 1f), -maxStretch, maxStretch);

            // Spring toward it: overshoots a little and wobbles back.
            float force = stiffness * (target - _s) - damping * _sv;
            _sv += force * dt;
            _s = Mathf.Clamp(_s + _sv * dt, -maxStretch * 1.3f, maxStretch * 1.3f);

            float along = 1f + _s, across = 1f / Mathf.Sqrt(Mathf.Max(0.2f, along));
            Vector3 k = new Vector3(across, across, across);
            k[_lengthAxis] = along;
            _model.localScale = Vector3.Scale(_baseScale, k);

            // ---- eating: hunger going up on the synced vitals, seen on every machine
            if (!_shark && _player != null && _player.TryGetComponent(out FishVitals vitals))
            {
                float h = vitals.Hunger;
                if (_lastHunger >= 0f && h - _lastHunger > 3f) Flip(0.45f);
                _lastHunger = h;
            }

            // ---- idle: resting a while, now and then do something
            bool resting = speed < 0.6f && _player != null && !_player.IsOut && !_player.IsBeingEaten;
            _restFor = resting ? _restFor + dt : 0f;
            if (_restFor > 2.5f && Time.time >= _nextIdle && Time.time - _idleStart > 2.5f)
            {
                _idleStart = Time.time;
                _idleKind = _shark ? 0 : Random.Range(0, 10) < 1 ? 2 : Random.Range(0, 2); // a barrel roll is rare
                if (_idleKind == 2) Flip(0.7f);
                _nextIdle = Time.time + Random.Range(4f, 9f);
            }
            if (!resting) _idleStart = -10f;

            Write(_base * Extra(dt));
        }

        // The rotation we add on top: a flip's roll, and the idle moves.
        Quaternion Extra(float dt)
        {
            Quaternion q = Quaternion.identity;
            Vector3 lengthAxis = Axes[_lengthAxis], upAxis = Axes[_upAxis];

            float f = (Time.time - _flipStart) / _flipSeconds;
            if (f >= 0f && f < 1f)
            {
                float e = f * f * (3f - 2f * f);
                q *= Quaternion.AngleAxis(360f * e * _flipDir, lengthAxis);
            }

            float t = Time.time - _idleStart;
            if (t >= 0f && t < 2.5f)
            {
                switch (_idleKind)
                {
                    case 0 when _shark:
                        // A lazy stretch: a slow swing of the body, then settle.
                        q *= Quaternion.AngleAxis(Mathf.Sin(t / 2.5f * Mathf.PI * 2f) * 6f * (1f - t / 2.5f), upAxis);
                        if (dt > 0f && t < dt * 1.5f) Punch(0.03f);
                        break;
                    case 0:
                        // A flick: a quick decaying wiggle.
                        if (t < 0.6f) q *= Quaternion.AngleAxis(Mathf.Sin(t * 30f) * 22f * (1f - t / 0.6f), upAxis);
                        break;
                    case 1:
                        // A look around: one way, the other, back.
                        float u = t / 2.5f;
                        float yaw = u < 0.3f ? Mathf.SmoothStep(0f, 28f, u / 0.3f)
                                  : u < 0.65f ? Mathf.SmoothStep(28f, -28f, (u - 0.3f) / 0.35f)
                                  : Mathf.SmoothStep(-28f, 0f, (u - 0.65f) / 0.35f);
                        q *= Quaternion.AngleAxis(yaw, upAxis);
                        break;
                }
            }
            return q;
        }

        void Write(Quaternion q)
        {
            _model.localRotation = q;
            _written = _model.localRotation;
            _wrote = true;
        }

        // Only where this body is simulated (its driver's machine) do collisions happen.
        void OnCollisionEnter(Collision c)
        {
            float hit = c.relativeVelocity.magnitude;
            if (hit < 3f || Time.time < _nextBump || c.contactCount == 0) return;
            _nextBump = Time.time + 0.4f;
            Vector3 at = c.GetContact(0).point;
            Punch(-Mathf.Clamp(hit * 0.012f, 0.03f, 0.1f));
            if (c.collider.GetComponentInParent<FishPlayer>() != null) return; // bumping another swimmer: just the squish

            if (((1 << c.collider.gameObject.layer) & (1 << 3)) != 0) // the seabed (Terrain layer): a cloud of sand
                SandTrails.Kick(at, Mathf.Clamp(0.4f + hit * 0.08f, 0.5f, 1.4f));
            else
            {
                WaterFx.BubbleBurst(at, 10, 0.2f, 1.2f, new Vector2(0.02f, 0.05f));
                WaterFx.Shockwave(at, Mathf.Clamp(hit * 0.4f, 1.5f, 4f), 0.35f);
            }
            if (GameFeel.IsViewTarget(transform)) GameFeel.Shake(Mathf.Clamp(hit * 0.03f, 0.1f, 0.3f));
        }
    }
}
