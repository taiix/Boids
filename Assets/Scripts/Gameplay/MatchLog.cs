using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// Balance log: one CSV per match, per machine, in
    /// <c>%USERPROFILE%/AppData/LocalLow/&lt;company&gt;/&lt;product&gt;/MatchLogs</c> (Application.persistentDataPath).
    /// Every player's machine logs what that player did (minigames started, finished, walked away from, hides);
    /// the host also logs time taken off the clock, completed tasks, deaths and who won.
    ///
    /// Columns: t (seconds since the round started), who (host / client / offline), event, task, seconds, detail.
    /// Collect the files after a playtest and they add up into a balance sheet.
    /// </summary>
    public static class MatchLog
    {
        /// <summary>Turn off to stop writing files.</summary>
        public static bool Enabled = true;

        static bool s_open;
        static float s_roundStart;
        public static string CurrentPath { get; private set; }

        static string Who => Mirror.NetworkServer.active ? "host" : Mirror.NetworkClient.active ? "client" : "offline";

        /// <summary>A new round started: start a new file.</summary>
        public static void BeginRound()
        {
            End();
            if (!Enabled) return;
            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "MatchLogs");
                Directory.CreateDirectory(dir);
                CurrentPath = Path.Combine(dir, $"match_{DateTime.Now:yyyyMMdd_HHmmss}_{Who}.csv");
                // Opened and closed for every line (they're rare), so the file is never left locked -
                // not mid-match, and not when the Editor leaves Play mode without quitting.
                File.WriteAllText(CurrentPath, "t,who,event,task,seconds,detail\n", new UTF8Encoding(false));
                s_open = true;
                s_roundStart = Time.time;
                Write("round_start", "", -1f, $"version={Application.version}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MatchLog] Can't write the match log: {e.Message}");
                s_open = false;
            }
        }

        /// <summary>The round ended with this result: write it and close the file.</summary>
        public static void EndRound(string result)
        {
            Write("round_end", "", Time.time - s_roundStart, result);
            End();
        }

        /// <summary>
        /// One line. <paramref name="seconds"/> is a duration (time spent, time off the clock), -1 for none;
        /// <paramref name="detail"/> is free text (key=value pairs, space separated).
        /// </summary>
        public static void Write(string evt, string task, float seconds = -1f, string detail = "")
        {
            if (!Enabled) return;
            if (!s_open) { BeginRound(); if (!s_open) return; }
            try
            {
                var c = CultureInfo.InvariantCulture;
                File.AppendAllText(CurrentPath, string.Join(",",
                    (Time.time - s_roundStart).ToString("0.0", c), Who, Clean(evt), Clean(task),
                    seconds >= 0f ? seconds.ToString("0.0", c) : "", Clean(detail)) + "\n", new UTF8Encoding(false));
            }
            catch (Exception) { /* a full disk shouldn't break the game */ }
        }

        static string Clean(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace(',', ';').Replace('\n', ' ');

        static void End() => s_open = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            End();
            CurrentPath = null;
            Application.quitting -= End;
            Application.quitting += End;
        }
    }
}
