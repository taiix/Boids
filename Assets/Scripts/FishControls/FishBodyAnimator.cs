using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// Procedural "it looks alive" layer. Reads the motor and bends the visual model:
    ///   * A traveling sine wave runs head -> tail (carangiform/thunniform undulation):
    ///     amplitude grows toward the tail and tail-beat frequency rises with speed.
    ///   * The body BANKS (rolls) into turns based on the motor's yaw rate.
    ///   * The body LEANS (pitches) slightly when rising/diving.
    ///
    /// This is purely cosmetic — it never touches physics, so it's safe to disable or
    /// replace with a real skeletal animation later. Two ways to use it:
    ///   A) Drop it on a fish with no rig: leave Spine Segments empty and it sways the
    ///      whole model + tail. Works on a stretched capsule so you can test instantly.
    ///   B) Assign Spine Segments head->tail (bones or nested transforms) for a real wave.
    /// </summary>
    public class FishBodyAnimator : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The visual root that gets rolled/pitched (usually the mesh child, NOT the physics object).")]
        [SerializeField] Transform modelRoot;
        [Tooltip("Optional: spine/tail transforms ordered HEAD -> TAIL. Leave empty to just sway the model root.")]
        [SerializeField] Transform[] spineSegments;
        [SerializeField] FishMotor motor;

        [Header("Undulation (tail wave)")]
        [Tooltip("Side-to-side sway in degrees at the TAIL when swimming hard.")]
        [SerializeField] float swayAmplitude = 14f;
        [Tooltip("Tail beats per second while idle...")]
        [SerializeField] float baseFrequency = 1.2f;
        [Tooltip("...plus this many extra beats/sec at full speed.")]
        [SerializeField] float frequencyPerSpeed = 2.5f;
        [Tooltip("How much the wave lags from one segment to the next (the 'travelling' look).")]
        [SerializeField] float waveLagPerSegment = 0.6f;
        [Tooltip("Keep at least this much idle wiggle so a resting fish still looks alive (0..1).")]
        [Range(0f, 1f)][SerializeField] float idleSwayFraction = 0.25f;

        [Header("Banking (roll into turns)")]
        [Tooltip("Degrees of roll per (deg/sec) of turning.")]
        [SerializeField] float bankPerYawRate = 0.25f;
        [SerializeField] float maxBank = 40f;
        [SerializeField] float bankSmoothing = 6f;

        [Header("Pitch lean (rise/dive)")]
        [Tooltip("Degrees of nose-up/down per (m/s) of vertical speed.")]
        [SerializeField] float pitchPerVerticalSpeed = 3f;
        [SerializeField] float maxPitchLean = 25f;
        [SerializeField] float pitchSmoothing = 6f;

        Quaternion _modelBaseLocalRot;
        Quaternion[] _segmentBaseLocalRot;
        float _phase;
        float _bank;
        float _pitch;

        void Awake()
        {
            if (motor == null) motor = GetComponentInParent<FishMotor>();
            if (modelRoot == null) modelRoot = transform;

            _modelBaseLocalRot = modelRoot.localRotation;

            if (spineSegments != null && spineSegments.Length > 0)
            {
                _segmentBaseLocalRot = new Quaternion[spineSegments.Length];
                for (int i = 0; i < spineSegments.Length; i++)
                    if (spineSegments[i] != null)
                        _segmentBaseLocalRot[i] = spineSegments[i].localRotation;
            }
        }

        void LateUpdate()
        {
            if (motor == null) return;
            float dt = Time.deltaTime;
            float speed01 = motor.SpeedFraction;

            // --- Tail wave -----------------------------------------------------------
            float frequency = baseFrequency + frequencyPerSpeed * speed01;
            _phase += frequency * Mathf.PI * 2f * dt;

            // Wiggle scales with effort but never drops fully to zero (a fish always idles).
            float waveStrength = Mathf.Lerp(idleSwayFraction, 1f, speed01);

            if (_segmentBaseLocalRot != null)
            {
                int n = spineSegments.Length;
                for (int i = 0; i < n; i++)
                {
                    if (spineSegments[i] == null) continue;
                    // Amplitude grows toward the tail: front stays stiff, rear swings most.
                    float tailWeight = (n > 1) ? (float)i / (n - 1) : 1f;
                    float amp = swayAmplitude * waveStrength * tailWeight;
                    float angle = Mathf.Sin(_phase - i * waveLagPerSegment) * amp;
                    spineSegments[i].localRotation = _segmentBaseLocalRot[i] * Quaternion.Euler(0f, angle, 0f);
                }
            }

            // --- Banking + pitch lean on the whole model -----------------------------
            float targetBank = Mathf.Clamp(-motor.YawRate * bankPerYawRate, -maxBank, maxBank);
            _bank = Mathf.Lerp(_bank, targetBank, 1f - Mathf.Exp(-bankSmoothing * dt));

            float verticalSpeed = motor.Velocity.y;
            float targetPitch = Mathf.Clamp(-verticalSpeed * pitchPerVerticalSpeed, -maxPitchLean, maxPitchLean);
            _pitch = Mathf.Lerp(_pitch, targetPitch, 1f - Mathf.Exp(-pitchSmoothing * dt));

            // If there are no spine segments, fold a gentle body sway into the model root
            // so a rig-less test fish still swims convincingly.
            float bodySway = (_segmentBaseLocalRot == null)
                ? Mathf.Sin(_phase) * swayAmplitude * 0.5f * waveStrength
                : 0f;

            modelRoot.localRotation = _modelBaseLocalRot * Quaternion.Euler(_pitch, bodySway, _bank);
        }
    }
}
