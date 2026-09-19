using UnityEditor;
using UnityEngine;

// Builds a ring of flattened cubes around a point and drops the frosted glass material on them.
// Kept as an editor tool rather than a component so the barrier costs nothing at runtime.
public class FrostedGlassBarrierBuilder : EditorWindow
{
    enum PaneShape { FlattenedCube, Quad }

    const string k_RootName = "FrostedGlassBarrier";
    const string k_MaterialPath = "Assets/Materials/Glass/M_FrostedGlass_Barrier.mat";

    Vector3 m_Center = new Vector3(0f, -30f, -8f);
    float m_Radius = 200f;
    int m_PaneCount = 16;
    float m_TopY = 0f;
    float m_BottomY = -60f;
    float m_Thickness = 0.5f;
    PaneShape m_Shape = PaneShape.FlattenedCube;
    Material m_Material;

    [MenuItem("Tools/Frosted Glass Barrier")]
    static void Open() => GetWindow<FrostedGlassBarrierBuilder>("Glass Barrier");

    void OnEnable()
    {
        if (m_Material == null)
            m_Material = AssetDatabase.LoadAssetAtPath<Material>(k_MaterialPath);
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("Ring", EditorStyles.boldLabel);
        m_Center = EditorGUILayout.Vector3Field("Center", m_Center);
        m_Radius = EditorGUILayout.FloatField("Radius", m_Radius);
        m_PaneCount = Mathf.Max(3, EditorGUILayout.IntField("Pane Count", m_PaneCount));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Extent", EditorStyles.boldLabel);
        m_TopY = EditorGUILayout.FloatField("Top Y", m_TopY);
        m_BottomY = EditorGUILayout.FloatField("Bottom Y", m_BottomY);
        m_Thickness = EditorGUILayout.FloatField("Thickness", m_Thickness);
        m_Shape = (PaneShape)EditorGUILayout.EnumPopup("Pane Shape", m_Shape);
        m_Material = (Material)EditorGUILayout.ObjectField("Material", m_Material, typeof(Material), false);

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(Selection.activeGameObject == null))
        {
            if (GUILayout.Button("Fit Ring To Selection"))
                FitToSelection();
        }

        EditorGUILayout.HelpBox(
            m_Shape == PaneShape.FlattenedCube
                ? "A box has a front and a back wall, so every pixel blends the glass twice. Keep thickness small, or switch to Quad for half the cost."
                : "Quads are single surfaces: one blend per pixel, no slab thickness.",
            MessageType.None);

        if (GUILayout.Button("Build / Rebuild Barrier", GUILayout.Height(28)))
            Build();
    }

    void FitToSelection()
    {
        var renderers = Selection.activeGameObject.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return;

        var bounds = renderers[0].bounds;
        foreach (var r in renderers)
            bounds.Encapsulate(r.bounds);

        m_Center = new Vector3(bounds.center.x, m_Center.y, bounds.center.z);
        // Circumscribe the footprint, with a little air so the panes clear the silhouette.
        m_Radius = new Vector2(bounds.extents.x, bounds.extents.z).magnitude * 1.1f;
        m_BottomY = bounds.min.y;
    }

    void Build()
    {
        if (m_Material == null)
        {
            EditorUtility.DisplayDialog("Frosted Glass Barrier", "Assign the frosted glass material first.", "OK");
            return;
        }

        var existing = GameObject.Find(k_RootName);
        if (existing != null)
            Undo.DestroyObjectImmediate(existing);

        var root = new GameObject(k_RootName);
        Undo.RegisterCreatedObjectUndo(root, "Build Glass Barrier");
        root.transform.position = new Vector3(m_Center.x, 0f, m_Center.z);

        float height = Mathf.Abs(m_TopY - m_BottomY);
        float midY = (m_TopY + m_BottomY) * 0.5f;

        // Chord width of one segment, plus 2% so neighbouring panes overlap instead of
        // leaving a hairline gap you can see straight through.
        float paneWidth = 2f * m_Radius * Mathf.Tan(Mathf.PI / m_PaneCount) * 1.02f;

        for (int i = 0; i < m_PaneCount; i++)
        {
            float angle = i * Mathf.PI * 2f / m_PaneCount;
            var outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));

            var pane = m_Shape == PaneShape.FlattenedCube
                ? GameObject.CreatePrimitive(PrimitiveType.Cube)
                : GameObject.CreatePrimitive(PrimitiveType.Quad);

            pane.name = $"GlassPane_{i:00}";
            pane.transform.SetParent(root.transform, false);
            pane.transform.position = new Vector3(m_Center.x, midY, m_Center.z) + outward * m_Radius;
            // Face the centre, so the normal points at the player standing inside the ring.
            pane.transform.rotation = Quaternion.LookRotation(-outward, Vector3.up);
            pane.transform.localScale = m_Shape == PaneShape.FlattenedCube
                ? new Vector3(paneWidth, height, m_Thickness)
                : new Vector3(paneWidth, height, 1f);

            var collider = pane.GetComponent<Collider>();
            if (collider != null)
                DestroyImmediate(collider);

            var renderer = pane.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = m_Material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        Selection.activeGameObject = root;
        EditorGUIUtility.PingObject(root);
    }
}
