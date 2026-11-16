// Where are we? Play state, active scene, local body, role, match phase, frame count.
var sb = new System.Text.StringBuilder();
sb.Append("playing=" + UnityEditor.EditorApplication.isPlaying);
sb.Append(" scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
sb.Append(" frame=" + UnityEngine.Time.frameCount);
sb.Append(" bg=" + UnityEngine.Application.runInBackground);
if (UnityEditor.EditorApplication.isPlaying)
{
    FishPlayer me = null;
    foreach (var fp in FishPlayer.All) if (fp != null && fp.isLocalPlayer) me = fp;
    sb.Append(" body=" + (me == null ? "none" : (me.GetComponent<FishGame.SharkAbilities>() != null ? "shark" : "fish") + "@" + me.transform.position.ToString("F0")));
    var m = FishGame.MatchManager.Instance;
    if (m != null) sb.Append(" phase=" + m.Phase + " left=" + m.RemainingSeconds.ToString("0") + " event=" + m.Event);
    var clip = FishGame.GameplayClip.Current;
    if (clip != null) sb.Append(" clip=" + clip.Status);
}
return sb.ToString();

