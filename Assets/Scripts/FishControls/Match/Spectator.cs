using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FishGame
{
    /// <summary>
    /// Out of the round (escaped, eaten, died) while others still play: watch them. Added to the local
    /// player's camera when their body goes out. Left click moves on to the next player, right click back;
    /// the shark comes first, then the fish still in the water. When whoever you're watching leaves the
    /// round, it moves on by itself. The view takes on the watched creature's eyes (shark or fish vision).
    /// </summary>
    public class Spectator : MonoBehaviour
    {
        /// <summary>The local spectator, if we're spectating.</summary>
        public static Spectator Active { get; private set; }

        /// <summary>Who we're watching (null when nobody is left in the water).</summary>
        public FishPlayer Watching => _target;

        FishPlayer _self;
        FishOrbitCamera _orbit;
        CreatureVision _vision;
        FishPlayer _target;
        int _index;
        InputAction _next, _previous, _hint;
        readonly List<FishPlayer> _players = new List<FishPlayer>();

        public void Begin(FishPlayer self, FishOrbitCamera orbit, CreatureVision vision)
        {
            _self = self;
            _orbit = orbit;
            _vision = vision;
            Active = this;
            enabled = true;

            // Already watching someone (the shark that just ate us)? Stay on them.
            Gather();
            var current = orbit != null && orbit.SpectateTarget != null ? orbit.SpectateTarget.GetComponent<FishPlayer>() : null;
            if (current != null && _players.Contains(current)) Watch(current);
            else Step(0);
        }

        void Awake()
        {
            _next = new InputAction("SpectateNext", InputActionType.Button);
            _next.AddBinding("<Mouse>/leftButton");
            _next.AddBinding("<Gamepad>/rightShoulder");
            _previous = new InputAction("SpectatePrevious", InputActionType.Button);
            _previous.AddBinding("<Mouse>/rightButton");
            _previous.AddBinding("<Gamepad>/leftShoulder");
            _hint = new InputAction("GhostHint", InputActionType.Button);
            _hint.AddBinding("<Keyboard>/g");
            _hint.AddBinding("<Gamepad>/buttonNorth");
        }

        void OnEnable() { _next.Enable(); _previous.Enable(); _hint.Enable(); }
        void OnDisable() { _next.Disable(); _previous.Disable(); _hint.Disable(); }

        void OnDestroy()
        {
            _next.Dispose();
            _previous.Dispose();
            _hint.Dispose();
            if (Active == this) Active = null;
        }

        void Update()
        {
            if (_orbit == null) return;
            Gather();

            // Someone else pointed the camera (the bite that took us): follow along.
            var pointed = _orbit.SpectateTarget != null ? _orbit.SpectateTarget.GetComponent<FishPlayer>() : null;
            if (pointed != null && pointed != _target && _players.Contains(pointed)) Watch(pointed);

            // Whoever we watched escaped, was eaten or left: on to the next.
            if (_target == null || !_players.Contains(_target)) Step(0);

            if (PauseMenu.IsOpen || RoundScreen.ShowingResults) return;
            if (_next.WasPressedThisFrame()) Step(+1);
            else if (_previous.WasPressedThisFrame()) Step(-1);

            // A ghost's one hint: show the shark to the fish still in the water.
            if (_self != null && _self.CanGhostHint)
            {
                MatchHud.ShowPrompt("Press G to show the shark to the fish (once)");
                if (_hint.WasPressedThisFrame()) _self.RequestGhostHint();
            }
        }

        // Everyone still in the water but us: sharks first, then fish, each by name.
        void Gather()
        {
            _players.Clear();
            foreach (var fp in FishPlayer.All)
                if (fp != null && fp != _self && fp.isActiveAndEnabled && !fp.IsOut && !fp.IsBeingEaten)
                    _players.Add(fp);
            _players.Sort((a, b) =>
            {
                int side = b.IsSharkBody.CompareTo(a.IsSharkBody);
                return side != 0 ? side : string.CompareOrdinal(a.PlayerName, b.PlayerName);
            });
        }

        void Step(int direction)
        {
            if (_players.Count == 0)
            {
                _target = null;
                return;
            }
            int at = _target != null ? _players.IndexOf(_target) : -1;
            // Lost them: carry on from the spot they held.
            int i = at >= 0 ? at + direction : _index;
            i = ((i % _players.Count) + _players.Count) % _players.Count;
            Watch(_players[i]);
        }

        void Watch(FishPlayer fp)
        {
            _target = fp;
            _index = _players.IndexOf(fp);
            _orbit.Spectate(fp.transform);
            if (_vision != null) _vision.ApplyRole(fp.IsSharkBody ? FishRole.Shark : FishRole.Fish);
        }
    }
}
