using UnityEngine.UIElements;

namespace FishGame
{
    // HUD labels whose wording changes with what's going on. Each one's own text is how it usually reads; the
    // other wordings are set beside it in the layout (Resources/Hud/MatchHud.uxml - select the label in the UI
    // Builder and they're in its Inspector), so none of it lives in code. MatchHud picks which to show.

    /// <summary>The word over the survival clock: its text during the round, and these the rest of the time.</summary>
    [UxmlElement]
    public partial class HudClockLabel : Label
    {
        [UxmlAttribute] public string warmupText = "DIVE STARTING";
        [UxmlAttribute] public string overText = "ROUND OVER";
        [UxmlAttribute] public string noMatchText = "NO MATCH";
    }

    /// <summary>The hunger bar's name: its text normally, this once hunger has run out.</summary>
    [UxmlElement]
    public partial class HudHungerLabel : Label
    {
        [UxmlAttribute] public string starvingText = "STARVING";
    }

    /// <summary>The voice panel's word: its text while you're quiet, these the rest of the time.</summary>
    [UxmlElement]
    public partial class HudVoiceLabel : Label
    {
        [UxmlAttribute] public string talkingText = "TALKING";
        [UxmlAttribute] public string micOffText = "MIC OFF";
        [UxmlAttribute] public string noMicText = "NO MIC";
        [UxmlAttribute] public string sharkHeardText = "SHARK HEARD YOU";
        [UxmlAttribute] public string ghostText = "GHOST CHANNEL";
        [UxmlAttribute] public string ghostTalkingText = "TALKING TO GHOSTS";
    }
}
