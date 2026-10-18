using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// An ammonite about the reef, for ambience (<see cref="AmmoniteSchools"/> places them; local to each
    /// machine, like the crabs). It keeps with its little school, jetting along shell first the way cephalopods
    /// do - tentacles trailing - now and then putting on a burst, sometimes just drifting; it keeps over the
    /// seabed and the coral (<see cref="SceneryIndex"/>), under the surface and inside the arena. A player fish
    /// coming close, or the shark from further off, sends it jetting away.
    ///
    /// Its animator blends the swim takes from where it's going: Speed (0 idle, 1 swimming, 2 fast), Turn
    /// (+1 turning right) and Climb (+1 climbing), set up by FishGame > Build Ammonite Assets.
    /// </summary>
    public class AmmoniteSwimmer : MonoBehaviour
    {
        [Header("Model (set by FishGame > Build Ammonite Assets)")]
        [SerializeField] Animator animator;
        [Tooltip("The pace (m/s, at scale 1) its swim take looks right at; faster or slower plays it faster or slower.")]
        [SerializeField] float swimPace = 0.35f;
        [Tooltip("And its fast swim.")]
        [SerializeField] float fastPace = 0.9f;

        [Header("Swimming (m/s)")]
        [SerializeField] Vector2 cruiseSpeed = new Vector2(0.7f, 1.2f);
        [SerializeField] float driftSpeed = 0.25f;
        [SerializeField] float burstSpeed = 3.2f;
        [Tooltip("Seconds a burst lasts (min, max).")]
        [SerializeField] Vector2 burstSeconds = new Vector2(1.2f, 2.2f);
        [Tooltip("Degrees a second it can turn.")]
        [SerializeField] float turnRate = 60f;
        [Tooltip("Keep this far over the seabed, and over the coral, in metres.")]
        [SerializeField] float clearance = 1.5f;

        [Header("Shy")]
        [Tooltip("A player fish this close sends it jetting off (metres).")]
        [SerializeField] float fishAlarm = 5f;
        [Tooltip("The shark, from this far (metres).")]
        [SerializeField] float sharkAlarm = 11f;

        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int TurnId = Animator.StringToHash("Turn");
        static readonly int ClimbId = Animator.StringToHash("Climb");

        /// <summary>Its school (set by AmmoniteSchools).</summary>
        public AmmoniteSchools.School School { get; set; }

        Vector3 _offset, _fleeDir;
        float _cruise, _speed, _yaw, _pitch, _turn, _climb, _burstUntil, _nextLook, _nextOffset, _scale = 1f;

        void Start()
        {
            _scale = transform.lossyScale.y;
            _cruise = Random.Range(cruiseSpeed.x, cruiseSpeed.y);
            _speed = _cruise;
            Vector3 e = transform.eulerAngles;
            _yaw = e.y;
            _pitch = Mathf.DeltaAngle(0f, e.x);
            NewOffset();
            if (animator != null)
            {
                animator.speed = Random.Range(0.9f, 1.1f);
                animator.Update(Random.Range(0f, 2f));   // not all in step
            }
        }

        void NewOffset()
        {
            _offset = Vector3.Scale(Random.insideUnitSphere, new Vector3(3.5f, 1.2f, 3.5f));
            _nextOffset = Time.time + Random.Range(6f, 14f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            Vector3 pos = transform.position;
            if (Time.time >= _nextLook) Look(pos);
            if (Time.time >= _nextOffset) NewOffset();

            // Where to: with its school (or, scared, away), at its own pace.
            bool bursting = Time.time < _burstUntil;
            Vector3 goal = School != null ? School.Anchor + _offset : pos + transform.forward * 5f;
            float want = School != null && School.Drifting ? driftSpeed : _cruise;
            if (bursting)
            {
                goal = pos + _fleeDir * 10f;
                want = burstSpeed;
            }
            else if (School != null && (goal - pos).sqrMagnitude > 64f) want = Mathf.Max(want, _cruise * 1.4f);   // fallen behind

            Vector3 to = goal - pos;
            // Not into the others.
            if (School != null)
                foreach (var other in School.Members)
                {
                    if (other == null || other == this) continue;
                    Vector3 away = pos - other.transform.position;
                    float d = away.magnitude, min = 0.45f * _scale + 0.4f;
                    if (d > 1e-3f && d < min) to += away / d * (min - d) * 6f;
                }
            if (to.sqrMagnitude < 0.01f) to = transform.forward;
            Vector3 dir = to.normalized;

            // Over the seabed and the reef, under the surface.
            Vector3 heading = transform.forward;
            float look = 2f + _speed * 1.5f;
            if (SceneryIndex.FloorAt(pos + heading * look, 0.5f * _scale, clearance + 0.5f, clearance, out float floorAhead)
                && pos.y + heading.y * look < floorAhead) dir.y = Mathf.Max(dir.y, 0.5f);
            if (SceneryIndex.FloorAt(pos, 0.5f * _scale, clearance, clearance * 0.7f, out float floor) && pos.y < floor) dir.y = Mathf.Max(dir.y, 0.8f);
            if (pos.y > WaterFx.SurfaceY - 3f) dir.y = Mathf.Min(dir.y, -0.3f);
            dir.Normalize();

            // Turn toward it as fast as it can; it keeps its shell upright (no banking).
            float wantYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float wantPitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg, -30f, 30f);
            float rate = turnRate * (bursting ? 1.6f : 1f);
            float yawStep = Mathf.Clamp(Mathf.DeltaAngle(_yaw, wantYaw), -rate * dt, rate * dt);
            float pitchStep = Mathf.Clamp(wantPitch - _pitch, -rate * 0.6f * dt, rate * 0.6f * dt);
            _yaw += yawStep;
            _pitch += pitchStep;
            _speed = Mathf.MoveTowards(_speed, want, (want > _speed ? 4f : 1.2f) * dt);
            transform.SetPositionAndRotation(pos + Quaternion.Euler(_pitch, _yaw, 0f) * Vector3.forward * (_speed * dt),
                                             Quaternion.Euler(_pitch, _yaw, 0f));

            Animate(yawStep / dt, -pitchStep / dt, dt);
        }

        // Fish about? Every quarter second.
        void Look(Vector3 pos)
        {
            _nextLook = Time.time + 0.25f;
            foreach (var fp in FishPlayer.All)
            {
                if (fp == null || !fp.isActiveAndEnabled || fp.IsOut) continue;
                Vector3 from = pos - fp.transform.position;
                float alarm = fp.IsSharkBody ? sharkAlarm : fishAlarm;
                if (from.sqrMagnitude > alarm * alarm) continue;
                Scare(from);
                if (School != null) School.Scare(fp.transform.position);
                return;
            }
        }

        /// <summary>Jet off, away along <paramref name="away"/>.</summary>
        public void Scare(Vector3 away)
        {
            if (Time.time < _burstUntil) return;
            away.y *= 0.4f;
            _fleeDir = (away.sqrMagnitude > 1e-4f ? away.normalized : -transform.forward) + Random.insideUnitSphere * 0.3f;
            _fleeDir.Normalize();
            _burstUntil = Time.time + Random.Range(burstSeconds.x, burstSeconds.y);
        }

        // The swim takes for how it's going: the pace picks idle / swim / fast and how quickly they play, the
        // turn and climb bend its tentacles round.
        void Animate(float yawRate, float climbRate, float dt)
        {
            if (animator == null) return;
            float swim = swimPace * _scale, fast = fastPace * _scale;
            float speed = _speed <= swim ? _speed / swim : 1f + Mathf.Clamp01((_speed - swim) / Mathf.Max(0.01f, fast - swim));
            float k = 1f - Mathf.Exp(-4f * dt);
            _turn = Mathf.Lerp(_turn, Mathf.Clamp(yawRate / turnRate, -1f, 1f), k);
            _climb = Mathf.Lerp(_climb, Mathf.Clamp(climbRate / (turnRate * 0.6f), -1f, 1f), k);
            animator.SetFloat(SpeedId, speed);
            animator.SetFloat(TurnId, _turn);
            animator.SetFloat(ClimbId, _climb);
            // Its jets keep time with the pace (within reason).
            float pace = speed < 0.5f ? 1f : _speed / Mathf.Lerp(swim, fast, Mathf.Clamp01(speed - 1f));
            animator.speed = Mathf.Lerp(animator.speed, Mathf.Clamp(pace, 0.7f, 1.6f), k);
        }
    }
}
