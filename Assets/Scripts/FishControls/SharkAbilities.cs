using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FishGame
{
    /// <summary>
    /// The hunter's offense, layered on top of the shared movement (FishController + FishMotor).
    ///
    ///   * ATTACK  (Left Click): a short forward dash + bite animation, on a small cooldown.
    ///     If a fish is in the mouth during the bite, it gets eaten.
    ///   * LUNGE   (Q / Left Trigger): a big, fast, long-distance dash on a longer cooldown.
    ///     If the shark catches up to a fish and it enters eat range, the lunge is cut short
    ///     and the shark bites/eats instead of overshooting.
    ///   * SONAR   (R / Y): a pulse on a long cooldown. Every player fish within <see cref="sonarRadius"/> that is
    ///     moving shows up for a moment (<see cref="SharkTargeting.SonarSweep"/>); a fish that keeps still doesn't.
    ///   * NEAR MISSES: a fish just out of the mouth but ahead of the head (<see cref="snapReach"/>,
    ///     <see cref="snapAngle"/>) is snapped up anyway - the devour pulls it into the jaws. A player fish
    ///     that came within <see cref="grazeReach"/> during a bite that caught nothing is grazed instead:
    ///     hurt and bleeding (<see cref="GrazedPrey"/>), and too weak to tear free if it's caught again soon.
    ///
    /// NETCODE: this stays Mirror-agnostic (like FishController). When it detects a catch it
    /// does the instant local feedback (stop + bite anim) and raises <see cref="CaughtPrey"/>.
    /// In single-player there's no subscriber, so it falls back to eating locally. In Mirror,
    /// have your FishPlayer/shark NetworkBehaviour subscribe to <see cref="CaughtPrey"/> and send
    /// a [Command] so the SERVER validates and performs the actual Devour. Enable this component
    /// only for the local player (same pattern FishPlayer uses for FishController).
    /// </summary>
    [RequireComponent(typeof(FishMotor))]
    public class SharkAbilities : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] FishMotor motor;
        [Tooltip("Animator with the bite/eat clip. Optional - leave empty to skip animation.")]
        [SerializeField] Animator animator;

        [Header("Attack (Left Click)")]
        [SerializeField] float attackDashSpeed = 12f;
        [SerializeField] float attackDashDuration = 0.18f;
        [Range(0f, 1f)][SerializeField] float attackSteerControl = 0.5f;
        [SerializeField] float attackCooldown = 0.6f;

        [Header("Lunge (Q / Left Trigger)")]
        [SerializeField] float lungeSpeed = 26f;
        [SerializeField] float lungeDuration = 0.5f;
        [Range(0f, 1f)][SerializeField] float lungeSteerControl = 0.15f;
        [SerializeField] float lungeCooldown = 4f;

        [Header("Sonar (R / Y)")]
        [Tooltip("How far the pulse reaches (metres).")]
        [SerializeField] float sonarRadius = 40f;
        [Tooltip("A player fish moving faster than this (m/s) when the pulse goes out is revealed.")]
        [SerializeField] float sonarMovingSpeed = 1.2f;
        [Tooltip("How long a fish the pulse caught stays revealed and lockable (seconds).")]
        [SerializeField] float sonarRevealSeconds = 2.5f;
        [SerializeField] float sonarCooldown = 25f;

        [Header("Eating")]
        [Tooltip("Distance from the shark's pivot forward to its 'mouth'.")]
        [SerializeField] float mouthOffset = 1.0f;
        [Tooltip("How close a fish must be to the mouth to be eaten.")]
        [SerializeField] float eatRadius = 0.9f;
        [Tooltip("Which layers can contain prey. Leave as Everything and filter by the Edible component.")]
        [SerializeField] LayerMask preyMask = ~0;

        [Header("Near misses")]
        [Tooltip("A fish this close to the mouth (metres) and ahead of the head is snapped up even though " +
                 "it isn't in the mouth.")]
        [SerializeField] float snapReach = 1.8f;
        [Tooltip("How far off the shark's heading (degrees) a fish may be and still be snapped up.")]
        [Range(0f, 90f)][SerializeField] float snapAngle = 40f;
        [Tooltip("A player fish this close to the mouth at some point during a bite that caught nothing is " +
                 "grazed: hurt and bleeding.")]
        [SerializeField] float grazeReach = 2.6f;
        [Tooltip("Health a graze takes off a player fish.")]
        [SerializeField] float grazeDamage = 15f;
        [Tooltip("Seconds a grazed fish bleeds and stays wounded. Caught again while wounded, it can't tear free.")]
        [SerializeField] float grazeWoundSeconds = 12f;

        /// <summary>Health a graze takes (the host reads these off the shark when it applies one).</summary>
        public float GrazeDamage => grazeDamage;
        /// <summary>How long a grazed fish bleeds and stays too weak to tear free.</summary>
        public float GrazeWoundSeconds => grazeWoundSeconds;

        [Header("Food (the shark only feeds on fish)")]
        [Tooltip("Hunger restored by eating an ambient school fish.")]
        [SerializeField] float flockFishFood = 20f;
        [Tooltip("Hunger restored by eating a player fish.")]
        [SerializeField] float playerFishFood = 60f;

        [Header("Target lock")]
        [Tooltip("Optional focus system. When it has a locked fish, a bite eats THAT fish specifically, " +
                 "ignoring closer ones. Auto-found on this object if left empty.")]
        [SerializeField] SharkTargeting targeting;
        [Tooltip("The locked fish's bite range is eatRadius x this - a little more forgiving than a " +
                 "blind bite, since you've committed to that fish.")]
        [SerializeField] float focusEatMultiplier = 1.4f;

        [Header("Devour")]
        [Tooltip("Empty transform at the shark's mouth (parent it to the jaw bone). A caught fish is pulled here.")]
        [SerializeField] Transform mouthAnchor;
        [Tooltip("Also eat ambient NPC flock fish (detected via BoidsManager, since they have no colliders).")]
        [SerializeField] bool eatFlockFish = true;
        [Range(0f, 1f)]
        [Tooltip("Fraction of the shark's speed kept when it catches a fish. 1 = full momentum, 0 = stop dead.")]
        [SerializeField] float momentumOnCatch = 0.85f;

        [Header("Animator")]
        [SerializeField] string biteTrigger = "Bite";
        [Tooltip("Trigger for the devour/eat clip, played when a fish is actually caught.")]
        [SerializeField] string devourTrigger = "Devour";
        [Tooltip("Bool held on while a caught player fish is being shaken in the jaws (the thrash loop).")]
        [SerializeField] string holdingBool = "Holding";

        /// <summary>The shark's mouth anchor, so a networked eat handler can pass it to Edible.Devour.</summary>
        public Transform MouthAnchor => mouthAnchor;

        /// <summary>
        /// Raised on the owner client the instant a bite connects with an <see cref="Edible"/>.
        /// Subscribe from your Mirror layer to route the kill through a server [Command].
        /// If nobody subscribes, the shark eats locally (single-player / testing).
        /// </summary>
        public event Action<Edible> CaughtPrey;

        /// <summary>
        /// Raised on the owner client after it devours an ambient flock fish (that part is always
        /// local, for instant feedback). Carries the school and the fish's id in it, so the Mirror
        /// layer can have the host remove that same fish for every other player.
        /// </summary>
        public event Action<BoidsManager, int> CaughtFlockFish;

        /// <summary>Raised on the owner when it attacks (false) or lunges (true), so the Mirror layer can show
        /// the swing and the dash's bubbles to everyone else (<see cref="PlayAttackFx"/>).</summary>
        public event Action<bool> Attacked;

        /// <summary>Raised on the owner when it sends a sonar pulse, so the Mirror layer can show it to everyone.</summary>
        public event Action Sonared;

        /// <summary>Raised on the owner when a bite that caught nothing passed close to a player fish (a graze).
        /// The Mirror layer has the host hurt it; with no subscriber it's hurt here (single-player / test).</summary>
        public event Action<Edible> GrazedPrey;

        InputAction _attackAction;
        InputAction _lungeAction;
        InputAction _sonarAction;
        float _sonarCd;

        float _attackCd;
        float _lungeCd;
        float _eatWindow; // > 0 while a dash can connect into a bite
        int _biteId;
        int _devourId;
        int _holdingId;
        bool _holding;          // a player fish is in our jaws, struggling
        float _noBitesUntil;    // a fish just broke free: a moment before we can bite again
        Edible _grazed;         // the player fish that came closest during this bite, inside grazeReach
        float _grazedDist;
        readonly Collider[] _hits = new Collider[16];

        public bool AttackReady => _attackCd <= 0f;
        public bool LungeReady => _lungeCd <= 0f;
        public bool SonarReady => _sonarCd <= 0f;
        /// <summary>0 = just used, 1 = ready (for the HUD).</summary>
        public float LungeCharge => lungeCooldown > 0f ? 1f - Mathf.Clamp01(_lungeCd / lungeCooldown) : 1f;
        public float SonarCharge => sonarCooldown > 0f ? 1f - Mathf.Clamp01(_sonarCd / sonarCooldown) : 1f;
        public float SonarRadius => sonarRadius;

        /// <summary>A caught player fish is in our jaws (being shaken until it's swallowed or breaks free).</summary>
        public bool IsHolding => _holding;

        /// <summary>Every loaded instance, enabled or not (what FindAnyObjectByType would search, without
        /// searching the scene).</summary>
        internal static readonly System.Collections.Generic.List<SharkAbilities> Loaded = new System.Collections.Generic.List<SharkAbilities>();

        void OnDestroy() => Loaded.Remove(this);

        void Awake()
        {
            Loaded.Add(this);
            if (motor == null) motor = GetComponent<FishMotor>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (targeting == null) TryGetComponent(out targeting);
            _biteId = Animator.StringToHash(biteTrigger);
            _devourId = Animator.StringToHash(devourTrigger);
            _holdingId = Animator.StringToHash(holdingBool);

            _attackAction = new InputAction("Attack", InputActionType.Button);
            _attackAction.AddBinding("<Mouse>/leftButton");
            _attackAction.AddBinding("<Gamepad>/rightTrigger");

            _lungeAction = new InputAction("Lunge", InputActionType.Button);
            _lungeAction.AddBinding("<Keyboard>/q");
            _lungeAction.AddBinding("<Gamepad>/leftTrigger");

            _sonarAction = new InputAction("Sonar", InputActionType.Button);
            _sonarAction.AddBinding("<Keyboard>/r");
            _sonarAction.AddBinding("<Gamepad>/buttonNorth");
        }

        void OnEnable()
        {
            _attackAction.Enable();
            _lungeAction.Enable();
            _sonarAction.Enable();
        }

        void OnDisable()
        {
            _attackAction.Disable();
            _lungeAction.Disable();
            _sonarAction.Disable();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (_attackCd > 0f) _attackCd -= dt;
            if (_lungeCd > 0f) _lungeCd -= dt;
            if (_sonarCd > 0f) _sonarCd -= dt;
            if (_eatWindow > 0f)
            {
                _eatWindow -= dt;
                if (_eatWindow <= 0f) EndBite(); // the bite closed on nothing: maybe it grazed someone
            }

            // Jaws full (or the last catch just tore free): no new bites for now.
            if (_holding || Time.time < _noBitesUntil)
            {
                _eatWindow = 0f;
                _grazed = null;
                return;
            }

            // The pulse doesn't move the shark, so it goes out even mid-dash.
            if (SonarReady && _sonarAction.WasPressedThisFrame() && !PauseMenu.IsOpen)
                DoSonar();

            // Don't let a new ability interrupt an in-progress dash.
            if (!motor.IsDashing)
            {
                if (AttackReady && _attackAction.WasPressedThisFrame())
                    DoAttack();
                else if (LungeReady && _lungeAction.WasPressedThisFrame())
                    DoLunge();
            }

            if (_eatWindow > 0f)
                TryEat();
        }

        void DoAttack()
        {
            motor.BeginDash(attackDashSpeed, attackDashDuration, attackSteerControl);
            PlayAttackFx(lunge: false); // the swing and its bubbles happen whether or not it connects
            Attacked?.Invoke(false);
            _attackCd = attackCooldown;
            _eatWindow = attackDashDuration + 0.05f;
            _grazed = null;
        }

        void DoLunge()
        {
            motor.BeginDash(lungeSpeed, lungeDuration, lungeSteerControl);
            PlayAttackFx(lunge: true);
            Attacked?.Invoke(true);
            _lungeCd = lungeCooldown;
            _eatWindow = lungeDuration; // contact anywhere along the lunge = a catch
            _grazed = null;
        }

        void DoSonar()
        {
            _sonarCd = sonarCooldown;
            PlaySonarFx();
            if (targeting != null) targeting.SonarSweep(transform.position, sonarRadius, sonarMovingSpeed, sonarRevealSeconds);
            Sonared?.Invoke();
        }

        /// <summary>Every machine: the pulse going out - the water buckling around the shark and a burst of bubbles.</summary>
        public void PlaySonarFx()
        {
            WaterFx.Shockwave(transform.position, 14f, 0.9f);
            WaterFx.BubbleBurst(transform.position, 24, 1.2f, 3f, new Vector2(0.04f, 0.1f));
            // The ping carries: anyone within the pulse hears it go out.
            var lib = GameAudio.Lib;
            if (lib != null) GameAudio.PlayAt(lib.sonar, transform.position, 1f, sonarRadius * 1.6f, 0f);
        }

        /// <summary>Every machine: what an attack looks like - the bite swing (not on a lunge) and the fizz of
        /// bubbles torn off the body for the length of the dash.</summary>
        public void PlayAttackFx(bool lunge)
        {
            if (!lunge) TriggerBite();
            if (TryGetComponent(out SwimTrail trail)) trail.Dash(lunge ? lungeDuration : attackDashDuration);
            var lib = GameAudio.Lib;
            if (lib != null) GameAudio.PlayAt(lunge ? lib.lunge : GameAudio.Pick(lib.swishes), transform.position, 0.9f, 30f);
        }

        void TryEat()
        {
            // Eat where the mouth actually is (the anchor bone), falling back to a forward offset.
            Vector3 mouth = mouthAnchor != null ? mouthAnchor.position : transform.position + transform.forward * mouthOffset;

            // 0) Focused bite: if a fish is locked, the shark commits to THAT fish. A bite eats it once
            //    it's in (a slightly forgiving) mouth range, and never grabs a different fish instead -
            //    even one that's closer. Tap the lock key again to release and hunt freely.
            if (targeting != null && targeting.LockedTarget != null)
            {
                var locked = targeting.LockedTarget;
                Vector3 lp = locked.transform.position;
                float focusR = eatRadius * focusEatMultiplier;
                if ((lp - mouth).sqrMagnitude <= focusR * focusR || InSnapReach(lp, mouth))
                    EatSpecific(locked);
                else if (locked.TryGetComponent(out Edible lockedEdible))
                    NoteGraze(lockedEdible, Vector3.Distance(lp, mouth));
                return;
            }

            // 1) Players / anything with a collider + Edible.
            int count = Physics.OverlapSphereNonAlloc(mouth, eatRadius, _hits, preyMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var edible = _hits[i].GetComponentInParent<Edible>();
                if (edible == null || edible.IsEaten || edible.gameObject == gameObject) continue;
                Consume(edible.gameObject, edible);
                return;
            }

            // (Food pellets are fish food - the shark only gets fed by eating fish.)

            // 2) Ambient NPC flock fish (no colliders) — found via the BoidsManager positions.
            if (eatFlockFish)
            {
                var npc = BoidsManager.EatNearestFish(mouth, eatRadius, out var flock, out int id);
                if (npc != null) { EatFlockFish(npc, flock, id); return; }
            }

            // 3) Near misses. A fish just ahead of the head is snapped up (the devour pulls it into the
            //    jaws); a player fish merely close by is remembered, and grazed if the bite catches nothing.
            Edible snap = null;
            float snapBest = float.MaxValue;
            count = Physics.OverlapSphereNonAlloc(mouth, Mathf.Max(snapReach, grazeReach), _hits, preyMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var edible = _hits[i].GetComponentInParent<Edible>();
                if (edible == null || edible.IsEaten || edible.gameObject == gameObject) continue;
                Vector3 p = edible.transform.position;
                float d = Vector3.Distance(p, mouth);
                if (d < snapBest && InSnapReach(p, mouth)) { snap = edible; snapBest = d; }
                NoteGraze(edible, d);
            }
            if (snap != null) { Consume(snap.gameObject, snap); return; }

            if (eatFlockFish)
            {
                var npc = NearestFlockFishInSnapReach(mouth);
                if (npc != null && BoidsManager.DetachFish(npc, out var flock, out int id)) EatFlockFish(npc, flock, id);
            }
        }

        /// <summary>Close enough to the mouth, and ahead of the head rather than off to its side.</summary>
        bool InSnapReach(Vector3 p, Vector3 mouth) =>
            (p - mouth).sqrMagnitude <= snapReach * snapReach &&
            Vector3.Angle(transform.forward, p - transform.position) <= snapAngle;

        GameObject NearestFlockFishInSnapReach(Vector3 mouth)
        {
            GameObject best = null;
            float bestSqr = snapReach * snapReach;
            for (int m = 0; m < BoidsManager.All.Count; m++)
            {
                var mgr = BoidsManager.All[m];
                if (mgr == null || mgr.allFish == null) continue;
                foreach (var f in mgr.allFish)
                {
                    if (f == null) continue;
                    Vector3 p = f.transform.position;
                    float d = (p - mouth).sqrMagnitude;
                    if (d < bestSqr && InSnapReach(p, mouth)) { bestSqr = d; best = f; }
                }
            }
            return best;
        }

        /// <summary>A player fish came within graze reach during this bite: keep the closest.</summary>
        void NoteGraze(Edible edible, float dist)
        {
            if (dist > grazeReach || edible == null || edible.IsEaten) return;
            if (edible.GetComponentInParent<FishPlayer>() == null) return; // school fish are just food
            if (_grazed != null && dist >= _grazedDist) return;
            _grazed = edible;
            _grazedDist = dist;
        }

        // The bite ran out without a catch. A player fish that came close is grazed.
        void EndBite()
        {
            var grazed = _grazed;
            _grazed = null;
            if (grazed == null || grazed.IsEaten || _holding) return;
            MatchHud.Flash("Grazed!");
            if (GrazedPrey != null) GrazedPrey.Invoke(grazed);  // networked: the host hurts it
            else if (grazed.TryGetComponent(out FishVitals vitals)) vitals.Damage(grazeDamage, grazeWoundSeconds);
        }

        /// <summary>Eat one particular fish (the focused bite). Player prey carry an Edible; ambient
        /// flock fish don't and must first be pulled out of the school so the boids job stops driving
        /// them, after which Consume gives them an Edible for the same devour treatment.</summary>
        void EatSpecific(GameObject fish)
        {
            if (fish == null) return;

            // Flock fish live in the boids sim (and the prefab also carries an Edible): pull it out of
            // its school first, or the job keeps steering a fish that's being devoured.
            if (BoidsManager.DetachFish(fish, out var flock, out int id))
            {
                EatFlockFish(fish, flock, id);
                return;
            }

            var edible = fish.GetComponentInParent<Edible>();
            if (edible != null && !edible.IsEaten)
                Consume(edible.gameObject, edible);
        }

        /// <summary>An ambient flock fish, already detached from its school. Devoured locally right away
        /// (instant feedback), then announced so every other player's copy goes into our jaws too.</summary>
        void EatFlockFish(GameObject fish, BoidsManager flock, int id)
        {
            motor.CancelDash(momentumOnCatch);
            TriggerDevour();
            _eatWindow = 0f;

            if (!fish.TryGetComponent(out Edible edible)) edible = fish.AddComponent<Edible>();
            edible.Devour(gameObject, mouthAnchor, bloodSeed: BloodInWater.SeedFor(flock.NetId, id)); // same seed FlockNetSync uses
            if (CaughtFlockFish != null) CaughtFlockFish.Invoke(flock, id); // networked: the host feeds us once it confirms
            else FeedFor(playerFish: false);                                // single-player / test
        }

        /// <summary>Restore the shark's hunger for a fish it ate. Only takes effect where the vitals are
        /// authoritative (host or offline), so the network layer calls it once the bite is confirmed.</summary>
        public void FeedFor(bool playerFish)
        {
            if (TryGetComponent(out FishVitals vitals))
                vitals.Feed(playerFish ? playerFishFood : flockFishFood);
        }

        void Consume(GameObject fish, Edible edible)
        {
            // Keep most of the shark's momentum so it eats on the move instead of stopping dead.
            motor.CancelDash(momentumOnCatch);
            // A player fish isn't eaten outright: once the host confirms the bite we hold and shake it
            // (SetHolding) while it struggles. Anything else goes straight down.
            bool player = edible != null && CaughtPrey != null && edible.GetComponentInParent<FishPlayer>() != null;
            if (!player) TriggerDevour();
            _eatWindow = 0f;

            if (edible != null)
            {
                if (CaughtPrey != null)
                    CaughtPrey.Invoke(edible);              // networked: server validates, devours + feeds us
                else
                {
                    edible.Devour(gameObject, mouthAnchor); // single-player / test fallback
                    FeedFor(playerFish: true);
                }
            }
            else
            {
                // Ambient NPC fish (no Edible): give it one so it gets the SAME smooth
                // pull-into-the-mouth treatment as the player prey, then it despawns itself.
                fish.AddComponent<Edible>().Devour(gameObject, mouthAnchor);
            }
        }

        /// <summary>Every machine: shake a caught player fish in the jaws (the thrash loop), or stop. While
        /// holding, this shark (on its owner's machine) can't bite anything else.</summary>
        public void SetHolding(bool holding)
        {
            _holding = holding;
            if (animator != null && !string.IsNullOrEmpty(holdingBool))
                animator.SetBool(_holdingId, holding);
        }

        /// <summary>Every machine: the violent eat - a school fish going down, or the final swallow of a player.</summary>
        public void PlayDevour() => TriggerDevour();

        /// <summary>The catch tore free: no bites for <paramref name="seconds"/>, so it gets a head start.</summary>
        public void Stun(float seconds)
        {
            _noBitesUntil = Time.time + seconds;
            _eatWindow = 0f;
        }

        void TriggerBite()
        {
            if (animator != null && !string.IsNullOrEmpty(biteTrigger))
                animator.SetTrigger(_biteId);
        }

        void TriggerDevour()
        {
            if (animator != null && !string.IsNullOrEmpty(devourTrigger))
                animator.SetTrigger(_devourId);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Vector3 mouth = mouthAnchor != null ? mouthAnchor.position : transform.position + transform.forward * mouthOffset;
            Gizmos.DrawWireSphere(mouth, eatRadius);
            Gizmos.color = new Color(1f, 0.6f, 0f);
            Gizmos.DrawWireSphere(mouth, snapReach);
            Gizmos.color = new Color(1f, 1f, 0f, 0.5f);
            Gizmos.DrawWireSphere(mouth, grazeReach);
        }
    }
}
