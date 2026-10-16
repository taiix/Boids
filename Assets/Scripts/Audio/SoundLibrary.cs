using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// Every sound and piece of music the game plays, in one asset so any of them can be swapped in the
    /// Inspector. Lives at Resources/Audio/SoundLibrary; filled from Assets/Audio by FishGame > Build Sound
    /// Library. Where the clips came from (and their licences) is in Assets/Audio/CREDITS.md and on the
    /// main menu's Credits screen (<see cref="GameCredits"/>).
    /// </summary>
    [CreateAssetMenu(menuName = "FishGame/Sound Library", fileName = "SoundLibrary")]
    public class SoundLibrary : ScriptableObject
    {
        [Header("Music")]
        public AudioClip musicMenu;
        public AudioClip musicRound;
        public AudioClip musicEscape;

        [Header("Jingles")]
        public AudioClip jingleRoundStart;
        public AudioClip jingleEscaped;
        public AudioClip jingleFishWin;
        public AudioClip jingleSharkWin;
        public AudioClip jingleLost;

        [Header("Interface")]
        public AudioClip uiClick;
        public AudioClip uiBack;
        public AudioClip uiConfirm;
        public AudioClip uiAnnounce;
        public AudioClip uiWarning;

        [Header("Shark")]
        public AudioClip[] bites;
        public AudioClip[] swishes;
        public AudioClip lunge;
        public AudioClip sonar;
        public AudioClip devour;

        [Header("Fish")]
        public AudioClip scream;
        public AudioClip heartbeat;
        public AudioClip graze;
        public AudioClip breakFree;
        public AudioClip pellet;

        [Header("Water")]
        public AudioClip[] splashes;
        public AudioClip[] bubbles;
        public AudioClip swim;
        public AudioClip ambientBubbles;

        [Header("Alerts")]
        public AudioClip taskDoneShark;
        public AudioClip taskDoneFish;
        public AudioClip exitOpen;
        public AudioClip rumble;
        public AudioClip ghost;
        public AudioClip ghostHint;

        [Header("World events")]
        public AudioClip currentLoop;
        public AudioClip trawlerEngine;
        public AudioClip netCatch;
        public AudioClip nightfallOnset;
        public AudioClip rainLoop;
        public AudioClip thunder;
        public AudioClip coldOnset;
        public AudioClip sting;
        public AudioClip stingHit;
        public AudioClip quake;
        public AudioClip rumbleLoop;
        public AudioClip tideOnset;
        public AudioClip tideBurn;

        static SoundLibrary s_library;
        static bool s_looked;

        /// <summary>The library, or null if it hasn't been built (sounds are then simply skipped).</summary>
        public static SoundLibrary Get()
        {
            if (!s_looked)
            {
                s_library = Resources.Load<SoundLibrary>("Audio/SoundLibrary");
                s_looked = true;
            }
            return s_library;
        }
    }
}
