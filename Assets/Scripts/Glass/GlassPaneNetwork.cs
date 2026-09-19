using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Makes a group of glass panes behave as one continuous wall instead of separate slabs that
/// interpenetrate.
///
/// Each pane is treated as a wall segment: a centreline along its long axis, with a thickness and
/// a height. Where two segments cross, both are trimmed back to the crossing point, which removes
/// the stubs that otherwise poke out past the joint. The segments carry round caps, so two trimmed
/// ends meeting at that point form a rounded outer corner on their own — no extra corner geometry.
///
/// Everything is recomputed from transforms, so panes can be dragged or animated and the joints
/// follow. Nothing is baked.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class GlassPaneNetwork : MonoBehaviour
{
    public const int MaxNeighbours = 8;

    [Tooltip("Extra rounding added to the joint, on top of the pane's own half thickness.")]
    [Range(0f, 4f)] public float cornerRadius = 0.25f;

    [Tooltip("How close two centrelines must come before they count as joined. Scaled by the " +
             "panes' thickness, so thicker walls latch on from further out.")]
    [Range(1f, 8f)] public float junctionTolerance = 2f;

    [Tooltip("Recompute every frame. Leave on while authoring; turn off if the panes never move.")]
    public bool continuousUpdate = true;

    static readonly int k_OwnSegA   = Shader.PropertyToID("_OwnSegA");
    static readonly int k_OwnSegB   = Shader.PropertyToID("_OwnSegB");
    static readonly int k_OtherSegA = Shader.PropertyToID("_OtherSegA");
    static readonly int k_OtherSegB = Shader.PropertyToID("_OtherSegB");
    static readonly int k_OtherCount = Shader.PropertyToID("_OtherCount");
    static readonly int k_CornerRadius = Shader.PropertyToID("_CornerRadius");
    static readonly int k_NetworkOn = Shader.PropertyToID("_PaneNetworkOn");

    struct Segment
    {
        public Vector3 A, B;              // centreline, world space, already trimmed
        public float HalfThickness, HalfHeight;
        public Renderer Rend;
    }

    readonly List<Segment> m_Segments = new List<Segment>();
    MaterialPropertyBlock m_Block;

    void OnEnable() { Rebuild(); }
    void OnValidate() { Rebuild(); }
    void Update() { if (continuousUpdate) Rebuild(); }

    public void Rebuild()
    {
        m_Segments.Clear();

        var renderers = GetComponentsInChildren<MeshRenderer>();
        foreach (var r in renderers)
        {
            if (!r.enabled) continue;
            var t = r.transform;
            var scale = t.lossyScale;

            // Long axis is local X, thickness local Z, height local Y — the convention the
            // barrier builder uses when it flattens a cube into a pane.
            float halfLength = Mathf.Abs(scale.x) * 0.5f;
            if (halfLength < 1e-4f) continue;

            m_Segments.Add(new Segment
            {
                A = t.position - t.right * halfLength,
                B = t.position + t.right * halfLength,
                HalfThickness = Mathf.Abs(scale.z) * 0.5f,
                HalfHeight = Mathf.Abs(scale.y) * 0.5f,
                Rend = r,
            });
        }

        TrimAtJunctions();
        PushToMaterials();
    }

    /// <summary>Pull every segment back to where it crosses another, so no stub survives the joint.</summary>
    void TrimAtJunctions()
    {
        int n = m_Segments.Count;
        var trimA = new float[n];   // how far in from the A end, 0..1
        var trimB = new float[n];   // how far in from the B end, 0..1
        for (int i = 0; i < n; i++) { trimA[i] = 0f; trimB[i] = 1f; }

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                if (i == j) continue;

                float ti, tj;
                float gap = ClosestPointsBetweenSegments(
                    m_Segments[i].A, m_Segments[i].B,
                    m_Segments[j].A, m_Segments[j].B, out ti, out tj);

                float reach = (m_Segments[i].HalfThickness + m_Segments[j].HalfThickness)
                              * junctionTolerance;
                if (gap > reach) continue;

                // Trim whichever end of i is nearer the crossing; the far end is untouched.
                if (ti < 0.5f) trimA[i] = Mathf.Max(trimA[i], ti);
                else           trimB[i] = Mathf.Min(trimB[i], ti);
            }
        }

        for (int i = 0; i < n; i++)
        {
            // A pane swallowed entirely by its neighbours would invert; leave it alone instead.
            if (trimA[i] >= trimB[i] - 1e-3f) continue;

            var s = m_Segments[i];
            Vector3 a0 = s.A, b0 = s.B;
            s.A = Vector3.Lerp(a0, b0, trimA[i]);
            s.B = Vector3.Lerp(a0, b0, trimB[i]);
            m_Segments[i] = s;
        }
    }

    void PushToMaterials()
    {
        m_Block ??= new MaterialPropertyBlock();

        var otherA = new Vector4[MaxNeighbours];
        var otherB = new Vector4[MaxNeighbours];

        for (int i = 0; i < m_Segments.Count; i++)
        {
            var self = m_Segments[i];

            int count = 0;
            for (int j = 0; j < m_Segments.Count && count < MaxNeighbours; j++)
            {
                if (i == j) continue;
                var o = m_Segments[j];
                otherA[count] = new Vector4(o.A.x, o.A.y, o.A.z, o.HalfThickness);
                otherB[count] = new Vector4(o.B.x, o.B.y, o.B.z, o.HalfHeight);
                count++;
            }
            for (int k = count; k < MaxNeighbours; k++) { otherA[k] = Vector4.zero; otherB[k] = Vector4.zero; }

            self.Rend.GetPropertyBlock(m_Block);
            m_Block.SetVector(k_OwnSegA, new Vector4(self.A.x, self.A.y, self.A.z, self.HalfThickness));
            m_Block.SetVector(k_OwnSegB, new Vector4(self.B.x, self.B.y, self.B.z, self.HalfHeight));
            m_Block.SetVectorArray(k_OtherSegA, otherA);
            m_Block.SetVectorArray(k_OtherSegB, otherB);
            m_Block.SetFloat(k_OtherCount, count);
            m_Block.SetFloat(k_CornerRadius, cornerRadius);
            m_Block.SetFloat(k_NetworkOn, 1f);
            self.Rend.SetPropertyBlock(m_Block);
        }
    }

    /// <summary>Gap between two segments, with the parameters of the closest points on each.</summary>
    static float ClosestPointsBetweenSegments(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2,
                                              out float s, out float t)
    {
        // Ericson, Real-Time Collision Detection, 5.1.9.
        Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
        float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
        const float eps = 1e-6f;

        if (a <= eps && e <= eps) { s = t = 0f; return Vector3.Distance(p1, p2); }
        if (a <= eps) { s = 0f; t = Mathf.Clamp01(f / e); }
        else
        {
            float c = Vector3.Dot(d1, r);
            if (e <= eps) { t = 0f; s = Mathf.Clamp01(-c / a); }
            else
            {
                float b = Vector3.Dot(d1, d2);
                float denom = a * e - b * b;
                s = denom > eps ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                t = (b * s + f) / e;
                if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
            }
        }
        return Vector3.Distance(p1 + d1 * s, p2 + d2 * t);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 1f, 0.8f, 0.9f);
        foreach (var s in m_Segments)
        {
            Gizmos.DrawLine(s.A, s.B);
            Gizmos.DrawWireSphere(s.A, s.HalfThickness + cornerRadius);
            Gizmos.DrawWireSphere(s.B, s.HalfThickness + cornerRadius);
        }
    }
}
