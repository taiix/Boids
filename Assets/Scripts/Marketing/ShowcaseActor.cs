using UnityEngine;

namespace FishGame
{
    /// <summary>What a <see cref="ShowcaseActor"/> mark spawns. Every kind is a real game prefab.</summary>
    public enum ShowcaseActorKind
    {
        Shark = 0,          // the great white (the menu's gameplay-free copy of the player shark's model)
        PlayerFish = 1,     // a player fish body (the school fish model at player size)
        School = 2,         // a whole flock: a BoidsManager running the game's own Burst flocking
        TangledTurtle = 3,  // the turtle rescue task: a loggerhead caught in a ghost net
        SwimmingTurtle = 4, // a free turtle roaming the reef
    }

    /// <summary>
    /// Where something has to be when a <see cref="ShowcaseShot"/> is captured. Put it under the shot and
    /// point its blue (Z) axis the way the body faces. Bodies with a swim speed start behind the mark and
    /// arrive on it at the moment of capture, so the game's own reactions (schools fleeing, bubble wakes)
    /// have played out naturally by then.
    /// </summary>
    public class ShowcaseActor : MonoBehaviour
    {
        public ShowcaseActorKind kind;

        [Header("Placement")]
        [Tooltip("Rest on the seabed (it is regenerated every run) at this height above it, instead of the mark's own height.")]
        public bool snapToSeabed;
        public float heightAboveSeabed = 2f;

        [Header("Motion")]
        [Tooltip("Metres per second along the mark's forward axis. The body starts behind the mark and reaches it at capture.")]
        public float swimSpeed;
        [Tooltip("Gentle turn while swimming, degrees per second around Y (arrives facing the mark's heading).")]
        public float turnRate;
        [Tooltip("Scale multiplier on the prefab's own size (1 = as in the game).")]
        public float scale = 1f;

        [Header("Animation")]
        [Tooltip("Shark: the Animator's Speed parameter (0 idle .. 1 full swim).")]
        [Range(0f, 1f)] public float animSpeed = 0.5f;
        [Tooltip("Shark: the Boosting flag the lunge sets.")]
        public bool boosting;
        [Tooltip("Optional Animator state to be in at capture (e.g. DevourViolent, Thrash). Empty = swimming.")]
        public string animState = "";
        [Tooltip("Seconds into that state at capture.")]
        public float animStateTime = 0.3f;
        [Tooltip("Shark: how far the jaw is open, 0..1 (same rig and angles as the main menu shark).")]
        [Range(0f, 1f)] public float jawOpen;

        [Header("Shark")]
        [Tooltip("Schools flee it (the game's Predator component).")]
        public bool predator = true;
        [Tooltip("Shark: the lunge's bubble wake torn off the tail for this many seconds before capture (0 = none).")]
        public float dashWakeSeconds;

        [Header("Player fish")]
        [Tooltip("The teammate glow a fish player sees on the other fish (FishAllies' material).")]
        public bool allyGlow;
        [Tooltip("Seconds before capture the shark catches this fish (Edible.Devour: blood, crunch, into the jaws). 0 = never. Needs a Shark actor in the same shot.")]
        public float eatenBeforeCapture;

        [Header("School")]
        [Min(1)] public int schoolSize = 220;
        [Tooltip("Size of the box the school roams in, centred on the mark.")]
        public Vector3 schoolArea = new Vector3(18f, 5f, 18f);
        [Tooltip("Flock seed (same seed = same school).")]
        public uint schoolSeed = 1;
        [Tooltip("Seconds the school's roaming goal takes between waypoints (higher = lazier).")]
        public float schoolGoalInterval = 12f;

        void OnDrawGizmos()
        {
            Gizmos.color = kind switch
            {
                ShowcaseActorKind.Shark => new Color(1f, 0.3f, 0.3f),
                ShowcaseActorKind.School => new Color(1f, 0.6f, 0.8f, 0.5f),
                ShowcaseActorKind.PlayerFish => new Color(1f, 0.5f, 0.8f),
                _ => new Color(0.4f, 1f, 0.5f),
            };
            if (kind == ShowcaseActorKind.School)
            {
                Gizmos.DrawWireCube(transform.position, schoolArea);
                return;
            }
            Gizmos.DrawWireSphere(transform.position, kind == ShowcaseActorKind.Shark ? 1.2f : 0.4f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * (kind == ShowcaseActorKind.Shark ? 4f : 1.5f));
            if (swimSpeed > 0f)
            {
                Gizmos.color *= 0.6f;
                Gizmos.DrawLine(transform.position, transform.position - transform.forward * swimSpeed * 3f);
            }
        }
    }
}
