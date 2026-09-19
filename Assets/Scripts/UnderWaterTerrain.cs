using UnityEngine;
using CodenameLib.ProceduralTerrain;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class UnderWaterTerrain : MonoBehaviour
{
    [SerializeField] private Material material;
    [SerializeField] private TerrainSettings terrainSettings;

    /// <summary>Settings this terrain was built from. Anything extending it must use the same ones.</summary>
    public TerrainSettings Settings => terrainSettings;

    /// <summary>Material actually used, which may differ from the inspector field if none was assigned.</summary>
    public Material TerrainMaterial => material;

    /// <summary>The mesh produced by the last generation, or null before Start has run.</summary>
    public Mesh GeneratedMesh { get; private set; }

    /// <summary>
    /// Raised once the terrain exists. Subscribe from Awake — the terrain is built in Start, so a
    /// listener that subscribes in its own Start may miss it depending on script execution order.
    /// </summary>
    public event System.Action<UnderWaterTerrain> Generated;

    void Start()
    {
        var settings = terrainSettings;

        MeshTerrainResult result = MeshTerrainGenerator.GenerateMeshTerrain(settings);

        if (!result.success)
        {
            Debug.LogError("Failed to generate terrain mesh: " + result.errorMessage);
            return;
        }

        var meshFilter = GetComponent<MeshFilter>();
        var meshRenderer = GetComponent<MeshRenderer>();
        var meshCollider = GetComponent<MeshCollider>();

        meshFilter.sharedMesh = result.mesh;
        meshCollider.sharedMesh = result.mesh;

        if(material == null)
            material = new(Shader.Find("Standard"));
        meshRenderer.sharedMaterial = material;

        GeneratedMesh = result.mesh;
        Generated?.Invoke(this);
    }
}
