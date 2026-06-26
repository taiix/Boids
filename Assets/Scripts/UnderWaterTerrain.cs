using UnityEngine;
using CodenameLib.ProceduralTerrain;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class UnderWaterTerrain : MonoBehaviour
{
    [SerializeField] private Material material;
    [SerializeField] private TerrainSettings terrainSettings;

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

    }
}
