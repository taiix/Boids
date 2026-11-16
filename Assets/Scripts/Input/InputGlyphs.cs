using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace FishGame
{
    /// <summary>
    /// Prompts that show the right button for whatever the player is holding. A key tile (a Label with the
    /// "key" look) bound here reads "E" on keyboard; on a controller it becomes Steam's glyph for the button
    /// the action is on in the player's own layout - the right picture for an Xbox pad, a DualSense, a Steam
    /// Deck - and changes the moment they switch device or rebind.
    /// </summary>
    public static class InputGlyphs
    {
        class Bound
        {
            public Label tile;
            public GameAction action;
            public InputSet set;
            public string keyText;
            public float keyHeight;   // the tile's own height, measured while it's a key
            public bool glyphOn, attached;
        }

        static readonly List<Bound> s_bound = new List<Bound>();
        static int s_version = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_bound.Clear();
            s_version = -1;
        }

        /// <summary>The button's name in words, for text that can't hold a picture: "E" / "X Button".</summary>
        public static string Name(GameAction action, InputSet? set = null)
        {
            if (!GameInput.UsingPad) return action.KeyLabel;
            return SteamInputService.ButtonName(action, set ?? action.HomeSet) ?? action.PadLabel;
        }

        /// <summary>Keep a key tile showing this action's button. Its text on keyboard is
        /// <paramref name="keyText"/> (default: the action's own key).</summary>
        public static void Bind(Label tile, GameAction action, InputSet? set = null, string keyText = null)
        {
            if (tile == null || action == null) return;
            var b = s_bound.Find(x => x.tile == tile);
            if (b == null) { b = new Bound { tile = tile }; s_bound.Add(b); }
            b.action = action;
            b.set = set ?? action.HomeSet;
            b.keyText = keyText ?? action.KeyLabel;
            Apply(b);
        }

        /// <summary>A new key tile for this action (class "key" plus any extra), kept up to date.</summary>
        public static Label Tile(GameAction action, InputSet? set = null, params string[] extraClasses)
        {
            var tile = new Label { pickingMode = PickingMode.Ignore };
            tile.AddToClassList("key");
            foreach (var c in extraClasses) tile.AddToClassList(c);
            Bind(tile, action, set);
            return tile;
        }

        internal static void Tick()
        {
            if (s_version == GameInput.GlyphVersion) return;
            s_version = GameInput.GlyphVersion;
            // Tiles whose screen is gone (scene unloaded, prompt rewritten) drop out here.
            s_bound.RemoveAll(Gone);
            foreach (var b in s_bound) Apply(b);
        }

        static bool Gone(Bound b)
        {
            if (b.tile == null) return true;
            if (b.tile.panel != null) return false;
            return b.attached || b.tile.parent == null;
        }

        static void Apply(Bound b)
        {
            var tile = b.tile;
            if (tile.panel != null) b.attached = true;
            if (!b.glyphOn)
            {
                float measured = tile.resolvedStyle.height;
                if (!float.IsNaN(measured) && measured >= 8f) b.keyHeight = measured;
            }

            var glyph = GameInput.UsingPad ? SteamInputService.Glyph(b.action, b.set) : null;
            if (glyph != null)
            {
                // The picture replaces the tile: no box, no letter, a square a little bigger than the key was.
                float size = Mathf.Round((b.keyHeight > 0f ? b.keyHeight : 34f) * 1.2f);
                b.glyphOn = true;
                tile.text = "";
                tile.style.backgroundImage = new StyleBackground(glyph);
                tile.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                tile.style.backgroundColor = Color.clear;
                tile.style.borderTopWidth = tile.style.borderBottomWidth = tile.style.borderLeftWidth = tile.style.borderRightWidth = 0;
                tile.style.width = tile.style.minWidth = size;
                tile.style.height = tile.style.minHeight = size;
                tile.style.paddingLeft = tile.style.paddingRight = tile.style.paddingTop = tile.style.paddingBottom = 0;
                tile.style.unityBackgroundImageTintColor = Color.white;
            }
            else
            {
                b.glyphOn = false;
                tile.text = GameInput.UsingPad ? b.action.PadLabel : b.keyText;
                tile.style.backgroundImage = StyleKeyword.Null;
                tile.style.backgroundSize = StyleKeyword.Null;
                tile.style.backgroundColor = StyleKeyword.Null;
                tile.style.borderTopWidth = tile.style.borderBottomWidth = tile.style.borderLeftWidth = tile.style.borderRightWidth = StyleKeyword.Null;
                tile.style.width = tile.style.minWidth = StyleKeyword.Null;
                tile.style.height = tile.style.minHeight = StyleKeyword.Null;
                tile.style.paddingLeft = tile.style.paddingRight = tile.style.paddingTop = tile.style.paddingBottom = StyleKeyword.Null;
                tile.style.unityBackgroundImageTintColor = StyleKeyword.Null;
            }
        }
    }
}
