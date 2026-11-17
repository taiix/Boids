using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace FishGame
{
    /// <summary>
    /// Co-op task: a loggerhead turtle caught in a ghost net, held down by four ropes tied to rocks on the
    /// seabed. Fish bite through the ropes - but the knots go in pairs, and a pair has to be bitten at the same
    /// time. Each knot's word (SHELL, STAR...) only shows to a fish right at that rope, so the fish have to talk:
    /// "I'm on SHELL - where's the other SHELL?" - near enough for the shark to hear them too. Bite one knot on
    /// its own, or two that don't match, and the net pulls tighter and the bite slips back. Both pairs cut: the
    /// survival clock drops, and the turtle swims free and roams the reef (<see cref="TurtleSwimmer"/>), leaving
    /// the cut ropes and the net on the seabed.
    ///
    /// Spawned by <see cref="TurtleRescueTask"/> (by the server, or this machine offline), which decides here;
    /// every machine builds its own ropes and rocks from the shared state.
    /// </summary>
    [RequireComponent(typeof(NetworkIdentity))]
    public class TangledTurtle : NetworkBehaviour
    {
        static readonly string[] Words = { "SHELL", "STAR", "CRAB", "PEARL", "KELP", "WAVE", "CORAL", "ANCHOR" };
        // The ropes (0 by the head, 1 its left, 2 the tail, 3 its right) that pair up: two pairs per way.
        static readonly int[,] Pairings = { { 0, 2, 1, 3 }, { 0, 1, 2, 3 }, { 0, 3, 1, 2 } };

        public static readonly List<TangledTurtle> All = new List<TangledTurtle>();

        [Header("Parts (set by FishGame > Build Turtle Task Assets)")]
        [SerializeField] Transform model;
        [SerializeField] Animator animator;
        [SerializeField] Transform net;
        [Tooltip("Where the ropes tie onto the net: by the head, its left, the tail, its right.")]
        [SerializeField] Transform[] corners = new Transform[4];
        [SerializeField] Material ropeMaterial;
        [SerializeField] Material knotMaterial;
        [Tooltip("Rocks the ropes are tied to (one is picked for each rope).")]
        [SerializeField] GameObject[] rocks;

        [Header("Ropes")]
        [Tooltip("How far out from the turtle each rope's rock sits (metres).")]
        [SerializeField] float ropeReach = 2.8f;
        [SerializeField] float ropeThickness = 0.03f;
        [Tooltip("Where along each rope its knot is - the place to bite (0 at the net, 1 at the rock).")]
        [Range(0f, 1f)][SerializeField] float knotAlong = 0.55f;
        [Tooltip("How close a fish has to be to a knot to bite it (metres).")]
        [SerializeField] float biteRange = 1.6f;
        [SerializeField] float rockSize = 0.8f;

        [Header("Biting")]
        [Tooltip("Seconds of a matching pair bitten together to cut it.")]
        [SerializeField] float chewSeconds = 6f;
        [Tooltip("Seconds a knot can be bitten without its partner before the net pulls tighter.")]
        [SerializeField] float graceSeconds = 0.6f;
        [Tooltip("How much of a pair's progress slips away per second while the net pulls tight.")]
        [SerializeField] float slipPerSecond = 0.25f;

        [Header("Reward")]
        [Tooltip("Seconds off the survival clock when the turtle is freed.")]
        [SerializeField] float timeReward = 45f;

        [Header("The turtle")]
        [Tooltip("How high above the seabed it hangs, held down by the ropes (metres).")]
        [SerializeField] float hoverHeight = 1.3f;
        [Tooltip("Speed of its swim cycle while it struggles in the net.")]
        [SerializeField] float struggleSpeed = 0.45f;
        [Tooltip("A fish within this many metres sees what the turtle needs.")]
        [SerializeField] float hintRange = 12f;

        // ---- shared state ----
        [SyncVar] byte _wordA, _wordB;           // the two pairs' words (index into Words)
        [SyncVar] byte _pairing;                 // which ropes pair up (row of Pairings)
        [SyncVar] float _progressA, _progressB;  // each pair, 0..1 (cut at 1)
        [SyncVar] byte _biting;                  // a bit per rope: being bitten right now
        [SyncVar] bool _freed;

        // ---- server (or offline) ----
        readonly Dictionary<FishPlayer, int> _biters = new Dictionary<FishPlayer, int>();   // fish -> rope
        readonly Dictionary<int, int> _testBiters = new Dictionary<int, int>();             // DebugBite
        readonly List<FishPlayer> _dropped = new List<FishPlayer>();
        readonly float[] _lonely = new float[2];
        readonly float[] _nextTighten = new float[2];
        TurtleSwimmer _swimmer;

        // ---- every machine: looks ----
        class Rope
        {
            public Transform rock, upper, lower, knot;
            public Vector3 anchor, outward;
            public float sway;
        }
        readonly Rope[] _ropes = new Rope[4];
        bool _built, _shownFreed;
        Transform _leftovers;     // the rocks, ropes and (once it's free) the net: they stay when the turtle goes
        readonly bool[] _shownCut = new bool[2];
        Quaternion _modelRest;
        Vector3 _modelHome;
        float _thrashUntil, _bubbleDebt;

        // ---- every machine: this machine's own fish ----
        int _sentRope = -1;
        static int s_atRopeFrame = -10;

        /// <summary>This machine's fish is at one of the ropes: E bites it rather than eating.</summary>
        public static bool LocalAtRope => s_atRopeFrame >= Time.frameCount - 1;

        public bool Freed => _freed;
        public float HoverHeight => hoverHeight;
        bool Authority => isServer || !NetworkClient.active;

        int RopeOf(int pair, int which) => Pairings[_pairing % 3, pair * 2 + which];
        int PairOf(int rope) => Pairings[_pairing % 3, 0] == rope || Pairings[_pairing % 3, 1] == rope ? 0 : 1;
        int PartnerOf(int rope)
        {
            int pair = PairOf(rope);
            int a = RopeOf(pair, 0);
            return a == rope ? RopeOf(pair, 1) : a;
        }
        string WordOf(int rope) => Words[(PairOf(rope) == 0 ? _wordA : _wordB) % Words.Length];
        float Progress(int pair) => pair == 0 ? _progressA : _progressB;
        bool Cut(int pair) => Progress(pair) >= 1f;
        bool RopeCut(int rope) => Cut(PairOf(rope));
        int CutCount => (Cut(0) ? 1 : 0) + (Cut(1) ? 1 : 0);

        void Awake()
        {
            if (model != null)
            {
                _modelRest = model.localRotation;
                _modelHome = model.localPosition;
            }
            _swimmer = GetComponent<TurtleSwimmer>();
        }

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); CloseNet(null); }
        void OnDestroy()
        {
            if (_leftovers != null) Destroy(_leftovers.gameObject);
        }

        /// <summary>Server / offline, before it's spawned: deal out this net's knots.</summary>
        public void ServerSetup(System.Random rng)
        {
            int a = rng.Next(Words.Length), b = rng.Next(Words.Length - 1);
            if (b >= a) b++;
            _wordA = (byte)a;
            _wordB = (byte)b;
            _pairing = (byte)rng.Next(3);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (Authority) ServerTick(dt);
            if (!_built) _built = _freed ? AlreadyFree() : BuildRopes();
            UpdateLooks(dt);
            UpdateLocalFish();
        }

        // ================================================================= server (or offline)

        [Command(requiresAuthority = false)]
        void CmdBite(int rope, NetworkConnectionToClient sender = null)
        {
            var fish = sender != null && sender.identity != null ? sender.identity.GetComponent<FishPlayer>() : null;
            if (fish != null) ServerBite(fish, rope);
        }

        // A fish started (rope 0-3) or stopped (-1) biting.
        void ServerBite(FishPlayer fish, int rope)
        {
            if (rope >= 0 && CanBite(fish, rope)) _biters[fish] = rope;
            else _biters.Remove(fish);
        }

        bool CanBite(FishPlayer fish, int rope)
        {
            if (_freed || rope < 0 || rope > 3 || RopeCut(rope)) return false;
            if (fish == null) return true;   // a test bite (DebugBite)
            if (IsShark(fish) || Gone(fish)) return false;
            // The client's reach, plus some slack for the lag between the two.
            return (fish.transform.position - KnotPoint(rope)).sqrMagnitude < (biteRange + 1.5f) * (biteRange + 1.5f);
        }

        static bool Gone(FishPlayer p) => p == null || p.IsOut || p.IsBeingEaten;

        // The shark's body - also before roles are dealt (the offline sandbox's shark has none).
        static bool IsShark(FishPlayer p) => p.IsShark || p.IsSharkBody;

        void ServerTick(float dt)
        {
            if (_freed) return;

            // Who's biting what: biters that left, swam off, or whose rope is gone stop.
            _dropped.Clear();
            int mask = 0;
            foreach (var kv in _biters)
            {
                if (!CanBite(kv.Key, kv.Value)) { _dropped.Add(kv.Key); continue; }
                mask |= 1 << kv.Value;
            }
            foreach (var fish in _dropped) _biters.Remove(fish);
            foreach (var kv in _testBiters)
                if (CanBite(null, kv.Value)) mask |= 1 << kv.Value;
            if (_biting != (byte)mask) _biting = (byte)mask;

            for (int pair = 0; pair < 2; pair++)
            {
                float p = Progress(pair);
                if (p >= 1f) continue;
                bool a = (mask & (1 << RopeOf(pair, 0))) != 0, b = (mask & (1 << RopeOf(pair, 1))) != 0;
                if (a && b)
                {
                    p = Mathf.Min(1f, p + dt / chewSeconds);
                    _lonely[pair] = 0f;
                }
                else if (a || b)
                {
                    // One knot of a pair on its own: the net pulls tighter, and what's been bitten slips back.
                    _lonely[pair] += dt;
                    if (_lonely[pair] > graceSeconds)
                    {
                        p = Mathf.Max(0f, p - slipPerSecond * dt);
                        if (Time.time >= _nextTighten[pair])
                        {
                            _nextTighten[pair] = Time.time + 1.6f;
                            if (NetworkServer.active) RpcTighten();
                            else Thrash();
                        }
                    }
                }
                else _lonely[pair] = 0f;

                if (pair == 0) _progressA = p;
                else _progressB = p;
            }

            if (Cut(0) && Cut(1))
            {
                _freed = true;
                _biters.Clear();
                _testBiters.Clear();
                _biting = 0;
                if (MatchManager.Instance != null)
                {
                    MatchManager.Instance.ReduceRemaining(timeReward);
                    MatchManager.Instance.TaskCompleted(transform.position, "Turtle freed");
                }
                // Off it goes - up and away first, then roaming the reef like the others.
                if (_swimmer != null)
                    _swimmer.BeginSwimming(transform.position + transform.forward * 12f + Vector3.up * 5f, 0.4f);
                Debug.Log($"[TurtleTask] {name}: freed - {timeReward:0} s off the clock.");
            }
        }

        [ClientRpc]
        void RpcTighten() => Thrash();

        /// <summary>Testing: an extra (pretend) fish biting <paramref name="rope"/> (-1 stops it).</summary>
        public void DebugBite(int fakeFish, int rope)
        {
            if (!Authority) return;
            if (rope >= 0 && CanBite(null, rope)) _testBiters[fakeFish] = rope;
            else _testBiters.Remove(fakeFish);
        }

        /// <summary>Testing (Tools > Reef Run > Minigames): the first knot that's still tied, where a fish bites it.</summary>
        public bool DebugFirstKnot(out int rope, out Vector3 at)
        {
            rope = -1; at = default;
            if (!_built || _freed) return false;
            for (int i = 0; i < 4; i++)
                if (!RopeCut(i) && _ropes[i] != null) { rope = i; at = KnotPoint(i); return true; }
            return false;
        }

        /// <summary>Testing: open the net game at <paramref name="rope"/>, with a pretend fish biting its partner
        /// knot for as long as it's open (a pair has to be bitten together, so alone it would only slip).</summary>
        public void DebugOpenNet(FishPlayer me, int rope)
        {
            if (me == null || _net.open) return;
            OpenNet(me, rope);
            if (!_net.open) return;
            _debugPartner = true;
            DebugBite(-99, PartnerOf(rope));
        }
        bool _debugPartner;

        /// <summary>Testing: the word on a rope's knot.</summary>
        public string DebugWord(int rope) => WordOf(rope);

        /// <summary>Testing: the rope paired with <paramref name="rope"/>.</summary>
        public int DebugPartner(int rope) => PartnerOf(rope);

        // ================================================================= every machine: looks

        // Where a rope ties onto the net, and where its knot is.
        Vector3 CornerPoint(int rope) =>
            corners != null && rope < corners.Length && corners[rope] != null ? corners[rope].position : transform.position;

        Vector3 KnotPoint(int rope)
        {
            Vector3 corner = CornerPoint(rope);
            Vector3 anchor = _ropes[rope] != null ? _ropes[rope].anchor : AnchorFor(rope, corner);
            return Vector3.Lerp(corner, anchor, knotAlong);
        }

        // Out from the turtle past its corner, on the seabed.
        Vector3 AnchorFor(int rope, Vector3 corner)
        {
            Vector3 outward = corner - transform.position;
            outward.y = 0f;
            outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : Quaternion.Euler(0f, 90f * rope, 0f) * transform.forward;
            Vector3 p = transform.position + outward * ropeReach;
            p.y = WaterFx.SeabedHeight(p, out float ground) ? ground : transform.position.y - hoverHeight;
            return p;
        }

        // Joined after it was rescued: it's just a turtle swimming about (no ropes, no net).
        bool AlreadyFree()
        {
            if (net != null) net.gameObject.SetActive(false);
            _shownFreed = true;
            if (animator != null) animator.speed = 1f;
            return true;
        }

        // The rocks and ropes, once there's seabed to tie them to.
        bool BuildRopes()
        {
            if (!WaterFx.SeabedHeight(transform.position, out _)) return false;
            var rng = new System.Random((int)(transform.position.x * 73f) ^ (int)(transform.position.z * 131f));
            _leftovers = new GameObject($"{name} (net and ropes)").transform;
            _leftovers.position = transform.position;
            for (int i = 0; i < 4; i++)
            {
                var rope = new Rope();
                Vector3 corner = CornerPoint(i);
                rope.anchor = AnchorFor(i, corner);
                rope.outward = rope.anchor - transform.position;
                rope.outward.y = 0f;
                rope.outward.Normalize();
                rope.sway = (float)rng.NextDouble() * 10f;

                if (rocks != null && rocks.Length > 0 && rocks[i % rocks.Length] != null)
                {
                    var rock = Instantiate(rocks[rng.Next(rocks.Length)], rope.anchor,
                                           Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), _leftovers);
                    var r = rock.GetComponentInChildren<Renderer>();
                    float size = r != null ? Mathf.Max(r.bounds.size.x, r.bounds.size.z) : 1f;
                    rock.transform.localScale *= rockSize / Mathf.Max(0.05f, size);
                    // Sat in the sand, the rope tied on near its top.
                    if (r != null)
                    {
                        float sink = rock.transform.position.y - r.bounds.min.y;
                        rock.transform.position += Vector3.up * (sink - 0.25f * r.bounds.size.y);
                        rope.anchor.y = r.bounds.max.y - 0.1f;
                    }
                    rope.rock = rock.transform;
                }

                rope.upper = Segment($"Rope {i} (net)");
                rope.lower = Segment($"Rope {i} (rock)");
                rope.knot = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform;
                rope.knot.name = $"Knot {i}";
                Destroy(rope.knot.GetComponent<Collider>());
                rope.knot.SetParent(_leftovers, false);
                rope.knot.localScale = Vector3.one * 0.13f;
                rope.knot.GetComponent<Renderer>().sharedMaterial = knotMaterial;
                _ropes[i] = rope;
            }
            return true;
        }

        Transform Segment(string label)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = label;
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = ropeMaterial;
            go.transform.SetParent(_leftovers, false);
            return go.transform;
        }

        // A cylinder (2 long, along y) stretched from a to b.
        void Stretch(Transform seg, Vector3 a, Vector3 b, float thickness)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            seg.position = (a + b) * 0.5f;
            seg.rotation = len > 1e-4f ? Quaternion.FromToRotation(Vector3.up, d / len) : Quaternion.identity;
            Vector3 parent = seg.parent != null ? seg.parent.lossyScale : Vector3.one;
            seg.localScale = new Vector3(thickness / parent.x, len * 0.5f / parent.y, thickness / parent.z);
        }

        void Thrash() => _thrashUntil = Time.time + 0.9f;

        void UpdateLooks(float dt)
        {
            if (model == null) return;
            float t = Time.time;

            if (!_freed)
            {
                // Struggling against the net: a slow, straining swim, rocking - harder when it pulls tight.
                bool thrashing = t < _thrashUntil;
                if (animator != null) animator.speed = thrashing ? 1.7f : struggleSpeed;
                float shake = thrashing ? 1f : 0f;
                float pitch = 4f * Mathf.Sin(t * 1.3f) + shake * 7f * Mathf.Sin(t * 23f);
                float roll = 5f * Mathf.Sin(t * 0.9f + 1f) + shake * 9f * Mathf.Sin(t * 19f);
                model.localRotation = Quaternion.Euler(pitch, 0f, roll) * _modelRest;
                model.localPosition = _modelHome + Vector3.up * (0.08f * Mathf.Sin(t * 1.1f));
            }
            else FreeLooks(dt);

            if (!_built || _ropes[0] == null) return;
            for (int i = 0; i < 4; i++)
            {
                var rope = _ropes[i];
                bool cut = RopeCut(i);
                Vector3 corner = CornerPoint(i);
                Vector3 knot = Vector3.Lerp(corner, rope.anchor, knotAlong);
                if (!cut)
                {
                    // Taut, with a little sway; the knot thins as it's bitten through.
                    knot += Vector3.up * (0.03f * Mathf.Sin(t * 1.7f + rope.sway));
                    Stretch(rope.upper, corner, knot, ropeThickness);
                    Stretch(rope.lower, knot, rope.anchor, ropeThickness);
                    rope.knot.position = knot;
                    rope.knot.localScale = Vector3.one * Mathf.Lerp(0.13f, 0.06f, Progress(PairOf(i)));
                    if ((_biting & (1 << i)) != 0) BiteBubbles(knot, dt);
                }
                else
                {
                    // Bitten through: the net's end hangs off the net, the rock's end lies on the seabed.
                    Stretch(rope.upper, corner, corner + Vector3.down * 0.7f + rope.outward * (0.15f * Mathf.Sin(t + rope.sway)), ropeThickness);
                    Vector3 lie = rope.anchor - rope.outward * 0.9f;
                    lie.y = WaterFx.SeabedHeight(lie, out float g) ? g + 0.03f : rope.anchor.y;
                    Stretch(rope.lower, rope.anchor, lie, ropeThickness);
                    rope.knot.gameObject.SetActive(false);
                }
            }

            for (int pair = 0; pair < 2; pair++)
                if (Cut(pair) && !_shownCut[pair])
                {
                    _shownCut[pair] = true;
                    Thrash();
                    for (int k = 0; k < 2; k++)
                    {
                        int r = RopeOf(pair, k);
                        Vector3 at = Vector3.Lerp(CornerPoint(r), _ropes[r].anchor, knotAlong);
                        WaterFx.BubbleBurst(at, 14, 0.12f, 0.6f, new Vector2(0.015f, 0.04f));
                        SandTrails.Kick(_ropes[r].anchor, 0.8f);
                    }
                }
        }

        void BiteBubbles(Vector3 at, float dt)
        {
            _bubbleDebt += 14f * dt;
            while (_bubbleDebt >= 1f)
            {
                _bubbleDebt -= 1f;
                WaterFx.Bubble(at + Random.insideUnitSphere * 0.06f, Random.insideUnitSphere * 0.2f + Vector3.up * 0.2f,
                               Random.Range(0.012f, 0.03f), Random.Range(1f, 1.8f));
            }
        }

        // Free: the struggle eases out of its stroke, and the net slips off and settles on the seabed with the
        // cut ropes. Where it swims is up to its TurtleSwimmer (the server's; everyone else follows it).
        void FreeLooks(float dt)
        {
            if (!_shownFreed)
            {
                _shownFreed = true;
                if (net != null && _leftovers != null) net.SetParent(_leftovers, true);
                if (animator != null) animator.speed = 1f;
                WaterFx.BubbleBurst(model.position, 30, 0.4f, 1.2f, new Vector2(0.015f, 0.05f));
            }
            float k = 1f - Mathf.Exp(-3f * dt);
            model.localRotation = Quaternion.Slerp(model.localRotation, _modelRest, k);
            model.localPosition = Vector3.Lerp(model.localPosition, _modelHome, k);

            // The net sinks and lies on the seabed.
            if (net != null && net.parent == _leftovers)
            {
                float floor = WaterFx.SeabedHeight(net.position, out float g) ? g + 0.1f : net.position.y;
                if (net.position.y > floor) net.position += Vector3.down * (0.25f * dt);
            }
        }

        // ================================================================= every machine: this machine's fish

        // ================================================================= the net minigame (this machine's fish)

        // Strands of net go round the turtle; one is weak (orange). Space when it passes the fish's mouth
        // cuts it - and keeps the fish biting for BiteSeconds, long enough to reach the next weak strand.
        // A miss (or letting the bite run out) stops the bite, so pairs have to keep a rhythm together.
        struct NetGame
        {
            public bool open;
            public int rope, weak, cuts;
            public float rotation, biteUntil, flash, openedAt;
            public bool flashGood;
            public bool Biting => open && Time.time < biteUntil;
        }
        NetGame _net;
        NetRing _netRing;
        VisualElement _netPiece;
        Vector2 _ringAt;
        float _ringSize, _turtleRadius;
        Camera _netCam;   // the camera following our fish (the scene has no MainCamera tag)
        FishPlayer _netFish;
        static InputAction s_cutAction;

        int _netMisses;                    // this session at the net, for MatchLog
        const float NetSpin = 1.5f;        // rad/s
        const float BiteSeconds = 2.1f;    // one good cut keeps you biting this long
        const float BiteWindow = 0.3f;     // radians either side of the mouth

        void OpenNet(FishPlayer me, int rope)
        {
            var ui = MinigameHud.Open("NetBite");
            if (ui == null || me == null) return;
            _net = new NetGame { open = true, rope = rope, rotation = Random.value * 6.28f, openedAt = Time.unscaledTime };
            _netMisses = 0;
            MatchLog.Write("start", "Turtle net", -1f, $"rope={rope}");
            _netRing = ui.Q<NetRing>("ring");
            var key = ui.Q<Label>("cut-key");
            if (key != null) key.text = GameInput.UsingPad ? GameInput.PuzzleSubmit.PadLabel : "SPACE";
            var quit = ui.Q<Label>("quit-key");
            if (quit != null) quit.text = MinigameHud.KeyText(GameInput.Interact);
            var word = ui.Q<Label>("word");
            if (word != null) word.text = WordOf(rope);
            _netRing.window = BiteWindow;
            // the ring sits over the real turtle (FollowTurtle): no translate, we place it ourselves
            _netPiece = ui.Q(className: "mg-net");
            if (_netPiece != null) _netPiece.style.translate = new Translate(0, 0);
            _ringSize = -1f;
            _turtleRadius = 1f;
            var shell = model != null ? model : transform;
            foreach (var rend in shell.GetComponentsInChildren<Renderer>())
                _turtleRadius = Mathf.Max(_turtleRadius, Mathf.Max(rend.bounds.extents.x, rend.bounds.extents.z) * 0.8f);
            _net.weak = NextWeak(1.2f);
            _netFish = me;
            me.SetTaskLocked(true);
            MinigameHud.SetHint($"Someone has to chew the other {WordOf(rope)} knot at the same time");
            foreach (var cam in FindObjectsByType<FishOrbitCamera>(FindObjectsInactive.Exclude))
                if (cam.Target == me.transform)
                {
                    cam.TurnToward(TurtleCentre - me.transform.position, 24f);
                    _netCam = cam.GetComponentInChildren<Camera>();
                }
        }

        void CloseNet(FishPlayer me)
        {
            if (!_net.open) return;
            if (_debugPartner) { _debugPartner = false; DebugBite(-99, -1); }
            MatchLog.Write(_freed ? "solved" : "left", "Turtle net", Time.unscaledTime - _net.openedAt,
                $"cuts={_net.cuts} misses={_netMisses} pair={Progress(PairOf(_net.rope)):0.00}");
            _net.open = false;
            MinigameHud.Close();
            if (_netFish != null) _netFish.SetTaskLocked(false);
            _netFish = null;
        }

        Vector3 TurtleCentre => model != null ? model.position : transform.position;

        // Keep the ring wrapped round the real turtle on screen, just outside its shell.
        void FollowTurtle()
        {
            var cam = _netCam != null ? _netCam : Camera.main;
            if (_netPiece == null || _netPiece.panel == null || cam == null) return;
            Vector3 centre = TurtleCentre;
            if (Vector3.Dot(centre - cam.transform.position, cam.transform.forward) <= 0.5f) return;   // behind us
            var panel = _netPiece.panel;
            Vector2 c = RuntimePanelUtils.CameraTransformWorldToPanel(panel, centre, cam);
            Vector2 e = RuntimePanelUtils.CameraTransformWorldToPanel(panel, centre + cam.transform.right * _turtleRadius, cam);
            // NetRing draws its ring at 0.33 of its size; the net goes a little outside the shell
            float size = Mathf.Clamp((e - c).magnitude * 1.2f / 0.33f, 300f, 560f);

            float k = _ringSize < 0f ? 1f : 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime);
            _ringAt = _ringSize < 0f ? c : Vector2.Lerp(_ringAt, c, k);
            _ringSize = _ringSize < 0f ? size : Mathf.Lerp(_ringSize, size, k);
            _netRing.style.width = _ringSize;
            _netRing.style.height = _ringSize;

            // place the piece so the ring's middle lands on the turtle (below the clock, on screen)
            var ring = _netRing.layout;
            float left = _ringAt.x - (ring.x + ring.width * 0.5f);
            float top = _ringAt.y - (ring.y + ring.height * 0.5f);
            var root = panel.visualTree.layout;
            _netPiece.style.left = Mathf.Clamp(left, 0f, Mathf.Max(0f, root.width - _netPiece.layout.width));
            _netPiece.style.top = Mathf.Clamp(top, 110f, Mathf.Max(110f, root.height - _netPiece.layout.height - 20f));
        }

        // The strand that reaches the mouth in about `seconds`.
        int NextWeak(float seconds)
        {
            int n = _netRing != null ? _netRing.strands : 10, best = 0;
            float bestErr = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                float a = _net.rotation + i * Mathf.PI * 2f / n;
                // strands move with +rotation; time until strand i's angle reaches the top (-pi/2)
                float need = Mathf.Repeat(-Mathf.PI / 2f - a, Mathf.PI * 2f);
                float err = Mathf.Abs(need / NetSpin - seconds);
                if (err < bestErr) { bestErr = err; best = i; }
            }
            return best;
        }

        void TickNet()
        {
            if (_netRing == null) return;
            GameInput.ReportPuzzle();
            float dt = PauseMenu.IsOpen ? 0f : Mathf.Min(Time.deltaTime, 0.05f);
            _net.rotation += NetSpin * dt;
            _net.flash = Mathf.MoveTowards(_net.flash, 0f, dt * 2.5f);

            if (s_cutAction == null)
            {
                s_cutAction = new InputAction("net_cut", InputActionType.Button);
                s_cutAction.AddBinding("<Keyboard>/space");
                s_cutAction.AddBinding("<Gamepad>/buttonSouth");
                s_cutAction.Enable();
            }
            if (dt > 0f && (s_cutAction.WasPressedThisFrame() || GameInput.PuzzleSubmit.WasPressedThisFrame()))
            {
                float a = _net.rotation + _net.weak * Mathf.PI * 2f / _netRing.strands;   // this frame's angle
                float off = Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, -90f)) * Mathf.Deg2Rad;
                if (off <= BiteWindow)
                {
                    _net.cuts++;
                    _net.biteUntil = Time.time + BiteSeconds;
                    _net.flash = 1f; _net.flashGood = true;
                    _net.weak = NextWeak(Random.Range(1.0f, 1.6f));
                    MinigameHud.Flash(off < BiteWindow * 0.4f ? "SNIP!" : "NIBBLE", true);
                    GameFeel.Shake(0.05f);
                }
                else
                {
                    _net.biteUntil = 0f;
                    _netMisses++;
                    _net.flash = 1f; _net.flashGood = false;
                    MinigameHud.Flash("MISSED", false);
                }
            }

            _netRing.rotation = _net.rotation;
            _netRing.weak = _net.weak;
            _netRing.flash = _net.flash;
            _netRing.flashGood = _net.flashGood;
            _netRing.biting = _net.Biting ? Mathf.Clamp01((_net.biteUntil - Time.time) / BiteSeconds) : 0f;
            _netRing.MarkDirtyRepaint();
            _netRing.progress = Progress(PairOf(_net.rope));
            FollowTurtle();
        }

        static FishPlayer LocalFish()
        {
            if (NetworkClient.active)
                return NetworkClient.localPlayer != null ? NetworkClient.localPlayer.GetComponent<FishPlayer>() : null;
            foreach (var fp in FishPlayer.All)
                if (fp != null && fp.IsOfflineSandboxBody) return fp;
            return null;
        }

        int NearestKnot(Vector3 p)
        {
            int best = -1;
            float bestD = biteRange * biteRange;
            for (int i = 0; i < 4; i++)
            {
                if (RopeCut(i) || _ropes[i] == null) continue;
                float d = (KnotPoint(i) - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        void UpdateLocalFish()
        {
            var me = LocalFish();
            bool fish = me != null && !IsShark(me) && !Gone(me);
            int rope = fish && _built && !_freed ? NearestKnot(me.transform.position) : -1;
            if (rope >= 0 || _net.open) s_atRopeFrame = Time.frameCount;

            // E at a knot opens the net minigame; biting only happens while it says so (a strand cut on time).
            if (_net.open && (rope != _net.rope || me == null || GameInput.Interact.WasPressedThisFrame()
                              || (Time.unscaledTime - _net.openedAt > 0.3f && MinigameStation.PlayerWantsToMove())))
                CloseNet(me);
            else if (!_net.open && rope >= 0 && !PauseMenu.IsOpen && GameInput.Interact.WasPressedThisFrame())
                OpenNet(me, rope);
            if (_net.open) TickNet();
            bool holding = _net.open && rope == _net.rope && !PauseMenu.IsOpen && _net.Biting;

            int want = holding ? rope : -1;
            if (want != _sentRope && me != null)
            {
                _sentRope = want;
                if (Authority) ServerBite(me, want);
                else CmdBite(want);
            }

            if (fish) Hud(me, rope, holding);
        }

        void Hud(FishPlayer me, int rope, bool holding)
        {
            if (_freed || _net.open) return;   // the net minigame's panel says it all
            if (rope >= 0)
            {
                string word = WordOf(rope);
                bool together = (_biting & (1 << PartnerOf(rope))) != 0;
                string line = !_net.open
                    ? $"Knot: <b>{word}</b> — press {InputGlyphs.Name(GameInput.Interact)} to bite.  Bite it together with the other {word} knot!"
                    : !holding
                    ? $"Knot: <b>{word}</b> — cut the orange strand as it passes your mouth.  Together with the other {word} knot!"
                    : together
                        ? $"Biting the <b>{word}</b> knot together — keep going!"
                        : $"<color=#ff7a66>The net pulls tighter!</color>  The other <b>{word}</b> knot has to be bitten at the same time.";
                MatchHud.ShowStatus(line);
                MatchHud.ShowProgress($"{word} ropes", Progress(PairOf(rope)));
                return;
            }
            if ((me.transform.position - transform.position).sqrMagnitude < hintRange * hintRange)
                MatchHud.ShowStatus($"A turtle is caught in a net — bite its ropes through, two matching knots at once ({CutCount}/2 cut)");
        }
    }
}
