var sb = new System.Text.StringBuilder();
foreach (var tt in UnityEngine.Object.FindObjectsByType<FishGame.TurtleRescueTask>(UnityEngine.FindObjectsSortMode.None))
{
    sb.Append("task " + tt.name + " @" + tt.transform.position.ToString("F0") + " active=" + tt.isActiveAndEnabled + "; ");
    foreach (var f in typeof(FishGame.TurtleRescueTask).GetFields(BF))
        if (typeof(UnityEngine.Component).IsAssignableFrom(f.FieldType) || f.FieldType == typeof(UnityEngine.GameObject) || f.FieldType == typeof(UnityEngine.Vector3))
        {
            var v = f.GetValue(tt);
            string at = v is UnityEngine.Component c && c != null ? c.transform.position.ToString("F0") : v is UnityEngine.GameObject g && g != null ? g.transform.position.ToString("F0") : v is UnityEngine.Vector3 p ? p.ToString("F0") : "null";
            sb.Append(f.Name + "=" + at + " ");
        }
}
foreach (var sw in UnityEngine.Object.FindObjectsByType<FishGame.TurtleSwimmer>(UnityEngine.FindObjectsSortMode.None))
    sb.Append(" | swimmer " + sw.name + " @" + sw.transform.position.ToString("F0") + " ground " + Ground(sw.transform.position).ToString("F1"));
return sb.ToString();
