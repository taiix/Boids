using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace FishGame
{
    /// <summary>
    /// Gives the local player a role-specific look and view range.
    ///
    /// Prey get a cold, grainy, tunnel-visioned grade and a short view distance; the hunter gets a
    /// clear, slightly warm one and sees much further. Everything shared between the two lives in
    /// the scene volumes (IslandGradingProfile / UnderwaterGradingProfile) — this only adds the
    /// per-role layer on top.
    ///
    /// Put this on the player camera. Both player prefabs carry it; it does nothing until
    /// <see cref="ApplyRole"/> is called, which <see cref="FishPlayer"/> does for the local player.
    ///
    /// How the split works: the role volume is global but sits on its own layer, and the camera's
    /// volume layer mask only includes the layer for the role it's playing. Remote players and the
    /// Scene view never see it. Everything here is client-local rendering state — none of it is
    /// networked, so changing it cannot desync a match.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public class CreatureVision : MonoBehaviour
    {
        [Header("Role grades")]
        [Tooltip("Volume profile layered on top of the scene grade while playing as prey.")]
        [SerializeField] VolumeProfile fishProfile;
        [Tooltip("Volume profile layered on top of the scene grade while playing as the shark.")]
        [SerializeField] VolumeProfile sharkProfile;

        [Header("Volume layers")]
        [Tooltip("Layer the prey's role volume lives on. Must not be used by anything else.")]
        [SerializeField] string fishLayer = "PostFxFish";
        [Tooltip("Layer the shark's role volume lives on. Must not be used by anything else.")]
        [SerializeField] string sharkLayer = "PostFxShark";
        [Tooltip("Priority of the role volume. Must beat the scene volumes (base 1, underwater 2).")]
        [SerializeField] float rolePriority = 10f;

        [Header("Underwater view range")]
        // NOTE: these are the RAW WaterSurface.absorptionDistance values. The surface also has an
        // absorptionDistanceMultiplier (2.4 on the Island ocean), so the effective range is roughly
        // 2.4x these numbers: ~74m for the fish and ~132m for the shark against a ~89m default.
        // Tune by eye, not by reading these as metres.
        [Tooltip("Raw water absorption distance while playing as prey. Lower = murkier, sees less. " +
                 "This is the dominant underwater visibility lever. The water surface multiplies it " +
                 "by its own absorptionDistanceMultiplier, so this is not a distance in metres.")]
        [SerializeField] float fishAbsorptionDistance = 31f;
        [Tooltip("Raw water absorption distance while playing as the shark. Same multiplier caveat.")]
        [SerializeField] float sharkAbsorptionDistance = 55f;
        [Tooltip("Water surface to drive. Left empty, the first one in the scene is used.")]
        [SerializeField] WaterSurface water;

        [Header("Transition")]
        [Tooltip("Seconds to fade into the role look, so being assigned a role isn't a hard cut.")]
        [SerializeField] float blendSeconds = 1.5f;

        [Header("Camera")]
        [Tooltip("Force temporal anti-aliasing on the player camera. Underwater geometry and " +
                 "caustics alias badly without it.")]
        [SerializeField] bool forceTemporalAA = true;

        Camera _camera;
        HDAdditionalCameraData _cameraData;
        Volume _roleVolume;

        // Whatever the water was authored with, so we can restore it and so "no role yet" looks normal.
        float _defaultAbsorption;
        bool _capturedDefault;
        // Set once this instance has actually driven the water, so the copies of this component
        // riding on remote players never reset clarity out from under the local player.
        bool _drivesWater;

        float _startAbsorption;
        float _targetAbsorption;
        float _startWeight;
        float _blendTimer;
        bool _blending;

        void Awake() => EnsureRefs();

        /// <summary>
        /// Resolve the camera and water references. Called from Awake, and again from ApplyRole so
        /// that a caller which spawns and configures a player in one go still gets a working
        /// camera mask instead of silently skipping it.
        /// </summary>
        void EnsureRefs()
        {
            if (_camera == null) _camera = GetComponent<Camera>();
            if (_cameraData == null) _cameraData = GetComponent<HDAdditionalCameraData>();

            if (water == null)
                water = FindFirstObjectByType<WaterSurface>();

            if (water != null && !_capturedDefault)
            {
                _defaultAbsorption = water.absorptionDistance;
                _targetAbsorption = _defaultAbsorption;
                _capturedDefault = true;
            }

            if (forceTemporalAA && _cameraData != null)
                _cameraData.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
        }

        /// <summary>
        /// Switch this camera to the look and view range for <paramref name="role"/>.
        /// Safe to call repeatedly and safe to call when the role changes mid-round.
        /// </summary>
        public void ApplyRole(FishRole role)
        {
            EnsureRefs();

            var profile = role == FishRole.Shark ? sharkProfile : fishProfile;
            var layerName = role == FishRole.Shark ? sharkLayer : fishLayer;

            if (role == FishRole.Unassigned)
            {
                ClearRole();
                return;
            }

            int layer = LayerMask.NameToLayer(layerName);
            if (layer < 0)
            {
                Debug.LogWarning($"{nameof(CreatureVision)}: layer '{layerName}' does not exist. " +
                                 "Add it in Project Settings > Tags and Layers, or the role grade " +
                                 "will be skipped.", this);
            }
            else if (profile == null)
            {
                Debug.LogWarning($"{nameof(CreatureVision)}: no volume profile assigned for {role}.", this);
            }
            else
            {
                EnsureRoleVolume();
                _roleVolume.gameObject.layer = layer;
                _roleVolume.sharedProfile = profile;
                _roleVolume.priority = rolePriority;
                _roleVolume.gameObject.SetActive(true);

                // Only ever look at the shared volumes plus this role's own layer, so the other
                // role's grade can never leak in.
                if (_cameraData != null)
                    _cameraData.volumeLayerMask = 1 | (1 << layer);
            }

            SetAbsorptionTarget(role == FishRole.Shark ? sharkAbsorptionDistance : fishAbsorptionDistance);
        }

        /// <summary>Drop back to the plain scene grade and the authored view range.</summary>
        public void ClearRole()
        {
            if (_roleVolume != null)
                _roleVolume.gameObject.SetActive(false);

            if (_cameraData != null)
                _cameraData.volumeLayerMask = 1;

            if (_capturedDefault)
                SetAbsorptionTarget(_defaultAbsorption);
        }

        void EnsureRoleVolume()
        {
            if (_roleVolume != null) return;

            var go = new GameObject("Role Post FX (runtime)");
            go.transform.SetParent(transform, false);
            _roleVolume = go.AddComponent<Volume>();
            _roleVolume.isGlobal = true;
            _roleVolume.weight = 0f; // faded in by Update
        }

        void SetAbsorptionTarget(float metres)
        {
            _drivesWater = true;
            _startAbsorption = water != null ? water.absorptionDistance : metres;
            _targetAbsorption = metres;
            _startWeight = _roleVolume != null ? _roleVolume.weight : 0f;
            _blendTimer = 0f;
            _blending = true;
        }

        void Update()
        {
            if (!_blending) return;

            // Fade the role grade in and ease the water to its new clarity together, so a role
            // assignment reads as the world changing around you rather than a cut.
            _blendTimer += Time.deltaTime;
            float t = blendSeconds <= 0f ? 1f : Mathf.Clamp01(_blendTimer / blendSeconds);
            float eased = Mathf.SmoothStep(0f, 1f, t);

            if (_roleVolume != null)
            {
                float targetWeight = _roleVolume.gameObject.activeSelf ? 1f : 0f;
                _roleVolume.weight = Mathf.Lerp(_startWeight, targetWeight, eased);
            }

            if (water != null)
                water.absorptionDistance = Mathf.Lerp(_startAbsorption, _targetAbsorption, eased);

            if (t >= 1f) _blending = false;
        }

        void OnDestroy()
        {
            // The water surface is shared scene state; leaving it at this player's clarity would
            // bleed into whatever loads next.
            if (water != null && _capturedDefault && _drivesWater)
                water.absorptionDistance = _defaultAbsorption;
        }
    }
}
