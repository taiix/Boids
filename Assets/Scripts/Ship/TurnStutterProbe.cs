using System.Collections.Generic;
using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// Editor-only: samples frame time and the camera's own per-frame motion, so "it stutters
    /// when turning" can be pinned on the renderer or on the camera rig. Recorded in LateUpdate
    /// after the rig has run, so the camera samples are what actually gets drawn.
    /// </summary>
    public class TurnStutterProbe : MonoBehaviour
    {
        public Transform cam;
        public Transform ship;

        readonly List<float> dt = new();
        readonly List<Vector3> camPos = new();
        readonly List<Quaternion> camRot = new();
        bool recording;

        void LateUpdate()
        {
            if (!recording) return;
            dt.Add(Time.unscaledDeltaTime);
            if (cam != null) { camPos.Add(cam.position); camRot.Add(cam.rotation); }
        }

        public void StartRecording()
        {
            dt.Clear(); camPos.Clear(); camRot.Clear();
            recording = true;
        }

        public string Report(string label)
        {
            recording = false;
            if (dt.Count < 20) return label + ": no samples";

            var sorted = new List<float>(dt);
            sorted.Sort();
            float median = sorted[sorted.Count / 2];

            int spikes = 0;
            foreach (var t in dt) if (t > median * 2f) spikes++;

            // Second difference of camera position and of look direction: the honest measure of
            // whether the view itself is moving smoothly, independent of what it is looking at.
            float posD2 = 0f, maxPosD2 = 0f;
            for (int i = 2; i < camPos.Count; i++)
            {
                float v = (camPos[i] - 2f * camPos[i - 1] + camPos[i - 2]).magnitude;
                posD2 += v;
                if (v > maxPosD2) maxPosD2 = v;
            }
            if (camPos.Count > 2) posD2 /= (camPos.Count - 2);

            float angD2 = 0f, maxAngD2 = 0f;
            for (int i = 2; i < camRot.Count; i++)
            {
                float a1 = Quaternion.Angle(camRot[i - 2], camRot[i - 1]);
                float a2 = Quaternion.Angle(camRot[i - 1], camRot[i]);
                float v = Mathf.Abs(a2 - a1);
                angD2 += v;
                if (v > maxAngD2) maxAngD2 = v;
            }
            if (camRot.Count > 2) angD2 /= (camRot.Count - 2);

            return $"{label}: frames={dt.Count} median={median * 1000f:0.0}ms " +
                   $"p95={sorted[(int)(sorted.Count * 0.95f)] * 1000f:0.0}ms " +
                   $"worst={sorted[sorted.Count - 1] * 1000f:0.0}ms spikes>2x={spikes} " +
                   $"| camPos mean|d2|={posD2 * 1000f:0.00}mm max={maxPosD2 * 1000f:0.0}mm " +
                   $"| camAng mean|d2|={angD2:0.0000}deg max={maxAngD2:0.000}deg";
        }
    }
}
