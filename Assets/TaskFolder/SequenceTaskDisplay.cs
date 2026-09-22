using UnityEngine;
using UnityEngine.UI;

namespace FishGame
{
    /// <summary>
    /// On-screen text for the sequence task: a status line ("Memorize…" / "Repeat!") and a feedback
    /// pop ("Correct!" / "Wrong!"). Both are plain UI.Text placeholders on the task Canvas — restyle,
    /// reposition, or swap them for TMP in the editor; the task just calls SetStatus/ShowFeedback.
    /// </summary>
    public class SequenceTaskDisplay : MonoBehaviour
    {
        [Header("Status line (memorize / repeat) — editable placeholder")]
        [SerializeField] GameObject statusRoot;
        [SerializeField] Text statusLabel;

        [Header("Feedback pop (correct / wrong) — editable placeholder")]
        [SerializeField] GameObject feedbackRoot;
        [SerializeField] Text feedbackLabel;
        [SerializeField] float feedbackSeconds = 1.5f;

        [Header("Controls hint (backspace / enter) — editable placeholder")]
        [SerializeField] GameObject controlsRoot;
        [SerializeField] Text controlsLabel;

        void Awake() => HideAll();

        /// <summary>Set the persistent status line (null/empty hides it).</summary>
        public void SetStatus(string msg)
        {
            if (statusLabel != null) statusLabel.text = msg;
            if (statusRoot != null) statusRoot.SetActive(!string.IsNullOrEmpty(msg));
        }

        /// <summary>Flash a feedback message that auto-hides after a moment.</summary>
        public void ShowFeedback(string msg, Color color)
        {
            if (feedbackLabel != null) { feedbackLabel.text = msg; feedbackLabel.color = color; }
            if (feedbackRoot != null) feedbackRoot.SetActive(true);
            CancelInvoke(nameof(HideFeedback));
            Invoke(nameof(HideFeedback), feedbackSeconds);
        }

        void HideFeedback() { if (feedbackRoot != null) feedbackRoot.SetActive(false); }

        /// <summary>Set the controls hint line (null/empty hides it).</summary>
        public void SetControls(string msg)
        {
            if (controlsLabel != null) controlsLabel.text = msg;
            if (controlsRoot != null) controlsRoot.SetActive(!string.IsNullOrEmpty(msg));
        }

        public void HideAll()
        {
            SetStatus(null);
            SetControls(null);
            if (feedbackRoot != null) feedbackRoot.SetActive(false);
        }
    }
}
