namespace FishGame
{
    /// <summary>
    /// Who made the sound and music the game uses, shown on the main menu's Credits screen. Keep it in step
    /// with Assets/Audio/CREDITS.md, which also lists the source pages and what each file is used for.
    /// Everything here is free for commercial use; only the CC-BY item legally requires credit, but every
    /// author is credited.
    /// </summary>
    public static class GameCredits
    {
        /// <summary>One section per heading: (heading, lines).</summary>
        public static readonly (string heading, string[] lines)[] Sections =
        {
            ("MUSIC", new[]
            {
                "\"Underwater\" - Cleyton Kauffman (soundcloud.com/cleytonkauffman) - CC0",
                "\"Nautilus\" - poinl - CC0",
                "\"Sirens in Darkness\" - The Cynic Project / cynicmusic.com / pixelsphere.org - CC0",
                "Music Jingles - Kenney (kenney.nl) - CC0",
            }),
            ("SOUND EFFECTS", new[]
            {
                "Interface Sounds, Impact Sounds, Sci-fi Sounds - Kenney (kenney.nl) - CC0",
                "Bubble Sound Effects - Brian MacIntosh (BMacZero) - CC0",
                "Skippy Fish Water Sound Collection - jcpmcdonald - CC0",
                "Underwater or space engine rumble - gmason - CC0",
                "40 CC0 water / splash / slime SFX - rubberduck - CC0",
                "Electricity Game Sound Pack - faxcorp - CC0",
                "Ghost breath - qubodup - CC0",
                "Earthquake - mieki256 - CC0",
                "Sonar Ping - Spiceman - CC0",
                "7 Eating Crunches - StarNinjas - CC0",
                "Swishes Sound Pack, Freeze Spell - artisticdude - CC0",
                "Heartbeat sounds - bart - CC0",
                "Acid burn sounds - mikhog - CC-BY 3.0 (creativecommons.org/licenses/by/3.0)",
            }),
            ("", new[]
            {
                "Sound and music found on OpenGameArt.org and Kenney.nl.",
                "Thank you to everyone who shares their work for free.",
            }),
        };
    }
}
