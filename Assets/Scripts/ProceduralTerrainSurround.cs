using System.Collections.Generic;
using UnityEngine;
using CodenameLib.ProceduralTerrain;

/// <summary>
/// Surrounds the generated terrain chunk with more chunks from the same noise field, so the
/// seabed reads as continuing instead of ending at the chunk edge.
///
/// This regenerates alongside the terrain every play, which a baked mesh cannot do. It also does
/// not use CodenameLib's InfiniteTerrain, because that advances the noise offset by the chunk's
/// world size while the compute shader's noise advances with <c>scale</c> — with size 100 and
/// scale 50 every chunk border would jump two whole noise periods.
///
/// Derivation of the correct step: the shader samples at <c>uv = (id + 0.5) / res</c> and forms
/// <c>p = uv * scale + offset</c>. A chunk's first and last vertices therefore sit at uv
/// <c>0.5/res</c> and <c>(res - 0.5)/res</c>, so a neighbour must advance by
/// <c>scale * (res - 1) / res</c> for its first vertex to land on the same noise coordinate as
/// its neighbour's last.
/// </summary>
[DefaultExecutionOrder(100)]
public class ProceduralTerrainSurround : MonoBehaviour
{
    [Tooltip("Rings of chunks around the centre. 1 = 8 neighbours. Each ring costs " +
             "(2n+1)^2 - (2n-1)^2 more chunks, so keep it just past the water's visible range.")]
    [Range(1, 3)] public int rings = 2;

    [Tooltip("Snap shared edge vertices to a common height. The shader adds a small per-pixel " +
             "jitter keyed to the pixel index, so the same world point differs slightly between " +
             "two chunks and the seam cracks without this.")]
    public bool weldSeams = true;

    [Tooltip("Surround chunks are scenery. Colliders are only needed if anything can swim out there.")]
    public bool generateColliders = false;

    [Tooltip("Rebuild if the terrain regenerates again later.")]
    public bool rebuildOnRegenerate = true;

    const string k_ContainerName = "TerrainSurround";

    UnderWaterTerrain m_UnderWater;
    TestTerrain m_TestTerrain;
    Transform m_Container;

    void Awake()
    {
        // Subscribed in Awake on purpose: both generators build in Start.
        m_UnderWater = GetComponent<UnderWaterTerrain>();
        m_TestTerrain = GetComponent<TestTerrain>();

        if (m_UnderWater == null && m_TestTerrain == null)
        {
            Debug.LogError("[TerrainSurround] Needs an UnderWaterTerrain or TestTerrain to follow.", this);
            enabled = false;
            return;
        }

        if (m_UnderWater != null && m_TestTerrain != null)
        {
            Debug.LogWarning("[TerrainSurround] Both UnderWaterTerrain and TestTerrain are on this " +
                "object. They each generate a mesh and overwrite the same MeshFilter, so which one " +
                "you actually see depends on execution order. Following TestTerrain, since it wins " +
                "here — remove one of them.", this);
        }

        // Whichever assigns the MeshFilter last is the terrain the player sees.
        if (m_TestTerrain != null) TestTerrain.OnCreatingDone.AddListener(OnTestTerrainGenerated);
        else m_UnderWater.Generated += OnUnderWaterGenerated;
    }

    void OnDestroy()
    {
        if (m_UnderWater != null) m_UnderWater.Generated -= OnUnderWaterGenerated;
        if (m_TestTerrain != null) TestTerrain.OnCreatingDone.RemoveListener(OnTestTerrainGenerated);
    }

    void OnUnderWaterGenerated(UnderWaterTerrain terrain)
    {
        if (m_Container != null && !rebuildOnRegenerate) return;
        Build(terrain.Settings, terrain.TerrainMaterial, terrain.GeneratedMesh);
    }

    void OnTestTerrainGenerated()
    {
        if (m_Container != null && !rebuildOnRegenerate) return;
        var filter = GetComponent<MeshFilter>();
        var renderer = GetComponent<MeshRenderer>();
        Build(m_TestTerrain.settings,
              renderer != null ? renderer.sharedMaterial : null,
              filter != null ? filter.sharedMesh : null);
    }

    public void Build(TerrainSettings settings, Material material, Mesh centreMesh)
    {
        if (m_Container != null)
        {
            if (Application.isPlaying) Destroy(m_Container.gameObject);
            else DestroyImmediate(m_Container.gameObject);
        }

        int res = settings.EffectiveResolution;
        float size = settings.size;
        if (res < 2 || size <= 0f)
        {
            Debug.LogError("[TerrainSurround] Terrain settings have no usable resolution or size.");
            return;
        }

        float noiseStep = settings.scale * (res - 1) / res;

        var container = new GameObject(k_ContainerName);
        container.transform.SetParent(transform, false);
        m_Container = container.transform;

        // Seeded with the centre chunk so neighbours weld onto the real terrain, not each other.
        var edgeHeights = new Dictionary<Vector2Int, float>();
        if (weldSeams && centreMesh != null)
            RegisterEdges(centreMesh, Vector3.zero, res, edgeHeights);

        int built = 0;
        var timer = System.Diagnostics.Stopwatch.StartNew();

        // Nearest rings first, so the weld propagates outward from the real terrain.
        for (int ring = 1; ring <= rings; ring++)
        {
            for (int dz = -ring; dz <= ring; dz++)
            {
                for (int dx = -ring; dx <= ring; dx++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != ring) continue;   // this ring only

                    var chunkSettings = settings;
                    chunkSettings.offset = settings.offset + new Vector2(dx, dz) * noiseStep;

                    var result = MeshTerrainGenerator.GenerateMeshTerrain(chunkSettings);
                    if (!result.success || result.mesh == null)
                    {
                        Debug.LogWarning($"[TerrainSurround] Chunk ({dx},{dz}) failed: {result.errorMessage}");
                        continue;
                    }

                    var localPos = new Vector3(dx * size, 0f, dz * size);
                    if (weldSeams) WeldEdges(result.mesh, localPos, res, edgeHeights);

                    CreateChunk(result.mesh, localPos, material, $"Surround {dx},{dz}");
                    built++;
                }
            }
        }

        timer.Stop();
        Debug.Log($"[TerrainSurround] {built} chunk(s) in {timer.ElapsedMilliseconds} ms, " +
                  $"noise step {noiseStep:F3} per chunk (scale {settings.scale}, res {res}).", this);
    }

    void CreateChunk(Mesh mesh, Vector3 localPos, Material material, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(m_Container, false);
        go.transform.localPosition = localPos;

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        if (generateColliders)
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    static Vector2Int Key(float x, float z) =>
        new Vector2Int(Mathf.RoundToInt(x * 100f), Mathf.RoundToInt(z * 100f));

    static bool IsEdge(int index, int res)
    {
        int x = index % res, z = index / res;
        return x == 0 || z == 0 || x == res - 1 || z == res - 1;
    }

    static void RegisterEdges(Mesh mesh, Vector3 origin, int res, Dictionary<Vector2Int, float> map)
    {
        var verts = mesh.vertices;
        for (int i = 0; i < verts.Length; i++)
        {
            if (!IsEdge(i, res)) continue;
            map[Key(origin.x + verts[i].x, origin.z + verts[i].z)] = origin.y + verts[i].y;
        }
    }

    static void WeldEdges(Mesh mesh, Vector3 origin, int res, Dictionary<Vector2Int, float> map)
    {
        var verts = mesh.vertices;
        bool changed = false;

        for (int i = 0; i < verts.Length; i++)
        {
            if (!IsEdge(i, res)) continue;

            var key = Key(origin.x + verts[i].x, origin.z + verts[i].z);
            if (map.TryGetValue(key, out float y))
            {
                verts[i].y = y - origin.y;      // stored height is world-space
                changed = true;
            }
            else
            {
                map[key] = origin.y + verts[i].y;
            }
        }

        if (!changed) return;
        mesh.vertices = verts;
        mesh.RecalculateNormals();              // heights moved, so the old normals are stale
        mesh.RecalculateBounds();
    }
}
