using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace FishGame
{
    /// <summary>Host -> clients: corrected states for a batch of fish.</summary>
    public struct FlockStateMsg : NetworkMessage
    {
        public byte flock;
        public double time;             // host NetworkTime when sampled
        public ArraySegment<byte> data; // packed fish, see FlockNetSync.EntrySize
    }

    /// <summary>Host -> clients: a fish was eaten by the shark with netId <see cref="eater"/>.</summary>
    public struct FlockEatenMsg : NetworkMessage
    {
        public byte flock;
        public ushort fish;
        public uint eater;
    }

    /// <summary>Host -> a joining client: which fish are still alive (one bit per fish id).</summary>
    public struct FlockAliveMsg : NetworkMessage
    {
        public byte flock;
        public ArraySegment<byte> mask;
    }

    /// <summary>
    /// Keeps every player's ambient schools (<see cref="BoidsManager"/>) identical, cheaply.
    ///
    /// Every player simulates the flock locally - smooth, no network delay - from the same seed and
    /// the same clock-driven goal, and the host streams small corrections that everyone eases toward:
    /// 50 fish per batch (11 bytes each) shared by every school in the scene, 10 batches a second, on
    /// the unreliable channel - about 6 KB/s per player. Fish near a player (prey hiding in the school, the hunting shark)
    /// are picked ~10x as often as distant ones, since that's where the schools must match. A player
    /// who joins gets the whole state once, reliably.
    ///
    /// Eating is host-decided: the biting shark devours the fish locally at once, the host checks and
    /// removes it, and every other player sees it pulled into that shark's jaws.
    ///
    /// Plain Mirror messages, no NetworkIdentity, so the offline sandbox is unaffected. Created
    /// automatically - there's nothing to place in a scene.
    /// </summary>
    [DefaultExecutionOrder(100)] // after BoidsManager (-50) has stepped, so we send this frame's state
    public class FlockNetSync : MonoBehaviour
    {
        // ---- tuning ----
        const float SendRate = 10f;          // correction batches per second
        const int FishPerTick = 50;          // shared by every school in the scene: ~6 KB/s per player
        const float NearPlayerRadius = 25f;  // fish this close to any player...
        const float NearPlayerBoost = 10f;   // ...are corrected ~10x as often
        const int FullSyncChunk = 100;       // fish per reliable message when someone joins
        const float MaxEatDistance = 8f;     // host sanity check on a client's bite (metres)
        const float PositionMargin = 30f;    // quantisation box = flock area + this on each side
        const float MaxPackedSpeed = 20f;    // speed range packed into one byte
        const int EntrySize = 11;            // id(2) + position(6) + heading(2) + speed(1)

        static FlockNetSync s_instance;
        static readonly Predicate<int> s_notReadyRemote = NotReadyRemote;

        readonly Dictionary<BoidsManager, float[]> _priority = new Dictionary<BoidsManager, float[]>();
        readonly HashSet<int> _fullySynced = new HashSet<int>();
        readonly List<NetworkConnectionToClient> _remotes = new List<NetworkConnectionToClient>();
        readonly List<Vector3> _players = new List<Vector3>();
        readonly NetworkWriter _writer = new NetworkWriter();
        // Candidates across all schools for this tick: priority key -> (school, fish id).
        float[] _sortKeys = Array.Empty<float>();
        int[] _sortIdx = Array.Empty<int>();
        BoidsManager[] _candMgr = Array.Empty<BoidsManager>();
        int[] _candId = Array.Empty<int>();
        float _sendTimer;
        bool _clientHooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            // Every player derives the school's roaming goal from this clock, so it must be shared.
            BoidsManager.SharedClock = () =>
                NetworkServer.active || NetworkClient.active ? NetworkTime.time : Time.timeAsDouble;

            if (s_instance != null) return;
            var go = new GameObject(nameof(FlockNetSync));
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<FlockNetSync>();
        }

        void LateUpdate()
        {
            // Clients: register handlers at the start of every client session. Mirror clears them on
            // shutdown, and a message with no handler disconnects the client.
            if (NetworkClient.active)
            {
                if (!_clientHooked)
                {
                    NetworkClient.ReplaceHandler<FlockStateMsg>(OnState);
                    NetworkClient.ReplaceHandler<FlockEatenMsg>(OnEaten);
                    NetworkClient.ReplaceHandler<FlockAliveMsg>(OnAlive);
                    _clientHooked = true;
                }
            }
            else _clientHooked = false;

            if (!NetworkServer.active)
            {
                _fullySynced.Clear();
                _priority.Clear();
                return;
            }

            _sendTimer += Time.unscaledDeltaTime;
            if (_sendTimer < 1f / SendRate) return;
            _sendTimer = 0f;

            GatherRemotes();
            if (_remotes.Count == 0) return; // solo host: nobody to correct
            GatherPlayers();

            // Someone who just became ready gets the whole state once - as soon as our schools exist
            // (a fast client can be ready in the game scene before the host has finished loading it).
            if (BoidsManager.All.Count > 0)
                foreach (var conn in _remotes)
                    if (_fullySynced.Add(conn.connectionId))
                        foreach (var mgr in BoidsManager.All)
                            if (mgr != null) SendFullState(mgr, conn);

            SendCorrections();
        }

        // ================================================================= host
        void GatherRemotes()
        {
            _remotes.Clear();
            foreach (var conn in NetworkServer.connections.Values)
                if (conn != null && conn != NetworkServer.localConnection && conn.isReady)
                    _remotes.Add(conn);

            // Forget anyone who left or is loading a scene, so they get a fresh full state once ready.
            _fullySynced.RemoveWhere(s_notReadyRemote);
        }

        static bool NotReadyRemote(int connectionId) =>
            !NetworkServer.connections.TryGetValue(connectionId, out var c) || c == null || !c.isReady;

        void GatherPlayers()
        {
            _players.Clear();
            foreach (var conn in NetworkServer.connections.Values)
                if (conn != null && conn.identity != null)
                    _players.Add(conn.identity.transform.position);
        }

        bool NearAnyPlayer(Vector3 p)
        {
            const float r2 = NearPlayerRadius * NearPlayerRadius;
            for (int i = 0; i < _players.Count; i++)
                if ((_players[i] - p).sqrMagnitude < r2) return true;
            return false;
        }

        // The most "overdue" fish across every school, weighted toward those near players, go out this
        // tick - one message per school that has fish in the batch.
        void SendCorrections()
        {
            if (_priority.Count > BoidsManager.All.Count) PrunePriorities();

            int total = 0;
            foreach (var mgr in BoidsManager.All) if (mgr != null) total += mgr.FishCapacity;
            if (total == 0) return;
            if (_sortKeys.Length < total)
            {
                _sortKeys = new float[total];
                _sortIdx = new int[total];
                _candMgr = new BoidsManager[total];
                _candId = new int[total];
            }

            int live = 0;
            foreach (var mgr in BoidsManager.All)
            {
                if (mgr == null) continue;
                int cap = mgr.FishCapacity;
                if (!_priority.TryGetValue(mgr, out var pr) || pr.Length != cap)
                    _priority[mgr] = pr = new float[cap];
                for (int id = 0; id < cap; id++)
                {
                    if (!mgr.TryGetState(id, out var p, out _, out _)) continue;
                    pr[id] += NearAnyPlayer(p) ? NearPlayerBoost : 1f;
                    _sortKeys[live] = -pr[id]; // ascending sort -> highest priority first
                    _sortIdx[live] = live;
                    _candMgr[live] = mgr;
                    _candId[live] = id;
                    live++;
                }
            }
            if (live == 0) return;
            Array.Sort(_sortKeys, _sortIdx, 0, live);
            int n = Mathf.Min(FishPerTick, live);

            double now = NetworkTime.time;
            foreach (var mgr in BoidsManager.All)
            {
                if (mgr == null || !_priority.TryGetValue(mgr, out var pr)) continue;
                Box(mgr, out var min, out var size);
                _writer.Reset();
                bool any = false;
                for (int i = 0; i < n; i++)
                {
                    int c = _sortIdx[i];
                    if (_candMgr[c] != mgr) continue;
                    WriteEntry(mgr, _candId[c], min, size);
                    pr[_candId[c]] = 0f;
                    any = true;
                }
                if (!any) continue;
                var msg = new FlockStateMsg { flock = mgr.NetId, time = now, data = _writer.ToArraySegment() };
                foreach (var conn in _remotes)
                    conn.Send(msg, Channels.Unreliable);
            }
            _writer.Reset();
            Array.Clear(_candMgr, 0, live); // don't keep unloaded schools alive
        }

        void PrunePriorities()
        {
            var stale = new List<BoidsManager>();
            foreach (var mgr in _priority.Keys) if (mgr == null) stale.Add(mgr);
            foreach (var mgr in stale) _priority.Remove(mgr);
        }

        void SendFullState(BoidsManager mgr, NetworkConnectionToClient conn)
        {
            int cap = mgr.FishCapacity;
            if (cap == 0) return;

            // Which fish are gone (eaten before they joined).
            var mask = new byte[(cap + 7) / 8];
            for (int id = 0; id < cap; id++)
                if (mgr.IsAlive(id)) mask[id >> 3] |= (byte)(1 << (id & 7));
            conn.Send(new FlockAliveMsg { flock = mgr.NetId, mask = new ArraySegment<byte>(mask) }, Channels.Reliable);

            // Every live fish, in chunks.
            Box(mgr, out var min, out var size);
            double now = NetworkTime.time;
            int inChunk = 0;
            _writer.Reset();
            for (int id = 0; id < cap; id++)
            {
                if (!WriteEntry(mgr, id, min, size)) continue;
                if (++inChunk < FullSyncChunk) continue;
                conn.Send(new FlockStateMsg { flock = mgr.NetId, time = now, data = _writer.ToArraySegment() }, Channels.Reliable);
                _writer.Reset();
                inChunk = 0;
            }
            if (inChunk > 0)
                conn.Send(new FlockStateMsg { flock = mgr.NetId, time = now, data = _writer.ToArraySegment() }, Channels.Reliable);
            _writer.Reset();
        }

        bool WriteEntry(BoidsManager mgr, int id, Vector3 min, Vector3 size)
        {
            if (!mgr.TryGetState(id, out var pos, out var heading, out float speed)) return false;
            _writer.WriteUShort((ushort)id);
            _writer.WriteUShort(Quantize(pos.x, min.x, size.x));
            _writer.WriteUShort(Quantize(pos.y, min.y, size.y));
            _writer.WriteUShort(Quantize(pos.z, min.z, size.z));
            OctEncode(heading, out byte hx, out byte hy);
            _writer.WriteByte(hx);
            _writer.WriteByte(hy);
            _writer.WriteByte((byte)Mathf.Clamp(Mathf.RoundToInt(speed / MaxPackedSpeed * 255f), 0, 255));
            return true;
        }

        /// <summary>Host: a client's shark says it bit this fish. Check it against the host's school,
        /// remove it here, and tell everyone. A rejected bite (fish far off on the host) leaves that one
        /// fish missing only on the biter's screen - rare with this generous limit.</summary>
        public static void ServerEatRequest(byte flock, ushort fishId, NetworkIdentity eater)
        {
            var mgr = BoidsManager.ById(flock);
            if (mgr == null || eater == null) return;
            if (!mgr.TryGetState(fishId, out var pos, out _, out _)) return; // already eaten
            if ((pos - eater.transform.position).sqrMagnitude > MaxEatDistance * MaxEatDistance) return;

            PlayEaten(mgr, fishId, eater.netId); // the host sees it too
            Broadcast(new FlockEatenMsg { flock = flock, fish = fishId, eater = eater.netId });
            if (eater.TryGetComponent(out SharkAbilities shark)) shark.FeedFor(playerFish: false);
        }

        /// <summary>Host: the host's own shark already ate this fish locally; tell everyone else.</summary>
        public static void ServerAnnounceEaten(byte flock, ushort fishId, uint eaterNetId) =>
            Broadcast(new FlockEatenMsg { flock = flock, fish = fishId, eater = eaterNetId });

        static void Broadcast<T>(T msg) where T : struct, NetworkMessage
        {
            foreach (var conn in NetworkServer.connections.Values)
                if (conn != null && conn != NetworkServer.localConnection && conn.isReady)
                    conn.Send(msg, Channels.Reliable);
        }

        // ================================================================= clients
        static void OnState(FlockStateMsg msg)
        {
            if (NetworkServer.active) return; // the host is the authority
            var mgr = BoidsManager.ById(msg.flock);
            if (mgr == null || mgr.FishCapacity == 0) return;

            // The state is roughly half a round trip old by now: project it forward along its heading.
            float age = Mathf.Clamp((float)(NetworkTime.time - msg.time), 0f, 0.5f);
            Box(mgr, out var min, out var size);
            using (var r = NetworkReaderPool.Get(msg.data))
            {
                while (r.Remaining >= EntrySize)
                {
                    int id = r.ReadUShort();
                    float x = Dequantize(r.ReadUShort(), min.x, size.x);
                    float y = Dequantize(r.ReadUShort(), min.y, size.y);
                    float z = Dequantize(r.ReadUShort(), min.z, size.z);
                    byte hx = r.ReadByte(), hy = r.ReadByte();
                    float speed = r.ReadByte() / 255f * MaxPackedSpeed;
                    Vector3 heading = OctDecode(hx, hy);
                    mgr.ReceiveHostState(id, new Vector3(x, y, z) + heading * (speed * age), heading);
                }
            }
        }

        static void OnEaten(FlockEatenMsg msg)
        {
            if (NetworkServer.active) return; // the host already played it
            var mgr = BoidsManager.ById(msg.flock);
            if (mgr != null) PlayEaten(mgr, msg.fish, msg.eater);
        }

        static void OnAlive(FlockAliveMsg msg)
        {
            if (NetworkServer.active) return;
            var mgr = BoidsManager.ById(msg.flock);
            if (mgr == null) return;
            var mask = msg.mask;
            int cap = mgr.FishCapacity;
            for (int id = 0; id < cap; id++)
            {
                int b = id >> 3;
                bool alive = b < mask.Count && (mask.Array[mask.Offset + b] & (1 << (id & 7))) != 0;
                if (!alive && mgr.IsAlive(id)) mgr.DespawnFish(id);
            }
        }

        // Pull the fish out of the school and play it being eaten by that shark (or just vanish).
        static void PlayEaten(BoidsManager mgr, int fishId, uint eaterNetId)
        {
            var fish = mgr.RemoveAt(fishId);
            if (fish == null) return; // already gone - e.g. our own bite, which played locally

            SharkAbilities eater = null;
            if (eaterNetId != 0 && TryGetSpawned(eaterNetId, out var identity))
                identity.TryGetComponent(out eater);

            if (!fish.TryGetComponent(out Edible edible)) edible = fish.AddComponent<Edible>();
            if (eater != null) edible.Devour(eater.gameObject, eater.MouthAnchor);
            else edible.Devour(null);
        }

        static bool TryGetSpawned(uint netId, out NetworkIdentity identity) =>
            NetworkServer.active
                ? NetworkServer.spawned.TryGetValue(netId, out identity)
                : NetworkClient.spawned.TryGetValue(netId, out identity);

        // ================================================================= packing
        static void Box(BoidsManager mgr, out Vector3 min, out Vector3 size)
        {
            Vector3 half = mgr.area * 0.5f + Vector3.one * PositionMargin;
            min = mgr.transform.position - half;
            size = half * 2f;
        }

        // 16 bits per axis over the flock's box: ~3 mm steps for a ~200 m area.
        static ushort Quantize(float v, float min, float size) =>
            (ushort)Mathf.Clamp(Mathf.RoundToInt((v - min) / size * 65535f), 0, 65535);

        static float Dequantize(ushort q, float min, float size) => min + q / 65535f * size;

        // Octahedral unit-vector encoding: a heading in two bytes, ~1 degree precision.
        static void OctEncode(Vector3 n, out byte bx, out byte by)
        {
            float s = Mathf.Abs(n.x) + Mathf.Abs(n.y) + Mathf.Abs(n.z);
            float x = s > 1e-6f ? n.x / s : 0f, y = s > 1e-6f ? n.y / s : 0f;
            if (n.z < 0f)
            {
                float ox = (1f - Mathf.Abs(y)) * (x >= 0f ? 1f : -1f);
                float oy = (1f - Mathf.Abs(x)) * (y >= 0f ? 1f : -1f);
                x = ox;
                y = oy;
            }
            bx = (byte)Mathf.Clamp(Mathf.RoundToInt((x * 0.5f + 0.5f) * 255f), 0, 255);
            by = (byte)Mathf.Clamp(Mathf.RoundToInt((y * 0.5f + 0.5f) * 255f), 0, 255);
        }

        static Vector3 OctDecode(byte bx, byte by)
        {
            float x = bx / 255f * 2f - 1f, y = by / 255f * 2f - 1f;
            float z = 1f - Mathf.Abs(x) - Mathf.Abs(y);
            if (z < 0f)
            {
                float ox = (1f - Mathf.Abs(y)) * (x >= 0f ? 1f : -1f);
                float oy = (1f - Mathf.Abs(x)) * (y >= 0f ? 1f : -1f);
                x = ox;
                y = oy;
            }
            return new Vector3(x, y, z).normalized;
        }
    }
}
