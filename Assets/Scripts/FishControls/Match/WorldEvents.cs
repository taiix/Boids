using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace FishGame
{
    /// <summary>
    /// Things that happen to the whole reef mid-round, one every couple of minutes while the survival clock runs
    /// (never the same as either of the last two). The reef's weather - nightfall, storm, cold layer, jellyfish,
    /// sand slide, red tide - is <see cref="ReefConditions"/>; this runs the scheduling and these three:
    ///   * <b>Strong current</b> - for <see cref="currentSeconds"/> the water pushes everyone one way
    ///     (<see cref="FishMotor.Flow"/>), fish and shark alike, with a stream of motes showing which way.
    ///   * <b>Trawler</b> - a boat drags a net from one wall to the one opposite. The net spans the arena wall to
    ///     wall and hangs from the surface down to <see cref="netDepthFraction"/> of the depth, so the way past it
    ///     is under it. Anyone it sweeps into - fish or shark - is hurt, carried along for a moment and dropped
    ///     behind it; school fish it catches are gone.
    ///   * <b>Orcas</b> - at most once a round, two killer whales come in from a wall and hunt the shark for
    ///     <see cref="orcaSeconds"/> (<see cref="OrcaHunter"/>), then swim off. The fish they leave alone.
    ///
    /// The host (or the offline sandbox) decides when and which (<see cref="MatchManager.ServerStartEvent"/>) and
    /// judges who the net catches; every machine plays the event from MatchManager's synced state, so the net is
    /// in the same place on every screen without sending it. The host presses <see cref="nextEventKey"/> (editor
    /// and development builds) to start the next event now, and any player Shift + <see cref="orcaKey"/> (any
    /// build, while <see cref="allowOrcaKey"/> is on) to call the orcas in now.
    /// </summary>
    public class WorldEvents : MonoBehaviour
    {
        static WorldEvents s_instance;

        /// <summary>Run world events in the current (game) scene, once.</summary>
        public static void Show()
        {
            if (s_instance != null) return;
            s_instance = new GameObject("WorldEvents").AddComponent<WorldEvents>();
        }

        [Header("When")]
        [Tooltip("Seconds into the round before the first event (a random value in this range).")]
        [SerializeField] Vector2 firstEventAfter = new Vector2(90f, 140f);
        [Tooltip("Seconds between one event ending and the next starting.")]
        [SerializeField] Vector2 eventGap = new Vector2(80f, 130f);
        [Tooltip("Editor / development builds: the host presses this to start the next event now.")]
        [SerializeField] Key nextEventKey = Key.F6;
        [Tooltip("Any player can press Shift + this to call the orcas in now (in any build); everyone sees them. " +
                 "Handy for testing - turn off for real matches.")]
        [SerializeField] Key orcaKey = Key.F6;
        [SerializeField] bool allowOrcaKey = true;

        [Header("Strong current")]
        [SerializeField] float currentSeconds = 40f;
        [Tooltip("How hard the current pushes (m/s) at full strength.")]
        [SerializeField] float currentStrength = 3.5f;
        [Tooltip("Seconds it takes to build up and to die down.")]
        [SerializeField] float currentRamp = 4f;

        [Header("Trawler")]
        [Tooltip("Seconds of warning between the announcement and the net starting to move.")]
        [SerializeField] float trawlerWarning = 8f;
        [Tooltip("How fast the net sweeps across (m/s).")]
        [SerializeField] float netSpeed = 4f;
        [Tooltip("How far down the net hangs, as a fraction of the water's depth in the middle of the arena.")]
        [Range(0.2f, 0.9f)][SerializeField] float netDepthFraction = 0.6f;
        [Tooltip("Size of a mesh square in the net (metres).")]
        [SerializeField] float netCell = 1.8f;
        [SerializeField] float netDamageFish = 25f;
        [SerializeField] float netDamageShark = 15f;
        [Tooltip("Seconds someone the net catches is carried along before it drops them behind it.")]
        [SerializeField] float entangleSeconds = 3f;
        [SerializeField] float boatScale = 1.6f;

        [Header("Orcas")]
        [Tooltip("Seconds of warning between the announcement and the orcas arriving.")]
        [SerializeField] float orcaWarning = 6f;
        [Tooltip("How long they hunt the shark (seconds).")]
        [SerializeField] float orcaSeconds = 90f;
        [Tooltip("How many come.")]
        [Min(1)][SerializeField] int orcaCount = 2;

        // ---- authority ----
        double _nextAt = -1;
        WorldEventKind _lastKind, _beforeLast;
        readonly List<WorldEventKind> _choices = new List<WorldEventKind>();
        readonly Dictionary<FishPlayer, double> _netImmune = new Dictionary<FishPlayer, double>();
        readonly List<OrcaHunter> _orcas = new List<OrcaHunter>();
        bool _orcasThisRound;
        double _roundSeen = double.NaN;

        // ---- every machine ----
        WorldEventKind _shownKind;
        double _shownStart;
        GameObject _trawl;            // the boat + net, while the trawler is on
        Transform _boat;
        TrawlLayout _layout;
        float _nextMote;

        struct TrawlLayout
        {
            public bool valid;
            public Vector3 start;      // middle of the net's top edge when it starts, at the surface
            public Vector3 travel;     // flat, the way it sweeps
            public Vector3 along;      // flat, across the net
            public float width, distance, top, bottom;
            public string fromName;
        }

        static bool Authority => MatchManager.Offline || NetworkServer.active;

        void Awake()
        {
            // Clients spawn the orcas the server spawns, so they need to know the prefab.
            var lib = WaterFxLibrary.Get();
            var prefab = lib != null ? lib.orcaPrefab : null;
            if (prefab != null && prefab.TryGetComponent(out NetworkIdentity id)
                && !(NetworkClient.prefabs.TryGetValue(id.assetId, out var known) && known == prefab))
                NetworkClient.RegisterPrefab(prefab);
        }

        void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
            FishMotor.Flow = Vector3.zero;
        }

        void Update()
        {
            var m = MatchManager.Instance;
            if (m == null) { FishMotor.Flow = Vector3.zero; return; }

            if (allowOrcaKey && Keyboard.current != null && ShiftHeld && Keyboard.current[orcaKey].wasPressedThisFrame)
                RequestOrcas(m);
            if (Authority) Schedule(m);

            // A new event was announced: tell this player, and set the trawler up.
            if (m.Event != _shownKind || m.EventStart != _shownStart)
            {
                _shownKind = m.Event;
                _shownStart = m.EventStart;
                Announce(m);
            }

            double now = MatchManager.Now;
            bool over = m.IsOver || m.Phase == MatchPhase.Warmup;
            bool on = !over && m.Event != WorldEventKind.None && now < m.EventEnd;

            UpdateCurrent(m, on && m.Event == WorldEventKind.Current, now);
            UpdateTrawler(m, on && m.Event == WorldEventKind.Trawler, now);
            UpdateOrcas(m, !over && m.Event == WorldEventKind.Orcas && now < m.EventEnd, now);
        }

        // ------------------------------------------------------------------ deciding (authority)

        void Schedule(MatchManager m)
        {
            if (m.Phase != MatchPhase.Playing) return;
            double now = MatchManager.Now;
            if (m.RoundStartTime != _roundSeen) { _roundSeen = m.RoundStartTime; _orcasThisRound = false; }
            if (_nextAt < 0) _nextAt = m.RoundStartTime + Random.Range(firstEventAfter.x, firstEventAfter.y);

            bool skip = Debug.isDebugBuild && Keyboard.current != null && !ShiftHeld && Keyboard.current[nextEventKey].wasPressedThisFrame;
            bool busy = m.Event != WorldEventKind.None && now < m.EventEnd;
            if (busy || (now < _nextAt && !skip)) return;

            // Something different from the last two; the trawler needs the arena's walls to know where to go.
            var walls = ArenaWalls.Instance;
            _choices.Clear();
            for (var k = WorldEventKind.Current; k <= WorldEventKind.Orcas; k++)
            {
                if (k == _lastKind || k == _beforeLast) continue;
                if (k == WorldEventKind.Trawler && (walls == null || walls.Count < 2)) continue;
                // The orcas: once a round at most, and only with a shark to hunt and orcas to send.
                if (k == WorldEventKind.Orcas && (_orcasThisRound || Shark() == null || OrcaPrefab() == null)) continue;
                _choices.Add(k);
            }
            var kind = _force != WorldEventKind.None ? _force : _choices[Random.Range(0, _choices.Count)];
            _force = WorldEventKind.None;

            if (ReefConditions.Handles(kind))
            {
                // The reef's weather: a drift direction and a seed lay it out the same on every machine.
                float a = Random.Range(0f, Mathf.PI * 2f);
                m.ServerStartEvent(kind, now, now + ReefConditions.Duration(kind), new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)),
                                   Random.Range(1, int.MaxValue));
            }
            else if (kind == WorldEventKind.Orcas)
            {
                // They come in from one of the walls (or, without walls, from far off).
                int wall = walls != null && walls.Count > 0 ? Random.Range(0, walls.Count) : -1;
                float a = Random.Range(0f, Mathf.PI * 2f);
                double start = now + orcaWarning;
                m.ServerStartEvent(kind, start, start + orcaSeconds, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), wall);
                _orcasThisRound = true;
            }
            else if (kind == WorldEventKind.Current)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                m.ServerStartEvent(kind, now, now + currentSeconds, dir, 0);
            }
            else
            {
                int wall = Random.Range(0, walls.Count);
                var layout = Layout(wall);
                if (!layout.valid) return;
                double start = now + trawlerWarning;
                m.ServerStartEvent(kind, start, start + layout.distance / Mathf.Max(0.5f, netSpeed), layout.travel, wall);
            }
            _beforeLast = _lastKind;
            _lastKind = kind;
            _netImmune.Clear();
            _nextAt = m.EventEnd + Random.Range(eventGap.x, eventGap.y);
        }

        static bool ShiftHeld => Keyboard.current != null && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);

        // Shift + F6 on any machine: the orcas now - started here if we decide, else asked of the host.
        void RequestOrcas(MatchManager m)
        {
            string why = WhyNoOrcas(m);
            if (why != null) { MatchHud.Flash(why, 2f); return; }
            if (Authority) ServerSummonOrcas();
            else
            {
                var me = LocalBody();
                if (me != null) me.CmdSummonOrcas();
            }
        }

        static string WhyNoOrcas(MatchManager m)
        {
            if (m.Phase != MatchPhase.Playing) return "The orcas can only come during the round";
            if (m.Event == WorldEventKind.Orcas && MatchManager.Now < m.EventEnd) return "The orcas are already here";
            if (Shark() == null) return "There's no shark for the orcas to hunt";
            if (OrcaPrefab() == null) return "No orca prefab (FishGame > Build Orca Assets)";
            return null;
        }

        /// <summary>Server / offline: a player pressed Shift + F6 - the orcas come now, ending whatever's on
        /// (if the key's allowed here and they can come).</summary>
        public static void ServerSummonOrcas()
        {
            var m = MatchManager.Instance;
            if (s_instance == null || m == null || !Authority || !s_instance.allowOrcaKey || WhyNoOrcas(m) != null) return;
            DevStart(WorldEventKind.Orcas);
        }

        /// <summary>Testing (host / offline): start <paramref name="kind"/> right now, ending whatever is on.</summary>
        public static void DevStart(WorldEventKind kind)
        {
            var m = MatchManager.Instance;
            if (s_instance == null || m == null || !Authority) return;
            double now = MatchManager.Now;
            m.ServerStartEvent(WorldEventKind.None, now, now, Vector3.zero, 0);
            s_instance._nextAt = 0;
            s_instance._lastKind = s_instance._beforeLast = WorldEventKind.None;
            s_instance._force = kind;
        }

        WorldEventKind _force;

        void Announce(MatchManager m)
        {
            // The reef's weather chimes for itself (ReefConditions); these two are ours.
            if ((m.Event == WorldEventKind.Current || m.Event == WorldEventKind.Trawler) && GameAudio.Lib != null)
                GameAudio.Play(GameAudio.Lib.uiAnnounce, 0.8f);
            if (m.Event == WorldEventKind.Current)
                MatchHud.Flash($"STRONG CURRENT - the water is pushing {Compass(m.EventDirection)}", 4f);
            else if (m.Event == WorldEventKind.Trawler)
            {
                _layout = Layout(m.EventParam);
                if (_layout.valid)
                    MatchHud.Flash($"A TRAWLER IS COMING FROM THE {_layout.fromName.ToUpperInvariant()} - dive under its net!", 4.5f);
            }
            else if (m.Event == WorldEventKind.Orcas)
            {
                if (GameAudio.Lib != null) GameAudio.Play(GameAudio.Lib.uiWarning, 0.9f);
                var me = LocalBody();
                MatchHud.Flash(me != null && me.IsSharkBody
                    ? "ORCAS ARE COMING FOR YOU - GET AWAY FROM THEM!"
                    : "A PAIR OF ORCAS IS HUNTING THE SHARK", 4.5f);
            }
        }

        static FishPlayer LocalBody()
        {
            if (NetworkClient.active)
                return NetworkClient.localPlayer != null ? NetworkClient.localPlayer.GetComponent<FishPlayer>() : null;
            foreach (var fp in FishPlayer.All)
                if (fp != null && fp.IsOfflineSandboxBody) return fp;
            return null;
        }

        static FishPlayer Shark()
        {
            foreach (var fp in FishPlayer.All)
                if (fp != null && fp.isActiveAndEnabled && fp.IsSharkBody && !fp.IsOut) return fp;
            return null;
        }

        static GameObject OrcaPrefab()
        {
            var lib = WaterFxLibrary.Get();
            return lib != null ? lib.orcaPrefab : null;
        }

        static string Compass(Vector3 dir)
        {
            float ang = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg; // 0 = north (+z), 90 = east (+x)
            string[] names = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
            return names[Mathf.RoundToInt(Mathf.Repeat(ang, 360f) / 45f) % 8];
        }

        // ------------------------------------------------------------------ strong current

        void UpdateCurrent(MatchManager m, bool on, double now)
        {
            if (!on)
            {
                FishMotor.Flow = Vector3.zero;
                return;
            }
            float t = (float)(now - m.EventStart), left = (float)(m.EventEnd - now);
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Min(t, left) / Mathf.Max(0.1f, currentRamp));
            FishMotor.Flow = m.EventDirection.normalized * (currentStrength * k);
            if (GameAudio.Lib != null) GameAudio.Loop("current", GameAudio.Lib.currentLoop, 0.55f * k);
            MatchHud.ShowEvent($"STRONG CURRENT  -  PUSHING {Compass(m.EventDirection).ToUpperInvariant()}  {Mathf.CeilToInt(left)}s");

            // Motes streaming past the camera, so the current can be seen.
            var cam = WaterFx.ViewCamera;
            if (cam == null || k < 0.05f) return;
            _nextMote -= Time.deltaTime * 110f * k;
            while (_nextMote <= 0f)
            {
                _nextMote += 1f;
                Vector3 p = cam.transform.position + cam.transform.forward * 6f + Random.insideUnitSphere * 11f;
                if (p.y > WaterFx.SurfaceY - 0.3f) continue;
                WaterFx.Bubble(p, FishMotor.Flow * 1.6f + Random.insideUnitSphere * 0.25f, Random.Range(0.05f, 0.11f), Random.Range(2f, 3f));
            }
        }

        // ------------------------------------------------------------------ orcas

        void UpdateOrcas(MatchManager m, bool on, double now)
        {
            if (Authority)
            {
                _orcas.RemoveAll(o => o == null);
                bool hunting = on && now >= m.EventStart;
                if (hunting && _orcas.Count == 0 && !_orcasSent) SpawnOrcas(m);
                if (!on && _orcas.Count > 0)
                {
                    // They see themselves off; a new hunt brings new ones.
                    foreach (var orca in _orcas) orca.ServerLeave(ExitPoint(orca.transform.position));
                    _orcas.Clear();
                }
                if (!on) _orcasSent = false;
            }
            if (!on) return;

            float wait = (float)(m.EventStart - now), left = (float)(m.EventEnd - now);
            var me = LocalBody();
            bool shark = me != null && me.IsSharkBody;
            if (wait > 0f) MatchHud.ShowEvent(shark ? $"ORCAS COMING FOR YOU IN {Mathf.CeilToInt(wait)}s" : $"ORCAS IN {Mathf.CeilToInt(wait)}s");
            else MatchHud.ShowEvent(shark ? $"ORCAS HUNTING YOU  -  {Clock(left)}" : $"ORCAS HUNTING THE SHARK  -  {Clock(left)}");
        }

        bool _orcasSent;

        static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{s / 60}:{s % 60:00}";
        }

        // Authority: the orcas, in from the announced wall (just inside it, at the shark's depth), side by side.
        void SpawnOrcas(MatchManager m)
        {
            var prefab = OrcaPrefab();
            var shark = Shark();
            if (prefab == null || shark == null) return;
            _orcasSent = true;

            Vector3 target = shark.transform.position;
            Vector3 at, inward, along;
            var walls = ArenaWalls.Instance;
            if (walls != null && walls.Describe(m.EventParam, out var home, out var outward, out var side, out float half, out float gapMin, out float gapMax))
            {
                inward = -outward;
                along = side;
                at = home + inward * (half + 12f) + side * ((gapMin + gapMax) * 0.5f);
            }
            else
            {
                inward = -m.EventDirection.normalized;
                along = Vector3.Cross(Vector3.up, inward);
                at = target - inward * 70f;
            }
            float surface = WaterFx.SurfaceY;
            for (int i = 0; i < orcaCount; i++)
            {
                Vector3 p = at + along * ((i - (orcaCount - 1) * 0.5f) * 14f);
                float ground = WaterFx.SeabedHeight(p, out float g) ? g : surface - 40f;
                p.y = Mathf.Clamp(target.y, ground + 5f, surface - 5f);
                var go = Instantiate(prefab, p, Quaternion.LookRotation(Vector3.ProjectOnPlane(target - p, Vector3.up).normalized + inward * 0.01f));
                if (!go.TryGetComponent(out OrcaHunter orca)) { Destroy(go); continue; }
                orca.ServerBegin(i % 2, ExitPoint(p));
                if (NetworkServer.active) NetworkServer.Spawn(go);
                _orcas.Add(orca);
            }
            Debug.Log($"[WorldEvents] {_orcas.Count} orcas hunting the shark.");
        }

        // Out past the nearest wall (or just far off), to swim away to.
        static Vector3 ExitPoint(Vector3 from)
        {
            var walls = ArenaWalls.Instance;
            Vector3 best = from + (from - Vector3.zero).normalized * 80f;
            float bestD = float.MaxValue;
            if (walls != null)
                for (int i = 0; i < walls.Count; i++)
                {
                    if (!walls.Describe(i, out var home, out var outward, out _, out float half, out _, out _)) continue;
                    float d = Mathf.Abs(Vector3.Dot(from - home, outward));
                    if (d < bestD)
                    {
                        bestD = d;
                        best = from + outward * (d + half + 40f);
                        best.y = from.y;
                    }
                }
            return best;
        }

        // ------------------------------------------------------------------ trawler

        // Where the net runs for a trawler starting at `wall`: from just outside it to past the wall opposite,
        // spanning the open stretch between the side walls.
        TrawlLayout Layout(int wall)
        {
            var l = new TrawlLayout();
            var walls = ArenaWalls.Instance;
            if (walls == null) return l;
            if (!walls.Describe(wall, out var home, out var outward, out var along, out float half, out float gapMin, out float gapMax)) return l;
            int opp = walls.Opposite(wall);
            if (!walls.Describe(opp, out var oppHome, out _, out _, out _, out _, out _)) return l;

            l.travel = -outward;
            l.along = along;
            l.width = gapMax - gapMin + 1f;
            l.start = home + outward * (half + 4f) + along * ((gapMin + gapMax) * 0.5f);
            l.distance = Vector3.Dot(home - oppHome, outward) + half * 2f + 8f;
            float surface = WaterFx.SurfaceY;
            l.start.y = surface;
            Vector3 middle = l.start + l.travel * (l.distance * 0.5f);
            float seabed = WaterFx.SeabedHeight(middle, out float y) ? y : surface - 40f;
            l.top = surface + 0.4f;
            l.bottom = surface - Mathf.Max(6f, (surface - seabed) * netDepthFraction);
            l.fromName = walls.NameOf(wall);
            l.valid = l.distance > 1f && l.width > 1f;
            return l;
        }

        void UpdateTrawler(MatchManager m, bool on, double now)
        {
            if (!on || !_layout.valid)
            {
                if (_trawl != null) Destroy(_trawl);
                _trawl = null;
                return;
            }
            if (_trawl == null) BuildTrawl();

            // Where the net is now: waiting outside its wall until it starts, then sweeping across.
            float span = (float)(m.EventEnd - m.EventStart);
            float speed = span > 0f ? _layout.distance / span : 0f;
            float travelled = Mathf.Clamp((float)(now - m.EventStart), 0f, span) * speed;
            Vector3 netAt = _layout.start + _layout.travel * travelled;
            _trawl.transform.SetPositionAndRotation(netAt, Quaternion.LookRotation(_layout.travel, Vector3.up));
            if (_boat != null)
            {
                Vector3 b = _boat.position;
                b.y = WaterFx.WaterHeightAt(b);
                _boat.position = b;
            }

            // The trawler's engine, from the boat (heard from well across the arena).
            if (GameAudio.Lib != null) GameAudio.Loop("trawler", GameAudio.Lib.trawlerEngine, 1f, _boat != null ? _boat : _trawl.transform, 140f);

            float wait = (float)(m.EventStart - now);
            MatchHud.ShowEvent(wait > 0f ? $"TRAWLER FROM THE {_layout.fromName.ToUpperInvariant()} IN {Mathf.CeilToInt(wait)}s"
                                         : "TRAWLER NET  -  DIVE UNDER IT");

            if (Authority && wait <= 0f) Sweep(netAt, _layout.travel * speed, now);
        }

        // Authority: who the net is passing through right now.
        void Sweep(Vector3 netAt, Vector3 netVelocity, double now)
        {
            foreach (var fp in FishPlayer.All)
            {
                if (fp == null || !fp.isActiveAndEnabled || fp.IsOut || fp.IsBeingEaten) continue;
                if (!MatchManager.Offline && fp.netId == 0) continue; // only real, spawned bodies
                if (_netImmune.TryGetValue(fp, out double until) && now < until) continue;
                if (!InNet(fp.transform.position, netAt, 1.5f)) continue;
                _netImmune[fp] = now + entangleSeconds + 2.5f;
                fp.ServerNetCaught(fp.IsSharkBody ? netDamageShark : netDamageFish, entangleSeconds, netVelocity);
            }

            // School fish in its path are caught for good.
            for (int b = 0; b < BoidsManager.All.Count; b++)
            {
                var flock = BoidsManager.All[b];
                if (flock == null || flock.allFish == null) continue;
                for (int id = 0; id < flock.allFish.Length; id++)
                {
                    var fish = flock.allFish[id];
                    if (fish == null || !InNet(fish.transform.position, netAt, 0.8f)) continue;
                    flock.DespawnFish(id);
                    if (NetworkServer.active) FlockNetSync.ServerAnnounceEaten(flock.NetId, (ushort)id, 0);
                }
            }
        }

        bool InNet(Vector3 p, Vector3 netAt, float reach)
        {
            Vector3 rel = p - netAt;
            return Mathf.Abs(Vector3.Dot(rel, _layout.travel)) < reach &&
                   Mathf.Abs(Vector3.Dot(rel, _layout.along)) < _layout.width * 0.5f + 0.5f &&
                   p.y > _layout.bottom - 0.5f && p.y < _layout.top + 1.5f;
        }

        // ------------------------------------------------------------------ the boat and its net

        void BuildTrawl()
        {
            _trawl = new GameObject("Trawler");
            _trawl.transform.SetParent(transform, false);
            var lib = WaterFxLibrary.Get();

            // Net in local space: x across, y up from the surface, z along the travel (the boat leads, at +z).
            float surface = _layout.start.y;
            float top = _layout.top - surface, bottom = _layout.bottom - surface, half = _layout.width * 0.5f;
            const float boatAhead = 22f;
            var mesh = new NetMesh();
            int columns = Mathf.Max(2, Mathf.CeilToInt(_layout.width / netCell));
            int rows = Mathf.Max(2, Mathf.CeilToInt((top - bottom) / netCell));
            for (int i = 0; i <= columns; i++)
            {
                float x = -half + _layout.width * i / columns;
                mesh.Beam(new Vector3(x, top, 0f), new Vector3(x, bottom, 0f), 0.05f);
            }
            for (int j = 0; j <= rows; j++)
            {
                float y = Mathf.Lerp(bottom, top, j / (float)rows);
                bool edge = j == 0 || j == rows; // the head rope and the weighted foot rope are thicker
                mesh.Beam(new Vector3(-half, y, 0f), new Vector3(half, y, 0f), edge ? 0.16f : 0.05f);
            }
            // Floats along the head rope, and the two tow lines up to the boat's stern.
            for (float x = -half; x <= half; x += 4f) mesh.Beam(new Vector3(x, top - 0.25f, 0f), new Vector3(x, top + 0.25f, 0f), 0.35f);
            mesh.Beam(new Vector3(-half, top, 0f), new Vector3(-1f, 0.6f, boatAhead - 4f), 0.08f);
            mesh.Beam(new Vector3(half, top, 0f), new Vector3(1f, 0.6f, boatAhead - 4f), 0.08f);

            var net = new GameObject("Net");
            net.transform.SetParent(_trawl.transform, false);
            net.AddComponent<MeshFilter>().sharedMesh = mesh.Build();
            var r = net.AddComponent<MeshRenderer>();
            r.sharedMaterial = lib != null && lib.trawlNet != null ? lib.trawlNet : null;
            r.shadowCastingMode = ShadowCastingMode.Off;

            if (lib != null && lib.trawlerBoat != null)
            {
                var boat = Instantiate(lib.trawlerBoat, _trawl.transform);
                // The sample's float script needs a water reference it doesn't have here; we float it ourselves.
                foreach (var mb in boat.GetComponentsInChildren<MonoBehaviour>(true))
                    if (mb != null && mb.GetType().Name == "FitToWaterSurface") Destroy(mb);
                boat.transform.localPosition = new Vector3(0f, 0f, boatAhead);
                boat.transform.localRotation = Quaternion.identity;
                boat.transform.localScale *= boatScale;
                _boat = boat.transform;
            }
        }

        // Thin square beams (ropes) gathered into one mesh.
        class NetMesh
        {
            readonly List<Vector3> _v = new List<Vector3>();
            readonly List<Vector3> _n = new List<Vector3>();
            readonly List<int> _t = new List<int>();

            public void Beam(Vector3 a, Vector3 b, float thickness)
            {
                Vector3 dir = (b - a).normalized;
                Vector3 side = Vector3.Cross(dir, Mathf.Abs(dir.y) > 0.9f ? Vector3.forward : Vector3.up).normalized;
                Vector3 up = Vector3.Cross(side, dir).normalized;
                float h = thickness * 0.5f;
                // Four faces, each with its own vertices so the rope shades as a square.
                Vector3[] offs = { side, up, -side, -up };
                for (int f = 0; f < 4; f++)
                {
                    Vector3 e0 = (offs[f] + offs[(f + 1) % 4]) * h, e1 = (offs[f] + offs[(f + 3) % 4]) * h;
                    int at = _v.Count;
                    _v.Add(a + e1); _v.Add(a + e0); _v.Add(b + e0); _v.Add(b + e1);
                    for (int k = 0; k < 4; k++) _n.Add(offs[f]);
                    // Both windings: ropes this thin should never vanish for facing the wrong way.
                    _t.Add(at); _t.Add(at + 2); _t.Add(at + 1);
                    _t.Add(at); _t.Add(at + 3); _t.Add(at + 2);
                    _t.Add(at); _t.Add(at + 1); _t.Add(at + 2);
                    _t.Add(at); _t.Add(at + 2); _t.Add(at + 3);
                }
            }

            public Mesh Build()
            {
                var m = new Mesh { name = "TrawlNet" };
                if (_v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
                m.SetVertices(_v);
                m.SetNormals(_n);
                m.SetTriangles(_t, 0);
                m.RecalculateBounds();
                return m;
            }
        }
    }
}
