using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace FishGame
{
    /// <summary>Host -> a joining client: a slice of the host's seabed heights (16 bits per vertex).</summary>
    public struct TerrainHeightsMsg : NetworkMessage
    {
        public int offset;              // first vertex in this slice
        public int total;               // vertex count of the whole seabed
        public float minY, maxY;        // quantisation range
        public ArraySegment<byte> data; // little-endian ushort per vertex
    }

    /// <summary>
    /// Makes every player's seabed identical to the host's.
    ///
    /// The terrain package erodes the seabed on the GPU with one thread per droplet and unsynchronised
    /// writes, so the result differs run to run and machine to machine (measured: 12 cm on average, up
    /// to ~4 m) - and the reef follows it, since coral is placed by height. So the host's seabed is the
    /// reference: every player rounds their heights to the same 16-bit grid, and a joining player
    /// receives the host's grid once (~115 KB, reliable, in 8 KB slices) and rebuilds seabed + reef on
    /// it. Everyone then has bit-identical heights, and <see cref="SeededVegetation"/> places identical
    /// coral on them.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class TerrainNetSync : MonoBehaviour
    {
        const int SliceVertices = 4096; // 8 KB per message, under Telepathy's 16 KB limit

        static TerrainNetSync s_instance;
        static readonly Predicate<int> s_notReadyRemote = id =>
            !NetworkServer.connections.TryGetValue(id, out var c) || c == null || !c.isReady;

        // Client: host heights being received / waiting for our terrain to exist.
        static ushort[] s_rx;
        static int s_rxCount;
        static float s_rxMin, s_rxMax;
        static bool s_rxComplete;

        /// <summary>Raised whenever the seabed's heights change (our own generation, or the host's
        /// replacing it) - anything resting on the seabed should re-settle.</summary>
        public static event Action SeabedChanged;

        readonly HashSet<int> _sent = new HashSet<int>();
        bool _clientHooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            s_rx = null;
            s_rxComplete = false;
            if (s_instance != null) return;
            var go = new GameObject(nameof(TerrainNetSync));
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<TerrainNetSync>();
        }

        void LateUpdate()
        {
            // Register the client handler at the start of every client session (Mirror clears handlers
            // on shutdown, and an unhandled message disconnects the client).
            if (NetworkClient.active)
            {
                if (!_clientHooked) { NetworkClient.ReplaceHandler<TerrainHeightsMsg>(OnHeights); _clientHooked = true; }
            }
            else
            {
                _clientHooked = false;
                s_rx = null;
                s_rxComplete = false;
            }

            if (!NetworkServer.active) { _sent.Clear(); return; }

            // Anyone who left or is reloading gets the seabed again once ready.
            _sent.RemoveWhere(s_notReadyRemote);
            foreach (var conn in NetworkServer.connections.Values)
            {
                if (conn == null || conn == NetworkServer.localConnection || !conn.isReady) continue;
                if (_sent.Contains(conn.connectionId)) continue;
                if (SendSeabed(conn)) _sent.Add(conn.connectionId);
            }
        }

        // ================================================================= host
        static bool SendSeabed(NetworkConnectionToClient conn)
        {
            var mesh = SeabedMesh(out _);
            if (mesh == null) return false; // not generated yet - try again next frame

            var verts = mesh.vertices;
            HeightRange(verts, out float min, out float max);
            var buf = new byte[Mathf.Min(SliceVertices, verts.Length) * 2];
            for (int off = 0; off < verts.Length; off += SliceVertices)
            {
                int n = Mathf.Min(SliceVertices, verts.Length - off);
                for (int i = 0; i < n; i++)
                {
                    ushort q = Quantize(verts[off + i].y, min, max);
                    buf[2 * i] = (byte)q;
                    buf[2 * i + 1] = (byte)(q >> 8);
                }
                conn.Send(new TerrainHeightsMsg
                {
                    offset = off, total = verts.Length, minY = min, maxY = max,
                    data = new ArraySegment<byte>(buf, 0, n * 2),
                }, Channels.Reliable);
            }
            return true;
        }

        // ================================================================= client
        static void OnHeights(TerrainHeightsMsg msg)
        {
            if (NetworkServer.active) return; // the host's own seabed is the reference
            if (msg.offset == 0 || s_rx == null || s_rx.Length != msg.total)
            {
                s_rx = new ushort[msg.total];
                s_rxCount = 0;
                s_rxComplete = false;
            }
            s_rxMin = msg.minY;
            s_rxMax = msg.maxY;

            var a = msg.data.Array;
            int o = msg.data.Offset, n = msg.data.Count / 2;
            for (int i = 0; i < n && msg.offset + i < s_rx.Length; i++)
                s_rx[msg.offset + i] = (ushort)(a[o + 2 * i] | (a[o + 2 * i + 1] << 8));
            s_rxCount += n;
            if (s_rxCount < msg.total) return;

            s_rxComplete = true;
            // Our terrain already exists: swap its heights and re-place the reef on them. Otherwise it's
            // applied when the terrain is generated, before any coral goes down.
            if (SeabedMesh(out _) != null && ApplyHostSeabed())
                SeededVegetation.Rebuild();
        }

        /// <summary>Called when the local terrain has just been generated, before the reef is placed.
        /// Uses the host's heights if they've arrived; otherwise rounds ours to the shared 16-bit grid
        /// (the host does this too, so what it later sends is exactly what it has).</summary>
        public static void OnLocalTerrainGenerated()
        {
            if (s_rxComplete && ApplyHostSeabed()) return;

            var mesh = SeabedMesh(out var terrain);
            if (mesh == null) return;
            var verts = mesh.vertices;
            HeightRange(verts, out float min, out float max);
            for (int i = 0; i < verts.Length; i++)
                verts[i].y = Dequantize(Quantize(verts[i].y, min, max), min, max);
            SetHeights(terrain, mesh, verts);
        }

        static bool ApplyHostSeabed()
        {
            var mesh = SeabedMesh(out var terrain);
            if (mesh == null || s_rx == null) return false;
            var verts = mesh.vertices;
            if (verts.Length != s_rx.Length)
            {
                Debug.LogWarning($"[TerrainNetSync] Host seabed has {s_rx.Length} vertices, ours {verts.Length} " +
                                 "(different terrain settings?) - keeping ours.");
                s_rx = null;
                s_rxComplete = false;
                return false;
            }
            for (int i = 0; i < verts.Length; i++)
                verts[i].y = Dequantize(s_rx[i], s_rxMin, s_rxMax);
            SetHeights(terrain, mesh, verts);
            s_rx = null;
            s_rxComplete = false;
            Debug.Log("[TerrainNetSync] Using the host's seabed.");
            return true;
        }

        // ================================================================= helpers
        static Mesh SeabedMesh(out TestTerrain terrain)
        {
            terrain = FindAnyObjectByType<TestTerrain>();
            return terrain != null && terrain.TryGetComponent(out MeshFilter mf) ? mf.sharedMesh : null;
        }

        static void SetHeights(TestTerrain terrain, Mesh mesh, Vector3[] verts)
        {
            mesh.vertices = verts;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            if (terrain.TryGetComponent(out MeshCollider mc))
            {
                mc.sharedMesh = null; // force the collider to re-bake
                mc.sharedMesh = mesh;
            }
            SeabedChanged?.Invoke();
        }

        static void HeightRange(Vector3[] verts, out float min, out float max)
        {
            min = float.MaxValue;
            max = float.MinValue;
            foreach (var v in verts)
            {
                if (v.y < min) min = v.y;
                if (v.y > max) max = v.y;
            }
            if (max <= min) max = min + 1f;
        }

        // Double maths so host and clients get bit-identical floats from the same (q, min, max).
        static ushort Quantize(float y, float min, float max) =>
            (ushort)Math.Max(0, Math.Min(65535, (int)Math.Round((y - (double)min) / ((double)max - min) * 65535.0)));

        static float Dequantize(ushort q, float min, float max) =>
            (float)(min + q / 65535.0 * ((double)max - min));
    }
}
