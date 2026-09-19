using UnityEngine;

/// <summary>
/// Holds a baked signed distance field of the geometry surrounding one glass pane and feeds
/// it to that pane's material.
///
/// The field is baked in the pane's own oriented frame, so a rotated pane still gets a volume
/// that is thin along its normal instead of a fat world-aligned box. It describes the
/// *environment*, not the pane, so moving either the pane or the scenery invalidates it and it
/// has to be re-baked — <see cref="IsStale"/> reports that.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(MeshRenderer))]
[DisallowMultipleComponent]
public class GlassSdfVolume : MonoBehaviour
{
    [Header("Baked data (Tools > Frosted Glass > Bake Contact SDF)")]
    public Texture3D sdf;

    [Tooltip("World-space centre of the baked volume.")]
    public Vector3 bakeCenterWS;
    [Tooltip("World-space half extents of the baked volume.")]
    public Vector3 bakeExtentsWS = Vector3.one;
    [Tooltip("World-space orientation of the baked volume.")]
    public Quaternion bakeRotationWS = Quaternion.identity;

    [Tooltip("Pane transform at bake time, used to detect that the bake is stale.")]
    public Matrix4x4 bakedAtLocalToWorld = Matrix4x4.identity;

    [Header("Contact blend")]
    [Tooltip("How far from the surface the fillet reaches, in metres. Also sets how far the " +
             "proxy geometry is inflated, so larger costs more overdraw.")]
    [Range(0.05f, 8f)] public float filletRadius = 1.5f;

    [Tooltip("How strongly the glass takes on the colour of the surface it is merging into.")]
    [Range(0f, 1f)] public float surfaceBlend = 0.7f;

    [Tooltip("Raymarch steps through the fillet. Raise if the blend looks faceted.")]
    [Range(8, 96)] public int marchSteps = 32;

    static readonly int k_Sdf             = Shader.PropertyToID("_SdfTex");
    static readonly int k_WorldToUvw      = Shader.PropertyToID("_SdfWorldToUvw");
    static readonly int k_HasSdf          = Shader.PropertyToID("_HasSdf");
    static readonly int k_GlassWorldToObj = Shader.PropertyToID("_GlassWorldToFrame");
    static readonly int k_GlassHalf       = Shader.PropertyToID("_GlassHalfExtentsWS");
    static readonly int k_ProxyExpand     = Shader.PropertyToID("_ProxyExpandOS");
    static readonly int k_Fillet          = Shader.PropertyToID("_FilletRadius");
    static readonly int k_SurfaceBlend    = Shader.PropertyToID("_SurfaceBlend");
    static readonly int k_MarchSteps      = Shader.PropertyToID("_MarchSteps");

    MeshRenderer m_Renderer;
    MaterialPropertyBlock m_Block;

    MeshRenderer Renderer => m_Renderer != null ? m_Renderer : (m_Renderer = GetComponent<MeshRenderer>());

    /// <summary>True when the pane has moved since the field was baked, so the field no longer lines up.</summary>
    public bool IsStale =>
        sdf != null && !MatrixApproximately(bakedAtLocalToWorld, transform.localToWorldMatrix);

    void OnEnable() { Apply(); }
    void OnValidate() { Apply(); }
    void Update()
    {
        // The pane is expected to be static, but keep the proxy correct if it is being moved
        // in the editor so the inflated bounds do not lag behind.
        if (transform.hasChanged) { Apply(); transform.hasChanged = false; }
    }

    /// <summary>World-space matrix taking a point into this volume's 0..1 texture coordinates.</summary>
    public Matrix4x4 WorldToUvw()
    {
        // Volume frame -> unit cube, then bias into 0..1.
        var worldToVolume = Matrix4x4.TRS(bakeCenterWS, bakeRotationWS, Vector3.one).inverse;
        var scale = new Vector3(
            0.5f / Mathf.Max(bakeExtentsWS.x, 1e-4f),
            0.5f / Mathf.Max(bakeExtentsWS.y, 1e-4f),
            0.5f / Mathf.Max(bakeExtentsWS.z, 1e-4f));
        var normalise = Matrix4x4.TRS(new Vector3(0.5f, 0.5f, 0.5f), Quaternion.identity, Vector3.one)
                      * Matrix4x4.Scale(scale);
        return normalise * worldToVolume;
    }

    public void Apply()
    {
        var r = Renderer;
        if (r == null) return;

        var scale = transform.lossyScale;
        // The mesh is a unit cube, so its own half extent is 0.5 before scaling.
        var halfExtents = new Vector3(
            Mathf.Abs(scale.x) * 0.5f, Mathf.Abs(scale.y) * 0.5f, Mathf.Abs(scale.z) * 0.5f);

        // The fillet bulges outside the pane's own thickness, so the proxy has to be inflated
        // to contain it. Expressed in object space because the vertex shader works there.
        var expand = new Vector3(
            filletRadius / Mathf.Max(Mathf.Abs(scale.x), 1e-4f),
            filletRadius / Mathf.Max(Mathf.Abs(scale.y), 1e-4f),
            filletRadius / Mathf.Max(Mathf.Abs(scale.z), 1e-4f));

        // Distances stay in world units, so the frame must be orthonormal: rotation and
        // translation only, never the pane's non-uniform scale.
        var worldToFrame = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one).inverse;

        m_Block ??= new MaterialPropertyBlock();
        r.GetPropertyBlock(m_Block);

        if (sdf != null) m_Block.SetTexture(k_Sdf, sdf);
        m_Block.SetFloat(k_HasSdf, sdf != null ? 1f : 0f);
        m_Block.SetMatrix(k_WorldToUvw, WorldToUvw());
        m_Block.SetMatrix(k_GlassWorldToObj, worldToFrame);
        m_Block.SetVector(k_GlassHalf, halfExtents);
        m_Block.SetVector(k_ProxyExpand, expand);
        m_Block.SetFloat(k_Fillet, filletRadius);
        m_Block.SetFloat(k_SurfaceBlend, surfaceBlend);
        m_Block.SetFloat(k_MarchSteps, marchSteps);
        r.SetPropertyBlock(m_Block);

        // Without this the inflated proxy gets frustum-culled using the original thin bounds
        // and the fillet pops out of view at glancing angles.
        var meshFilter = GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            var b = meshFilter.sharedMesh.bounds;
            b.Expand(expand * 2f);
            r.localBounds = b;
        }
    }

    static bool MatrixApproximately(Matrix4x4 a, Matrix4x4 b)
    {
        for (int i = 0; i < 16; i++)
            if (Mathf.Abs(a[i] - b[i]) > 1e-4f) return false;
        return true;
    }

    void OnDrawGizmosSelected()
    {
        if (sdf == null) return;
        Gizmos.color = IsStale ? new Color(1f, 0.4f, 0.2f, 0.9f) : new Color(0.4f, 0.9f, 1f, 0.7f);
        Gizmos.matrix = Matrix4x4.TRS(bakeCenterWS, bakeRotationWS, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, bakeExtentsWS * 2f);
    }
}
