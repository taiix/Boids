using UnityEngine;
using UnityEngine.InputSystem;

namespace FishGame
{
    /// <summary>
    /// Switches control between the ship and whatever else the scene already has a camera for
    /// (in Island 1, the player fish).
    ///
    /// This exists because a scene may only usefully have one active Camera and one
    /// AudioListener. Dropping a ship camera into a scene that already has a player camera
    /// gives you two of each, and Unity renders whichever it feels like while warning about
    /// the duplicate listener. So rather than leaving that for someone to trip over, this
    /// owns the handover: exactly one rig is live at a time, and the other is switched off
    /// entirely, input included.
    /// </summary>
    public class ShipDemoRig : MonoBehaviour
    {
        [Header("Ship")]
        [Tooltip("Camera rig that follows the ship.")]
        [SerializeField] GameObject shipCamera;
        [Tooltip("Helm that reads the wheel and telegraph. Disabled while not aboard, so W/S don't drive the ship from ashore.")]
        [SerializeField] ShipHelmController shipHelm;

        [Header("Other rig (the player fish, in Island 1)")]
        [Tooltip("Camera rig to switch off while the ship has control.")]
        [SerializeField] GameObject otherCamera;
        [Tooltip("Input component to switch off with it. Optional.")]
        [SerializeField] MonoBehaviour otherController;

        [Header("Startup")]
        [Tooltip("Start at the ship's helm. Turn off if you want the scene to open on the fish.")]
        [SerializeField] bool startInShipMode = true;
        [SerializeField] Key toggleKey = Key.F1;

        bool shipMode;

        /// <summary>True while the player has the helm.</summary>
        public bool ShipMode => shipMode;

        void Start()
        {
            Apply(startInShipMode);
        }

        void Update()
        {
            if (Keyboard.current == null) return;

            if (Keyboard.current[toggleKey].wasPressedThisFrame)
                Apply(!shipMode);
        }

        void Apply(bool toShip)
        {
            shipMode = toShip;

            if (shipCamera != null) shipCamera.SetActive(toShip);
            if (otherCamera != null) otherCamera.SetActive(!toShip);

            // Disabling the helm rather than just the camera matters: an enabled helm keeps
            // reading W/S and would sail the ship away while you are swimming around as a fish.
            if (shipHelm != null) shipHelm.enabled = toShip;
            if (otherController != null) otherController.enabled = !toShip;
        }
    }
}
