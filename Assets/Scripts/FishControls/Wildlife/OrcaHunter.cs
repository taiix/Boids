using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// A killer whale hunting the shark (the orca world event, <see cref="WorldEvents"/>). They hunt as a pair,
    /// the way orcas do: one comes up behind the shark while the other swims ahead of it to cut it off. Each
    /// attack is a committed charge - faster than the shark can swim, but turning badly - that ends in a lunging
    /// bite or a bite-and-shake if it gets its nose to the shark; a shark that dodges aside as it comes past gets
    /// the tail instead (a slap to that side), one that boosts away makes it miss, and the orca peels off to
    /// come round again. A hit hurts the shark, shoves it, slows it right down and
    /// shakes loose any fish in its jaws (<see cref="FishPlayer.ServerOrcaHit"/>). Once the shark is out cold
    /// they circle it until it comes round, and give it a moment before they start again. Fish they ignore.
    ///
    /// The server (or this machine, offline) swims them; everyone else follows the NetworkTransform. Every
    /// machine animates its own copy: the swim cycle at the pace they're going, the body bending into turns,
    /// and the attacks (announced by <see cref="_attackSeq"/>).
    /// </summary>
    [RequireComponent(typeof(NetworkIdentity))]
    public class OrcaHunter : NetworkBehaviour
    {
        public enum Attack : byte { Bite, RollBite, TailSlapLeft, TailSlapRight }

        public static readonly List<OrcaHunter> All = new List<OrcaHunter>();

        [Header("Model (set by FishGame > Build Orca Assets)")]
        [SerializeField] Animator animator;
        [Tooltip("Bent into its turns: from the middle of the body back to the tail.")]
        [SerializeField] Transform[] tail;
        [Tooltip("Bent into its turns: from the middle of the body forward to the head.")]
        [SerializeField] Transform[] front;
        [Tooltip("Nose to tail (metres) at the size it's drawn.")]
        [SerializeField] float bodyLength = 6f;

        [Header("Speeds (m/s; the shark cruises at 6, boosts at 11)")]
        [SerializeField] float cruiseSpeed = 8.5f;
        [Tooltip("Catching up when it's well behind.")]
        [SerializeField] float chaseSpeed = 10.5f;
        [Tooltip("An attack run.")]
        [SerializeField] float chargeSpeed = 14f;
        [SerializeField] float circleSpeed = 6f;
        [SerializeField] float leaveSpeed = 9f;

        [Header("Turning (degrees a second)")]
        [SerializeField] float turnRate = 75f;
        [Tooltip("Mid-charge it's committed: it can barely turn - dodging aside makes it miss.")]
        [SerializeField] float chargeTurnRate = 34f;

        [Header("Hunting")]
        [Tooltip("It starts an attack run from this close (metres).")]
        [SerializeField] float attackRange = 22f;
        [Tooltip("How far its strike reaches from its nose to the shark's body (metres).")]
        [SerializeField] float strikeReach = 3.2f;
        [Tooltip("Longest an attack run lasts before it gives up (seconds).")]
        [SerializeField] float chargeSeconds = 3f;
        [Tooltip("Seconds between one orca's attacks (min - max).")]
        [SerializeField] Vector2 cooldown = new Vector2(6f, 9f);
        [Tooltip("Seconds between hits by either orca: they take turns.")]
        [SerializeField] float podSpacing = 2.5f;
        [Tooltip("Seconds it peels off for after an attack (min - max).")]
        [SerializeField] Vector2 recoverSeconds = new Vector2(2.5f, 4f);
        [Tooltip("How far out it circles a stunned shark (metres).")]
        [SerializeField] float circleRadius = 12f;
        [Tooltip("Seconds it gives the shark after it comes round, before attacking again.")]
        [SerializeField] float graceAfterStun = 4f;

        [Header("Hits")]
        [SerializeField] float damage = 20f;
        [SerializeField] float bleedSeconds = 8f;
        [Tooltip("How hard a hit shoves the shark (m/s).")]
        [SerializeField] float knockback = 9f;
        [SerializeField] float hitSlowSeconds = 1.5f;
        [Tooltip("A tail slap over the coral a shark is hiding down in: the shockwave's share of a full hit.")]
        [Range(0f, 1f)][SerializeField] float coverSlapDamage = 0.4f;
        [Tooltip("The shark's speed for those seconds after a hit (1 = not slowed).")]
        [Range(0.1f, 1f)][SerializeField] float hitSlowFactor = 0.45f;

        // The animator states of each attack, where in it to start (seconds), and when (seconds after) the blow
        // lands. The bite starts at its big lunge (its first, small snap skipped), the bite-and-shake lands on its
        // first head swing, the slaps as the tail whips through.
        static readonly string[] AttackStates = { "BiteAttack", "AlternativeAttack", "TailSlapLeft", "TailSlapRight" };
        static readonly float[] StartAt = { 0.45f, 0f, 0f, 0f };
        static readonly float[] ImpactAt = { 0.32f, 0.22f, 0.47f, 0.47f };
        static readonly int SwimSpeedId = Animator.StringToHash("SwimSpeed");

        // ---- shared ----
        // Which attack before the count that announces it: clients read them in this order, and the count's hook
        // looks at the kind.
        [SyncVar] byte _attackKind;
        [SyncVar(hook = nameof(OnAttackChanged))] byte _attackSeq;

        // ---- server (or offline) ----
        enum Mode { Hunt, Charge, Strike, Recover, Circle, Leave }
        Mode _mode;
        int _role;                          // 0 comes up behind, 1 cuts it off ahead
        float _modeUntil, _readyAt, _impactTime, _speed, _yaw, _pitch, _roll, _circleAngle, _leaveBy;
        bool _struck, _runUp, _coverSlap;
        Attack _strikeKind;
        Vector3 _exit, _peelTo;
        FishPlayer _shark;
        static float s_lastHit = -99f;      // any orca's last hit, so they take turns

        // ---- every machine ----
        Vector3 _lastPos;
        Quaternion _lastRot;
        float _pace, _bend;

        bool Authority => isServer || !NetworkClient.active;
        public bool Leaving => _mode == Mode.Leave;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        /// <summary>Server / offline, before it's spawned: which part it plays, and where it goes when it leaves.</summary>
        public void ServerBegin(int role, Vector3 exit)
        {
            _role = role;
            _exit = exit;
            _mode = Mode.Hunt;
            _speed = cruiseSpeed;
            _readyAt = Time.time + 2f + role * 2f;   // not both at once
            Vector3 e = transform.eulerAngles;
            _yaw = e.y;
            _pitch = Mathf.DeltaAngle(0f, e.x);
        }

        /// <summary>Server / offline: the hunt's over - swim off (out past <paramref name="exit"/>) and go.</summary>
        public void ServerLeave(Vector3 exit)
        {
            if (_mode == Mode.Leave) return;
            _mode = Mode.Leave;
            _exit = exit;
            _leaveBy = Time.time + 20f;
        }

        void Start()
        {
            _lastPos = transform.position;
            _lastRot = transform.rotation;
            if (animator != null) animator.Play("Swim", 0, Random.value);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (Authority) Hunt(dt);
            Animate(dt);
        }

        void LateUpdate() => BendBody();

        // ================================================================= hunting (server / offline)

        static FishPlayer FindShark()
        {
            foreach (var fp in FishPlayer.All)
                if (fp != null && fp.isActiveAndEnabled && fp.IsSharkBody && !fp.IsOut) return fp;
            return null;
        }

        void Hunt(float dt)
        {
            if (_mode != Mode.Leave) _shark = FindShark();
            FishVitals vitals = null;
            if (_shark != null) _shark.TryGetComponent(out vitals);
            bool stunned = vitals != null && vitals.IsStunned;

            // Out cold: circle it till it comes round, then give it a moment.
            if (_mode != Mode.Leave && _shark != null)
            {
                if (stunned && _mode != Mode.Circle)
                {
                    _mode = Mode.Circle;
                    _circleAngle = Mathf.Atan2(transform.position.z - _shark.transform.position.z, transform.position.x - _shark.transform.position.x);
                }
                else if (!stunned && _mode == Mode.Circle)
                {
                    _mode = Mode.Hunt;
                    _readyAt = Time.time + graceAfterStun + _role * 1.5f;
                }
            }

            Vector3 pos = transform.position;
            Vector3 goal = pos + transform.forward * 10f;
            float speed = cruiseSpeed, turn = turnRate;
            Vector3 sharkPos = _shark != null ? _shark.transform.position : pos;
            Vector3 sharkFwd = _shark != null ? _shark.transform.forward : transform.forward;
            Vector3 sharkVel = SharkVelocity();

            switch (_mode)
            {
                case Mode.Hunt:
                {
                    if (_shark == null) break;
                    float dist = Vector3.Distance(pos, sharkPos);
                    // Not while it's getting its strength back after a stun: that's its chance to get away.
                    bool ready = Time.time >= _readyAt && Time.time - s_lastHit > podSpacing && !(vitals != null && vitals.IsRecovering);
                    goal = _role == 0
                        ? sharkPos - sharkFwd * 9f                                                  // behind it
                        : sharkPos + sharkVel * 2.2f + Vector3.Cross(Vector3.up, sharkFwd) * 10f; // ahead, to one side
                    // Its turn: line up on the shark before it commits (a charge barely turns) - and if it's too
                    // close to come round onto it, swim out for a run-up first.
                    if (ready && dist < attackRange * 1.6f)
                    {
                        bool lined = Vector3.Angle(transform.forward, sharkPos - pos) < 30f;
                        _runUp = !lined && (dist < 12f || (_runUp && dist < 18f));
                        goal = _runUp ? pos + (pos - sharkPos).normalized * 15f + transform.forward * 5f : sharkPos + sharkVel * 0.8f;
                    }
                    else _runUp = false;
                    speed = dist > 30f ? chaseSpeed : cruiseSpeed;

                    // Hiding down in the coral, where it can't get its jaws to it: it comes over the top and
                    // tail-slaps the reef - orcas stun fish that way - and the shockwave knocks the shark about.
                    if (ready && dist < attackRange * 1.6f && Covered(sharkPos, out float overY))
                    {
                        _runUp = false;
                        goal = new Vector3(sharkPos.x, overY, sharkPos.z);
                        Vector3 flat = goal - pos;
                        flat.y = 0f;
                        if (flat.magnitude < 4f && pos.y - sharkPos.y < 12f)
                        {
                            _coverSlap = true;
                            StartStrike(Random.value < 0.5f ? Attack.TailSlapLeft : Attack.TailSlapRight);
                        }
                        break;
                    }
                    if (ready && dist < attackRange && Vector3.Angle(transform.forward, sharkPos - pos) < 30f)
                    {
                        _mode = Mode.Charge;
                        _modeUntil = Time.time + chargeSeconds;
                    }
                    break;
                }

                case Mode.Charge:
                {
                    if (_shark == null) { Recover(); break; }
                    goal = sharkPos + sharkVel * 0.5f;
                    speed = chargeSpeed;
                    turn = chargeTurnRate;
                    // Dodged aside as it comes past: it gets the tail. Otherwise close enough to bite (allowing for
                    // the moment the blow takes to land), or it got away.
                    Vector3 local = transform.InverseTransformPoint(sharkPos);
                    if (local.z < bodyLength * 0.15f && local.z > -bodyLength * 0.75f && Mathf.Abs(local.x) < 5f && Mathf.Abs(local.y) < 3.5f)
                        StartStrike(local.x > 0f ? Attack.TailSlapRight : Attack.TailSlapLeft);
                    else if (DistanceToShark(Nose(), sharkPos, sharkFwd) < strikeReach + _speed * 0.25f)
                        StartStrike(Random.value < 0.6f ? Attack.Bite : Attack.RollBite);
                    else if (Time.time > _modeUntil || Vector3.Angle(transform.forward, sharkPos - pos) > 80f)
                        Recover();
                    break;
                }

                case Mode.Strike:
                {
                    bool slap = IsSlap(_strikeKind);
                    // A bite follows the shark in; a slap carries straight on while the tail comes round.
                    goal = slap ? pos + transform.forward * 10f : sharkPos + sharkVel * 0.3f;
                    speed = slap ? cruiseSpeed * 0.8f : Mathf.Lerp(chargeSpeed, cruiseSpeed, 0.5f);
                    turn = chargeTurnRate;
                    if (!_struck && Time.time >= _impactTime)
                    {
                        _struck = true;
                        Vector3 from = slap ? TailSweep(_strikeKind) : Nose();
                        Vector3 across = from - sharkPos;
                        across.y = 0f;
                        bool reaches = _coverSlap
                            ? across.magnitude < 6f && from.y - sharkPos.y < 12f   // the shockwave, down through the coral
                            : DistanceToShark(from, sharkPos, sharkFwd) < (slap ? 4f : strikeReach * 1.5f);
                        // (Not hard on the heels of the other's hit: two at once would be too much.)
                        if (_shark != null && !stunned && Time.time - s_lastHit > 1.2f && reaches)
                            Hit(sharkPos, from, slap, _coverSlap ? coverSlapDamage : 1f);
                    }
                    if (Time.time > _modeUntil) Recover();
                    break;
                }

                case Mode.Recover:
                    goal = _peelTo;
                    speed = cruiseSpeed;
                    if (Time.time > _modeUntil)
                    {
                        _mode = Mode.Hunt;
                        _readyAt = Mathf.Max(_readyAt, Time.time + Random.Range(cooldown.x, cooldown.y) * 0.5f);
                    }
                    break;

                case Mode.Circle:
                    _circleAngle += circleSpeed / circleRadius * dt * (_role == 0 ? 1f : -1f);
                    goal = sharkPos + new Vector3(Mathf.Cos(_circleAngle), 0.15f, Mathf.Sin(_circleAngle)) * circleRadius;
                    speed = circleSpeed;
                    break;

                case Mode.Leave:
                    goal = _exit;
                    speed = leaveSpeed;
                    if (Time.time > _leaveBy || (pos - _exit).sqrMagnitude < 25f) { Despawn(); return; }
                    break;
            }
            Steer(goal, speed, turn, dt);
        }

        void StartStrike(Attack kind)
        {
            _strikeKind = kind;
            _attackKind = (byte)kind;
            _attackSeq++;
            if (!NetworkClient.active) PlayAttack(kind);   // offline: no hook
            _mode = Mode.Strike;
            _struck = false;
            _impactTime = Time.time + ImpactAt[(int)kind];
            _modeUntil = Time.time + ImpactAt[(int)kind] + (IsSlap(kind) ? 0.8f : 0.5f);
        }

        static bool IsSlap(Attack kind) => kind == Attack.TailSlapLeft || kind == Attack.TailSlapRight;

        // Where the tail sweeps through on a slap: back along the body, out to the side it slaps.
        Vector3 TailSweep(Attack kind) =>
            transform.position - transform.forward * (bodyLength * 0.35f)
            + transform.right * (kind == Attack.TailSlapRight ? 2f : -2f);

        // Is the shark down in the coral, deeper than it can reach? (Where it can swim over it, if so.)
        bool Covered(Vector3 sharkPos, out float overY) =>
            SceneryIndex.FloorAt(sharkPos, 1.6f, 2.5f, 1.9f, out overY) && overY - sharkPos.y > strikeReach + 0.5f;

        void Recover()
        {
            _coverSlap = false;
            _mode = Mode.Recover;
            _modeUntil = Time.time + Random.Range(recoverSeconds.x, recoverSeconds.y);
            _readyAt = Time.time + Random.Range(cooldown.x, cooldown.y);
            // Peel away off to its side, then come round again.
            Vector3 side = Vector3.Cross(Vector3.up, transform.forward) * (Random.value < 0.5f ? 1f : -1f);
            _peelTo = transform.position + (transform.forward + side).normalized * 25f;
        }

        void Hit(Vector3 sharkPos, Vector3 from, bool slap, float share = 1f)
        {
            s_lastHit = Time.time;
            // A bite shoves it on the way the orca's going; a slap knocks it away to the side, harder.
            Vector3 push = sharkPos - from;
            push.y *= 0.3f;
            push = slap ? push.normalized * knockback * 1.25f : (push.normalized + transform.forward).normalized * knockback;
            _shark.ServerOrcaHit(damage * share, slap ? bleedSeconds * 0.5f : bleedSeconds, push, hitSlowSeconds, hitSlowFactor);
            Vector3 at = Vector3.Lerp(from, sharkPos, 0.5f);
            if (NetworkServer.active) RpcStruck(at, _shark.netIdentity, slap);
            else Struck(at, _shark, slap);
        }

        void Despawn()
        {
            if (NetworkServer.active && netId != 0) NetworkServer.Destroy(gameObject);
            else Destroy(gameObject);
        }

        Vector3 Nose() => transform.position + transform.forward * (bodyLength * 0.48f);

        // From a point to the shark's body (a line along it, the shark being long).
        static float DistanceToShark(Vector3 p, Vector3 sharkPos, Vector3 sharkFwd)
        {
            Vector3 a = sharkPos - sharkFwd * 1.8f, b = sharkPos + sharkFwd * 1.8f;
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector3.Distance(p, a + ab * t);
        }

        Vector3 _sharkLast;
        float _sharkLastAt = -1f;
        Vector3 _sharkVel;
        Vector3 SharkVelocity()
        {
            if (_shark == null) return Vector3.zero;
            Vector3 p = _shark.transform.position;
            if (_sharkLastAt > 0f && Time.time > _sharkLastAt)
                _sharkVel = Vector3.Lerp(_sharkVel, (p - _sharkLast) / (Time.time - _sharkLastAt), 0.2f);
            _sharkLast = p;
            _sharkLastAt = Time.time;
            return _sharkVel;
        }

        // Turn toward the goal no faster than it can, bank into the turn, keep off the seabed and under the
        // surface (it's a whale: it doesn't burst out of the water here), and swim on.
        void Steer(Vector3 goal, float speed, float turn, float dt)
        {
            Vector3 pos = transform.position;
            Vector3 want = goal - pos;
            if (want.sqrMagnitude < 0.01f) want = transform.forward;
            want.Normalize();

            // Over the seabed and over the reef - coral, sponges, rocks (SceneryIndex) - never through them:
            // look along the way it's going (further the faster it goes) and climb in good time.
            const float seabedClear = 2.5f, reefClear = 1.9f, halfWidth = 1.6f;
            Vector3 heading = Quaternion.Euler(_pitch, _yaw, 0f) * Vector3.forward;
            float look = 4f + _speed * 1.1f, climb = 0f;
            for (int i = 1; i <= 4; i++)
            {
                float d = look * i / 4f;
                Vector3 ahead = pos + heading * d;
                if (SceneryIndex.FloorAt(ahead, halfWidth, seabedClear, reefClear, out float floorAhead) && ahead.y < floorAhead)
                    climb = Mathf.Max(climb, Mathf.Clamp01((floorAhead - ahead.y) / d + 0.3f));
            }
            if (climb > 0f) want.y = Mathf.Max(want.y, Mathf.Lerp(0.35f, 0.9f, climb));
            bool floored = SceneryIndex.FloorAt(pos, halfWidth, seabedClear, reefClear, out float floor);
            if (floored && pos.y < floor) want.y = Mathf.Max(want.y, 0.8f);
            else if (floored && pos.y < floor + 1.5f) want.y = Mathf.Max(want.y, 0f);   // skimming it: don't dive in
            if (pos.y > WaterFx.SurfaceY - 3f) want.y = Mathf.Min(want.y, -0.4f);
            want.Normalize();

            float wantYaw = Mathf.Atan2(want.x, want.z) * Mathf.Rad2Deg;
            float wantPitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(want.y, -1f, 1f)) * Mathf.Rad2Deg, -40f, 40f);
            float yawStep = Mathf.Clamp(Mathf.DeltaAngle(_yaw, wantYaw), -turn * dt, turn * dt);
            _yaw += yawStep;
            // Even mid-charge it pulls up over something in its way.
            _pitch = Mathf.MoveTowards(_pitch, wantPitch, Mathf.Max(turn, climb > 0f ? 75f : 0f) * 0.7f * dt);
            _roll = Mathf.Lerp(_roll, Mathf.Clamp(-yawStep / dt * 0.45f, -35f, 35f), 1f - Mathf.Exp(-3f * dt));
            _speed = Mathf.MoveTowards(_speed, speed, (speed > _speed ? 9f : 6f) * dt);
            Vector3 next = pos + Quaternion.Euler(_pitch, _yaw, 0f) * Vector3.forward * (_speed * dt);
            // And if it's still caught low (came round a corner onto a coral head), lift it clear.
            if (floored && next.y < floor - 0.8f) next.y = Mathf.MoveTowards(next.y, floor - 0.8f, 5f * dt);
            transform.SetPositionAndRotation(next, Quaternion.Euler(_pitch, _yaw, _roll));
        }

        // ================================================================= every machine: looks

        void OnAttackChanged(byte _, byte __) => PlayAttack((Attack)_attackKind);

        void PlayAttack(Attack kind)
        {
            if (animator != null) animator.CrossFadeInFixedTime(AttackStates[(int)kind], 0.12f, 0, StartAt[(int)kind]);
            var lib = GameAudio.Lib;
            if (lib != null && lib.swishes != null && lib.swishes.Length > 0)
                GameAudio.PlayAt(lib.swishes[Random.Range(0, lib.swishes.Length)], transform.position, 1f, 60f, 0.1f);
            WaterFx.BubbleBurst(IsSlap(kind) ? TailSweep(kind) : Nose(), 25, 0.8f, 1.5f, new Vector2(0.02f, 0.06f), transform.forward * 2f);
        }

        [ClientRpc]
        void RpcStruck(Vector3 at, NetworkIdentity shark, bool slap) => Struck(at, shark != null ? shark.GetComponent<FishPlayer>() : null, slap);

        // The blow landing: blood in the water (a bite), the water buckling, a crunch, and the shark flinching.
        void Struck(Vector3 at, FishPlayer shark, bool slap)
        {
            if (!slap) BloodInWater.Spill(at, null, Random.Range(1, 9999), linger: true);
            WaterFx.Shockwave(at, slap ? 9f : 7f, 0.5f);
            WaterFx.BubbleBurst(at, slap ? 70 : 45, 1.2f, 2.5f, new Vector2(0.02f, 0.07f));
            var lib = GameAudio.Lib;
            if (lib != null && lib.bites != null && lib.bites.Length > 0)
                GameAudio.PlayAt(lib.bites[Random.Range(0, lib.bites.Length)], at, 1f, 70f, 0.1f);
            if (shark != null && shark.TryGetComponent(out SharkCondition condition)) condition.PlayHit();
        }

        // The swim cycle at the pace it's going.
        void Animate(float dt)
        {
            Vector3 pos = transform.position;
            float pace = (pos - _lastPos).magnitude / dt;
            _pace = Mathf.Lerp(_pace, pace, 1f - Mathf.Exp(-4f * dt));
            _lastPos = pos;
            if (animator != null) animator.SetFloat(SwimSpeedId, Mathf.Clamp(_pace / 7f, 0.5f, 2.2f));
        }

        // Its body curving into a turn (tail swinging round), on top of whatever the animation is doing.
        void BendBody()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            Vector3 was = _lastRot * Vector3.forward, now = transform.forward;
            was.y = now.y = 0f;
            float yawRate = was.sqrMagnitude > 1e-4f && now.sqrMagnitude > 1e-4f ? Vector3.SignedAngle(was, now, Vector3.up) / dt : 0f;
            _lastRot = transform.rotation;
            _bend = Mathf.Lerp(_bend, Mathf.Clamp(yawRate * 0.12f, -14f, 14f), 1f - Mathf.Exp(-5f * dt));
            if (Mathf.Abs(_bend) < 0.05f) return;
            Vector3 up = transform.up;
            if (tail != null)
                for (int i = 0; i < tail.Length; i++)
                    if (tail[i] != null) tail[i].rotation = Quaternion.AngleAxis(-_bend * (0.6f + 0.4f * i / Mathf.Max(1, tail.Length - 1)), up) * tail[i].rotation;
            if (front != null)
                foreach (var b in front)
                    if (b != null) b.rotation = Quaternion.AngleAxis(_bend * 0.5f, up) * b.rotation;
        }
    }
}
