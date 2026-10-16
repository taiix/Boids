using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// FishGame > Build Sound Library: fills Resources/Audio/SoundLibrary from the files in Assets/Audio (by
    /// name, see <see cref="Map"/>) and sets their import settings - music streamed, short effects decompressed
    /// on load, world effects forced to mono so they place properly in 3D.
    /// </summary>
    public static class GameAudioAssets
    {
        const string Root = "Assets/Audio";
        const string LibraryPath = "Assets/Resources/Audio/SoundLibrary.asset";

        // Library field -> file(s) under Assets/Audio (no extension). Several files make a random set.
        static readonly Dictionary<string, string[]> Map = new Dictionary<string, string[]>
        {
            { "musicMenu", new[] { "Music/Music_Menu" } },
            { "musicRound", new[] { "Music/Music_Round" } },
            { "musicEscape", new[] { "Music/Music_Escape" } },
            { "jingleRoundStart", new[] { "Jingles/Jingle_RoundStart" } },
            { "jingleEscaped", new[] { "Jingles/Jingle_Escaped" } },
            { "jingleFishWin", new[] { "Jingles/Jingle_FishWin" } },
            { "jingleSharkWin", new[] { "Jingles/Jingle_SharkWin" } },
            { "jingleLost", new[] { "Jingles/Jingle_Lost" } },
            { "uiClick", new[] { "Ui/Ui_Click" } },
            { "uiBack", new[] { "Ui/Ui_Back" } },
            { "uiConfirm", new[] { "Ui/Ui_Confirm" } },
            { "uiAnnounce", new[] { "Ui/Ui_Announce" } },
            { "uiWarning", new[] { "Ui/Ui_Warning" } },
            { "bites", new[] { "Shark/Bite_1", "Shark/Bite_2", "Shark/Bite_3" } },
            { "swishes", new[] { "Shark/Swish_1", "Shark/Swish_2", "Shark/Swish_3" } },
            { "lunge", new[] { "Shark/Lunge" } },
            { "sonar", new[] { "Shark/Sonar" } },
            { "devour", new[] { "Shark/Devour" } },
            { "scream", new[] { "Fish/Scream" } },
            { "heartbeat", new[] { "Fish/Heartbeat" } },
            { "graze", new[] { "Fish/Graze" } },
            { "breakFree", new[] { "Fish/BreakFree" } },
            { "pellet", new[] { "Fish/Pellet" } },
            { "splashes", new[] { "Water/Splash_1", "Water/Splash_2", "Water/Splash_3", "Water/Splash_4" } },
            { "bubbles", new[] { "Water/Bubbles_1", "Water/Bubbles_2", "Water/Bubbles_3" } },
            { "swim", new[] { "Water/Swim" } },
            { "ambientBubbles", new[] { "Water/AmbientBubbles" } },
            { "taskDoneShark", new[] { "Alerts/TaskDoneShark" } },
            { "taskDoneFish", new[] { "Alerts/TaskDoneFish" } },
            { "exitOpen", new[] { "Alerts/ExitOpen" } },
            { "rumble", new[] { "Alerts/Rumble" } },
            { "ghost", new[] { "Alerts/Ghost" } },
            { "ghostHint", new[] { "Alerts/GhostHint" } },
            { "currentLoop", new[] { "Events/CurrentLoop" } },
            { "trawlerEngine", new[] { "Events/TrawlerEngine" } },
            { "netCatch", new[] { "Events/NetCatch" } },
            { "nightfallOnset", new[] { "Events/NightfallOnset" } },
            { "rainLoop", new[] { "Events/RainLoop" } },
            { "thunder", new[] { "Events/Thunder" } },
            { "coldOnset", new[] { "Events/ColdOnset" } },
            { "sting", new[] { "Events/Sting" } },
            { "stingHit", new[] { "Events/StingHit" } },
            { "quake", new[] { "Events/Quake" } },
            { "rumbleLoop", new[] { "Events/RumbleLoop" } },
            { "tideOnset", new[] { "Events/TideOnset" } },
            { "tideBurn", new[] { "Events/TideBurn" } },
        };

        [MenuItem("FishGame/Build Sound Library")]
        public static void Build()
        {
            ConfigureImports();

            Directory.CreateDirectory(Path.GetDirectoryName(LibraryPath));
            var lib = AssetDatabase.LoadAssetAtPath<SoundLibrary>(LibraryPath);
            if (lib == null)
            {
                lib = ScriptableObject.CreateInstance<SoundLibrary>();
                AssetDatabase.CreateAsset(lib, LibraryPath);
            }

            var so = new SerializedObject(lib);
            int missing = 0;
            foreach (var kv in Map)
            {
                var prop = so.FindProperty(kv.Key);
                if (prop == null) { Debug.LogWarning($"[Audio] SoundLibrary has no field '{kv.Key}'."); continue; }
                var clips = new List<AudioClip>();
                foreach (var f in kv.Value)
                {
                    var clip = Load(f);
                    if (clip != null) clips.Add(clip);
                    else { missing++; Debug.LogWarning($"[Audio] Missing {Root}/{f}"); }
                }
                if (prop.isArray)
                {
                    prop.arraySize = clips.Count;
                    for (int i = 0; i < clips.Count; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
                }
                else prop.objectReferenceValue = clips.Count > 0 ? clips[0] : null;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Audio] Sound library built ({Map.Count} entries, {missing} missing files).");
        }

        static AudioClip Load(string name)
        {
            foreach (var ext in new[] { ".ogg", ".mp3", ".wav" })
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{Root}/{name}{ext}");
                if (clip != null) return clip;
            }
            return null;
        }

        // Music streams (long, played one at a time); effects load decompressed so they start instantly;
        // anything placed in the world is mono so it pans and fades with distance.
        static void ConfigureImports()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { Root }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is AudioImporter imp)) continue;
                bool music = path.Contains("/Music/");
                bool flat = music || path.Contains("/Jingles/") || path.Contains("/Ui/");
                var s = imp.defaultSampleSettings;
                s.loadType = music ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = music ? 0.6f : 0.7f;
                imp.defaultSampleSettings = s;
                imp.forceToMono = !flat;
                imp.loadInBackground = music;
                imp.SaveAndReimport();
            }
        }
    }
}
