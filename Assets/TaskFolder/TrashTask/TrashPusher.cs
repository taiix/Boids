using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// Lets a player fish push trash (<see cref="PushableTrash"/>). Goes on the PlayerFish prefab; the shark
    /// doesn't carry one, so the trash ignores it.
    ///
    /// Pushing is NOT left to physics collisions: on the host every other player's fish is a kinematic
    /// body that the network teleports between updates, so the host's own fish would shove trash
    /// smoothly while everyone else's barely could, and "two fish at once" would depend on who the host
    /// happens to see touching it. Instead each player's own machine decides whether its fish is pushing
    /// - nose against a piece of trash and swimming into it - and tells the server which piece (only
    /// when that changes). The server checks the fish really is there and adds up everyone's pushes
    /// (<see cref="PushableTrash"/>). Locally the trash still has a collider, so the fish bumps into it
    /// and it feels solid.
    /// </summary>
    [RequireComponent(typeof(NetworkIdentity))]
    public class TrashPusher : NetworkBehaviour
    {
        [Tooltip("How close the fish's body has to be to a piece of trash to push it, in metres.")]
        [SerializeField] float contactGap = 0.5f;
        [Tooltip("Only counts while swimming into it: at least this much forward throttle...")]
        [SerializeField] float minThrottle = 0.2f;
        [Tooltip("...with the nose toward it (cosine of the angle off the nose; 0.3 is within about 70°).")]
        [SerializeField] float minFacing = 0.3f;
        [Tooltip("Extra contact distance the server allows another player's fish: it sees them a moment late.")]
        [SerializeField] float remoteSlack = 1.5f;
        [Tooltip("While pushing, the fish can't swim faster than the trash can go plus this (m/s): it leans into " +
                 "the load instead of bouncing off it, and pushing feels heavy.")]
        [SerializeField] float pushSpeedMargin = 0.6f;

        /// <summary>Every fish that can push trash (for the server's count).</summary>
        public static readonly List<TrashPusher> All = new List<TrashPusher>();

        /// <summary>Server / offline: the trash this fish is pushing, or null.</summary>
        public PushableTrash Pushing { get; private set; }
        /// <summary>This machine's own fish: the trash it's pushing, as it sees it (for the HUD).</summary>
        public PushableTrash LocalPushing { get; private set; }
        /// <summary>Server / offline: seconds spent pushing sharp trash since the last cut.</summary>
        public float SharpTime { get; set; }
        public FishVitals Vitals { get; private set; }

        Collider[] _body;
        FishMotor _motor;
        FishPlayer _player;
        PushableTrash _sent; // what we last told the server we're pushing
        bool _reported;
        bool _limiting;      // we capped the motor's speed (and must give it back)
        float _limitUntil;

        bool Offline => !NetworkServer.active && !NetworkClient.active;
        /// <summary>The fish this machine's player is driving.</summary>
        public bool IsLocalFish => _player != null && (_player.isLocalPlayer || _player.IsOfflineSandboxBody);

        void Awake()
        {
            _motor = GetComponent<FishMotor>();
            _player = GetComponent<FishPlayer>();
            Vitals = GetComponent<FishVitals>();
            var solid = new List<Collider>();
            foreach (var c in GetComponentsInChildren<Collider>(true))
                if (!c.isTrigger) solid.Add(c);
            _body = solid.ToArray();
        }

        void OnEnable() => All.Add(this);
        void OnDisable()
        {
            All.Remove(this);
            Pushing = null;
            LocalPushing = null;
            ReleaseSpeed();
        }

        void Update()
        {
            if (!IsLocalFish) return;
            var target = FindPushed();
            LocalPushing = target;
            LimitSpeed(target);

            // Offline, or the host's own fish: this machine is the server, so just set it.
            if (Offline || isServer) { Pushing = target; return; }

            // A remote player: tell the server, only when it changes.
            if (_reported && target == _sent) return;
            _reported = true;
            _sent = target;
            CmdSetPushing(target != null ? target.netId : 0u);
        }

        // A fish twice the trash's speed just glances off a round load; held to its pace, it stays behind it
        // and keeps shoving. Kept on a moment after contact breaks, so a bump doesn't flick it on and off.
        void LimitSpeed(PushableTrash target)
        {
            if (_motor == null) return;
            if (target != null)
            {
                _motor.SetSpeedLimit(target.MaxSpeed + pushSpeedMargin);
                _limiting = true;
                _limitUntil = Time.time + 0.4f;
            }
            else if (_limiting && Time.time > _limitUntil) ReleaseSpeed();
        }

        void ReleaseSpeed()
        {
            if (!_limiting) return;
            _limiting = false;
            if (_motor != null) _motor.ClearSpeedLimit();
        }

        // The trash this fish is swimming into right now, if any: nose toward it, body against it, throttle on.
        PushableTrash FindPushed()
        {
            if (_motor == null || !_motor.enabled || _motor.Throttle < minThrottle) return null;
            if (_player != null && _player.IsBeingEaten) return null;
            if (Vitals != null && Vitals.IsDead) return null;

            PushableTrash best = null;
            float bestGap = contactGap;
            Vector3 nose = transform.forward;
            foreach (var trash in PushableTrash.All)
            {
                if (trash == null || !trash.CanBePushed) continue;
                Vector3 to = trash.Centre - transform.position;
                if (to.sqrMagnitude > 1e-6f && Vector3.Dot(nose, to.normalized) < minFacing) continue;
                float gap = Gap(trash);
                if (gap <= bestGap) { bestGap = gap; best = trash; }
            }
            return best;
        }

        /// <summary>Distance from this fish's body to the trash's surface, in metres (0 or less = touching).</summary>
        public float Gap(PushableTrash trash)
        {
            Vector3 c = trash.Centre;
            float best = float.MaxValue;
            foreach (var col in _body)
            {
                if (col == null || !col.enabled || !col.gameObject.activeInHierarchy) continue;
                best = Mathf.Min(best, (c - col.ClosestPoint(c)).magnitude);
            }
            if (best == float.MaxValue) best = (c - transform.position).magnitude;
            return best - trash.Radius;
        }

        /// <summary>Server / offline: is this fish really up against the trash it says it's pushing?</summary>
        public bool ServerTouching(PushableTrash trash)
        {
            bool mine = Offline || (_player != null && _player.isLocalPlayer);
            return Gap(trash) <= contactGap + (mine ? 0.25f : remoteSlack);
        }

        /// <summary>Which way this fish shoves the trash: mostly where it's heading, partly from where it's
        /// touching (straight through the trash's middle) - steerable, but a glancing push still sends it a
        /// bit sideways. Up and down count too: swim into it at an upward angle and it lifts.</summary>
        public Vector3 PushDirection(PushableTrash trash)
        {
            Vector3 through = trash.Centre - transform.position;
            Vector3 heading = transform.forward;
            Vector3 dir = through.normalized * 0.4f + heading.normalized * 0.6f;
            if (dir.sqrMagnitude < 1e-4f) dir = heading.sqrMagnitude > 1e-4f ? heading : through;
            return dir.normalized;
        }

        [Command]
        void CmdSetPushing(uint trashNetId)
        {
            PushableTrash trash = null;
            if (trashNetId != 0 && NetworkServer.spawned.TryGetValue(trashNetId, out var identity))
                identity.TryGetComponent(out trash);
            Pushing = trash;
        }
    }
}
