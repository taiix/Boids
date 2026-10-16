using System.Collections.Generic;
using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// A crab going about its business on the seabed, the way crabs do: mostly sat feeding (picking at the sand,
    /// eyes on stalks looking about), every so often grooming its eyes or waving a claw, then a short sideways
    /// dash somewhere nearby - it steps round side-on first, then scuttles - and still again. A fish coming close
    /// gets the claws: it turns to face it and holds them up open until it backs off. The shark makes it flinch
    /// (eyes folded, claws in), then either run sideways or dig itself into the sand and sit there with only its
    /// eyes out until the shark is well gone.
    ///
    /// On slopes it keeps its legs under it: the body sits on the ground its legs reach (probed all round it)
    /// and tilts with it, eased so bumps don't jolt it, and the walk and turn play at the speed it's moving so
    /// its feet don't skate. It turns back from slopes too steep to climb.
    ///
    /// Purely for looks and on each machine - nothing is networked (SeabedCrabs places them).
    /// </summary>
    public class CrabWalker : MonoBehaviour
    {
        enum State { Idle, Turn, Walk, Threat, Flee, Startle, Dig, Hidden, Emerge }

        [Header("Model (set by FishGame > Build Crab Assets)")]
        [SerializeField] Animator animator;
        [Tooltip("At full size (scale 1): how fast the walk cycle's feet push back on the ground - walk at this " +
                 "and they don't skate (export_crab.py measures it).")]
        [SerializeField] float strideSpeed = 0.443f;
        [Tooltip("Played forward, the walk carries it toward its right (or else its left).")]
        [SerializeField] bool walkGoesRight = true;
        [Tooltip("How fast the Turn take steps it round, played at normal speed (degrees a second).")]
        [SerializeField] float turnStride = 53f;
        [Tooltip("Played forward, the Turn take steps it round clockwise seen from above (or else anticlockwise).")]
        [SerializeField] bool turnGoesClockwise = true;
        [Tooltip("At scale 1: how far out its legs reach, where the ground under it is felt.")]
        [SerializeField] float footprint = 0.2f;

        [Header("Getting about")]
        [Tooltip("Walking speed at scale 1 (m/s).")]
        [SerializeField] float walkSpeed = 0.3f;
        [Tooltip("Running from the shark, at scale 1 (m/s).")]
        [SerializeField] float scuttleSpeed = 0.95f;
        [Tooltip("How fast it steps round on the spot (degrees a second).")]
        [SerializeField] float turnRate = 70f;
        [Tooltip("Seconds sat still between walks (min - max).")]
        [SerializeField] Vector2 idleSeconds = new Vector2(4f, 12f);
        [Tooltip("How far each walk goes (metres at scale 1, min - max).")]
        [SerializeField] Vector2 walkDistance = new Vector2(0.6f, 2.5f);
        [Tooltip("It wanders no further than this from where it started (metres).")]
        [SerializeField] float homeRange = 10f;
        [Tooltip("Slopes steeper than this it won't walk onto (degrees).")]
        [SerializeField] float maxSlope = 38f;

        [Header("Sat still")]
        [Tooltip("Seconds between little things it does while sat (grooming its eyes, waving a claw), min - max.")]
        [SerializeField] Vector2 fidgetEvery = new Vector2(4f, 10f);
        [Tooltip("Of those, how often it's a claw wave rather than grooming.")]
        [Range(0f, 1f)][SerializeField] float waveChance = 0.3f;

        [Header("Company")]
        [Tooltip("A fish this close (metres) gets the claws.")]
        [SerializeField] float threatRange = 2.2f;
        [Tooltip("The shark this close (metres) and it flinches, then runs or digs in.")]
        [SerializeField] float fleeRange = 7f;
        [Tooltip("When the shark comes, how often it digs into the sand rather than running.")]
        [Range(0f, 1f)][SerializeField] float digChance = 0.5f;
        [Tooltip("Dug in, it comes back out once the shark has been this far off (metres) for a few seconds.")]
        [SerializeField] float safeRange = 13f;

        static readonly int WalkSpeedId = Animator.StringToHash("WalkSpeed");
        static readonly int TurnSpeedId = Animator.StringToHash("TurnSpeed");

        State _state;
        float _stateUntil, _scale = 1f, _yaw, _height, _heightVelocity, _side = 1f, _nextLook, _nextFidget, _fidgetUntil;
        float _safeSince = -1f, _nextKick;
        Vector3 _up = Vector3.up, _target, _home;
        Transform _facing;
        string _playing;
        bool _placed;
        readonly Dictionary<string, float> _lengths = new Dictionary<string, float>();

        public void Init(float scale)
        {
            _scale = scale;
            transform.localScale = Vector3.one * scale;
        }

        void Start()
        {
            _home = transform.position;
            _yaw = transform.eulerAngles.y;
            if (animator != null && animator.runtimeAnimatorController != null)
                foreach (var clip in animator.runtimeAnimatorController.animationClips)
                    _lengths[clip.name] = clip.length;
            if (animator != null) animator.Play("Idle", 0, Random.value);
            BeginIdle(Random.Range(0f, idleSeconds.y));
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (Time.time >= _nextLook) { _nextLook = Time.time + 0.25f; LookAround(); }

            switch (_state)
            {
                case State.Idle:
                    Fidget();
                    if (Time.time > _stateUntil && Time.time > _fidgetUntil) PickWalk();
                    break;
                case State.Turn:
                    if (TurnToward(DesiredYaw(_target - transform.position, _side), dt) && Time.time > _stateUntil) BeginWalk();
                    break;
                case State.Walk:
                case State.Flee:
                    Walk(dt);
                    break;
                case State.Threat:
                    if (_facing != null) TurnToward(Yaw(_facing.position - transform.position), dt);
                    if (Time.time > _stateUntil) BeginIdle(Random.Range(1f, 3f));
                    break;
                case State.Startle:
                    if (Time.time > _stateUntil)
                    {
                        if (Random.value < digChance) Begin(State.Dig, "Dig");
                        else Flee(_facing != null ? _facing.position : transform.position - transform.forward);
                    }
                    break;
                case State.Dig:
                    KickSand();
                    if (Time.time > _stateUntil) { _state = State.Hidden; Play("Hidden"); _safeSince = -1f; }
                    break;
                case State.Emerge:
                    KickSand();
                    if (Time.time > _stateUntil) BeginIdle(Random.Range(1f, 3f));
                    break;
            }
            StandOnGround(dt);
        }

        // ------------------------------------------------------------------ what it's doing

        void BeginIdle(float seconds)
        {
            _state = State.Idle;
            _stateUntil = Time.time + seconds;
            _nextFidget = Time.time + Random.Range(fidgetEvery.x, fidgetEvery.y);
            Play("Idle");
        }

        // Sat still: now and then it grooms its eyes or waves a claw, then goes back to picking at the sand.
        void Fidget()
        {
            if (_fidgetUntil > 0f && Time.time > _fidgetUntil)
            {
                _fidgetUntil = 0f;
                Play("Idle");
            }
            if (_fidgetUntil > 0f || Time.time < _nextFidget) return;
            string take = Random.value < waveChance ? "Wave" : "Groom";
            Play(take);
            _fidgetUntil = Time.time + Length(take) - 0.25f;
            _nextFidget = _fidgetUntil + Random.Range(fidgetEvery.x, fidgetEvery.y);
        }

        // Somewhere nearby (back toward home if it's strayed), not up a cliff; step round side-on to it first.
        void PickWalk()
        {
            Vector3 pos = transform.position;
            for (int i = 0; i < 8; i++)
            {
                Vector2 dir = Random.insideUnitCircle.normalized;
                Vector3 to = new Vector3(dir.x, 0f, dir.y) * Random.Range(walkDistance.x, walkDistance.y) * _scale;
                Vector3 off = pos + to - _home;
                if (new Vector2(off.x, off.z).magnitude > homeRange) to = (_home - pos).normalized * Mathf.Min(walkDistance.y * _scale, (_home - pos).magnitude);
                if (!WaterFx.SeabedAt(pos + to, out Vector3 ground, out Vector3 normal) || Vector3.Angle(normal, Vector3.up) > maxSlope) continue;
                _target = ground;
                // Whichever side is nearer to facing it already leads.
                float right = DesiredYaw(to, 1f), left = DesiredYaw(to, -1f);
                _side = Mathf.Abs(Mathf.DeltaAngle(_yaw, right)) <= Mathf.Abs(Mathf.DeltaAngle(_yaw, left)) ? 1f : -1f;
                _state = State.Turn;
                _stateUntil = Time.time + 0.15f;
                Play("Turn");
                return;
            }
            BeginIdle(Random.Range(idleSeconds.x, idleSeconds.y));
        }

        void BeginWalk()
        {
            _state = State.Walk;
            Play("Walk");
        }

        void Walk(float dt)
        {
            Vector3 pos = transform.position;
            Vector3 to = _target - pos;
            to.y = 0f;
            float speed = (_state == State.Flee ? scuttleSpeed : walkSpeed) * _scale;
            if (to.magnitude < 0.15f * _scale)
            {
                BeginIdle(Random.Range(idleSeconds.x, idleSeconds.y));
                return;
            }

            // Keep side-on to where it's going (small corrections as it goes).
            TurnToward(DesiredYaw(to, _side), dt * (_state == State.Flee ? 2f : 0.6f));
            Vector3 lead = Quaternion.Euler(0f, _yaw, 0f) * Vector3.right * _side;

            // Too steep ahead: stop here.
            if (WaterFx.SeabedAt(pos + lead * (0.5f * _scale), out _, out Vector3 normal) && Vector3.Angle(normal, Vector3.up) > maxSlope)
            {
                BeginIdle(Random.Range(0.5f, 2f));
                return;
            }

            Vector3 step = Vector3.ProjectOnPlane(lead, _up).normalized * (speed * dt);
            transform.position = pos + new Vector3(step.x, 0f, step.z);
            // Feet that keep pace with the ground (backward for the other side).
            float pace = speed / Mathf.Max(0.05f, strideSpeed * _scale);
            if (animator != null) animator.SetFloat(WalkSpeedId, pace * (_side > 0f == walkGoesRight ? 1f : -1f));
        }

        // Fish about: the shark sends it running or digging in; another fish close by gets the claws.
        void LookAround()
        {
            Vector3 pos = transform.position;
            FishPlayer shark = null, nearestFish = null;
            float sharkDist = float.MaxValue, fishDist = threatRange * Mathf.Sqrt(_scale);
            foreach (var fp in FishPlayer.All)
            {
                if (fp == null || !fp.isActiveAndEnabled || fp.IsOut) continue;
                float d = Vector3.Distance(fp.transform.position, pos);
                if (fp.IsSharkBody) { if (d < sharkDist) { sharkDist = d; shark = fp; } }
                else if (d < fishDist) { fishDist = d; nearestFish = fp; }
            }

            switch (_state)
            {
                case State.Hidden:
                    // Out again once the shark's been well off for a few seconds.
                    if (sharkDist < safeRange) { _safeSince = -1f; return; }
                    if (_safeSince < 0f) _safeSince = Time.time;
                    if (Time.time - _safeSince > 3f) Begin(State.Emerge, "Emerge");
                    return;
                case State.Flee:
                case State.Startle:
                case State.Dig:
                case State.Emerge:
                    return;
            }

            if (shark != null && sharkDist < fleeRange)
            {
                // Flinch first: eyes folded, claws in - then run or dig in.
                _facing = shark.transform;
                Begin(State.Startle, "Startle", 0.45f);
                return;
            }
            if (nearestFish != null)
            {
                if (_state != State.Threat) Play("Threat");
                _state = State.Threat;
                _facing = nearestFish.transform;
                _fidgetUntil = 0f;
                _stateUntil = Time.time + 1.5f;   // held while it stays close (this runs again in a moment)
            }
        }

        // Away from the shark, sideways, at a scuttle - no stepping round first.
        void Flee(Vector3 from)
        {
            Vector3 away = transform.position - from;
            away.y = 0f;
            away = away.sqrMagnitude > 1e-4f ? away.normalized : Random.insideUnitSphere;
            away.y = 0f;
            Vector3 right = Quaternion.Euler(0f, _yaw, 0f) * Vector3.right;
            _side = Vector3.Dot(right, away) >= 0f ? 1f : -1f;
            Vector3 to = (right * _side + away).normalized * Random.Range(4f, 7f) * _scale;
            _target = WaterFx.SeabedAt(transform.position + to, out Vector3 ground, out _) ? ground : transform.position + to;
            _state = State.Flee;
            Play("Walk");
        }

        // A one-off take: in this state until it's played (or for `seconds`, if given).
        void Begin(State state, string take, float seconds = -1f)
        {
            _state = state;
            _fidgetUntil = 0f;
            _stateUntil = Time.time + (seconds > 0f ? seconds : Length(take) - 0.2f);
            Play(take);
        }

        // Digging in and out throws sand about.
        void KickSand()
        {
            if (Time.time < _nextKick) return;
            _nextKick = Time.time + Random.Range(0.25f, 0.45f);
            SandTrails.Kick(transform.position, 0.35f * _scale);
        }

        // ------------------------------------------------------------------ on the ground

        // Sit on the ground the legs reach and tilt with it, eased so bumps don't jolt it.
        void StandOnGround(float dt)
        {
            Vector3 pos = transform.position;
            Vector3 fwd = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
            Vector3 right = Quaternion.Euler(0f, _yaw, 0f) * Vector3.right;
            float reach = footprint * _scale;
            bool okF = WaterFx.SeabedAt(pos + fwd * reach, out Vector3 f, out _);
            bool okB = WaterFx.SeabedAt(pos - fwd * reach, out Vector3 b, out _);
            bool okR = WaterFx.SeabedAt(pos + right * reach, out Vector3 r, out _);
            bool okL = WaterFx.SeabedAt(pos - right * reach, out Vector3 l, out _);
            if (!(okF && okB && okR && okL)) return;

            Vector3 up = Vector3.Cross(f - b, r - l).normalized;
            if (up.y < 0.5f) up = Vector3.up;
            float ground = (f.y + b.y + r.y + l.y) * 0.25f;
            if (!_placed)
            {
                _placed = true;
                _up = up;
                _height = ground;
            }
            _up = Vector3.Slerp(_up, up, 1f - Mathf.Exp(-6f * dt)).normalized;
            _height = Mathf.SmoothDamp(_height, ground, ref _heightVelocity, 0.12f);
            transform.SetPositionAndRotation(new Vector3(pos.x, _height, pos.z),
                                             Quaternion.LookRotation(Vector3.ProjectOnPlane(fwd, _up), _up));
        }

        // ------------------------------------------------------------------ helpers

        // Turn on the spot toward a heading; true once there. Stepping round (the Turn take) when that's what
        // it's doing, its feet keeping pace.
        bool TurnToward(float yaw, float dt)
        {
            float delta = Mathf.DeltaAngle(_yaw, yaw);
            float turn = Mathf.Clamp(delta, -turnRate * dt, turnRate * dt);
            _yaw += turn;
            if (_state == State.Turn && animator != null)
            {
                float pace = Mathf.Max(0.3f, Mathf.Abs(turn) / dt / Mathf.Max(1f, turnStride));
                bool clockwise = delta > 0f;   // + yaw is clockwise from above
                animator.SetFloat(TurnSpeedId, pace * (clockwise == turnGoesClockwise ? 1f : -1f));
            }
            return Mathf.Abs(delta) < 8f;
        }

        // The heading that puts its right side (side +1) or left side (-1) toward a direction.
        static float DesiredYaw(Vector3 toward, float side) => Yaw(toward) - 90f * side;

        static float Yaw(Vector3 v) => Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;

        float Length(string take) => _lengths.TryGetValue(take, out float s) ? s : 1f;

        void Play(string state)
        {
            if (animator == null || _playing == state) return;
            _playing = state;
            if (state == "Walk") animator.SetFloat(WalkSpeedId, 1f);
            animator.CrossFadeInFixedTime(state, 0.25f);
        }
    }
}
