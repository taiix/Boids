using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// A player fish's tail beat (species model, FishSwim.hlsl). School fish beat on the clock; a player's
    /// stroke follows what the body is doing: a slow, small flutter while it hangs in the water, a steady beat
    /// when cruising, and hard, fast strokes when it sprints or turns sharply.
    ///
    /// The phase is advanced here and handed to the shader (rather than a rate it multiplies by time), so a
    /// change of pace never makes the tail jump. Speed is measured from the body's movement, so it looks the
    /// same on every machine - another player's copy has its motor switched off.
    /// </summary>
    [DisallowMultipleComponent]
    public class SpeciesSwimDriver : MonoBehaviour
    {
        [Tooltip("Below this speed (m/s) the fish counts as idling.")]
        [SerializeField] float idleSpeed = 0.35f;
        [Tooltip("Tail beats while idling, as a fraction of the species' cruising beat.")]
        [SerializeField] float idleRate = 0.3f;
        [Tooltip("Swing while idling, as a fraction of the baked swing.")]
        [SerializeField] float idleSwing = 0.3f;
        [Tooltip("Extra beat rate and swing at full sprint (fractions of cruising).")]
        [SerializeField] float sprintRate = 0.7f, sprintSwing = 0.35f;
        [Tooltip("Turning hard adds this much swing at 180 degrees per second.")]
        [SerializeField] float turnSwing = 0.3f;
        [Tooltip("How quickly the stroke follows a change of pace (1/seconds).")]
        [SerializeField] float response = 5f;

        MeshRenderer _renderer;
        Transform _body;
        FishMotor _motor;
        float _baseRate = 2f, _phase, _rate, _swing = 1f, _speed, _yawRate;
        Vector3 _lastPos, _lastFwd;
        bool _started;

        public void Init(MeshRenderer renderer, Transform body, Mesh mesh)
        {
            _renderer = renderer;
            _body = body;
            _motor = body != null ? body.GetComponent<FishMotor>() : null;
            // The species' cruising beat, baked into the mesh (TEXCOORD3.x).
            var uv3 = new System.Collections.Generic.List<Vector2>();
            if (mesh != null && mesh.isReadable) mesh.GetUVs(3, uv3);
            if (uv3.Count > 0 && uv3[0].x > 0f) _baseRate = uv3[0].x;
            _phase = Random.value;
            _rate = _baseRate * idleRate;
            _swing = idleSwing;
        }

        void LateUpdate()
        {
            if (_renderer == null || _body == null) return;
            float dt = Time.deltaTime;
            Vector3 pos = _body.position, fwd = _body.forward;
            if (!_started || dt <= 0f)
            {
                _started = true;
                _lastPos = pos;
                _lastFwd = fwd;
                Push();
                return;
            }

            // How fast it's really going, and turning (the motor's own numbers only exist on the owner's copy).
            float speed = (pos - _lastPos).magnitude / dt;
            float yaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(_lastFwd, Vector3.up), Vector3.ProjectOnPlane(fwd, Vector3.up), Vector3.up) / dt;
            _lastPos = pos;
            _lastFwd = fwd;
            if (speed > 60f) speed = _speed;   // a teleport (respawn, mouth release), not a swim
            float k = 1f - Mathf.Exp(-response * dt);
            _speed = Mathf.Lerp(_speed, speed, k);
            _yawRate = Mathf.Lerp(_yawRate, yaw, k);

            float cruise = _motor != null ? Mathf.Max(0.5f, _motor.MaxSpeed) : 6f;
            float s01 = Mathf.Clamp01(_speed / cruise);                       // 0 still .. 1 cruising flat out
            float boost = Mathf.Clamp01((_speed - cruise) / (cruise * 0.6f));  // beyond cruise: a sprint or a dash
            float idle = 1f - Mathf.Clamp01(_speed / Mathf.Max(0.01f, idleSpeed * 2f));

            float rate = _baseRate * (Mathf.Lerp(Mathf.Lerp(0.45f, 1f, s01), idleRate, idle) + sprintRate * boost);
            float swing = Mathf.Lerp(Mathf.Lerp(0.55f, 1f, s01), idleSwing, idle) + sprintSwing * boost
                        + turnSwing * Mathf.Clamp01(Mathf.Abs(_yawRate) / 180f);
            _rate = Mathf.Lerp(_rate, rate, k);
            _swing = Mathf.Lerp(_swing, Mathf.Min(swing, 2f), k);

            _phase = Mathf.Repeat(_phase + _rate * dt, 1f);
            Push();
        }

        void Push()
        {
            uint phase = (uint)(_phase * 65536f) & 0xFFFFu;
            uint swing = (uint)Mathf.Clamp(Mathf.RoundToInt(_swing * 127f), 0, 255);
            _renderer.SetShaderUserValue(0x10000u | phase | (swing << 17));
        }
    }
}
