using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Copies the project's steam_appid.txt next to the built executable. Until the game has its own App ID
/// on Steam, SteamAPI.Init() reads the ID (480 = Spacewar) from that file in the working directory -
/// without it Steam fails to start in the build and no lobby can be hosted or joined.
/// </summary>
class SteamAppIdPostBuild : IPostprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platformGroup != BuildTargetGroup.Standalone) return;

        string source = Path.Combine(Directory.GetCurrentDirectory(), "steam_appid.txt");
        if (!File.Exists(source))
        {
            Debug.LogWarning("[Build] No steam_appid.txt in the project folder - Steam won't initialise in this build.");
            return;
        }

        string target = Path.Combine(Path.GetDirectoryName(report.summary.outputPath), "steam_appid.txt");
        File.Copy(source, target, overwrite: true);
        Debug.Log($"[Build] Copied steam_appid.txt next to the executable ({target}).");
    }
}
