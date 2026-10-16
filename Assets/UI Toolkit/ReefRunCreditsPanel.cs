using FishGame;
using UnityEngine.UIElements;

namespace ReefRun
{
    /// <summary>
    /// The main menu's Credits overlay: who made the game's sound and music (<see cref="GameCredits"/>), in
    /// the settings overlay's look. Built in code on top of the menu's root; ✕ or a click outside the card
    /// closes it.
    /// </summary>
    public class ReefRunCreditsPanel
    {
        readonly VisualElement _overlay;

        public ReefRunCreditsPanel(VisualElement root)
        {
            _overlay = new VisualElement();
            _overlay.AddToClassList("mm-settings-overlay");
            _overlay.style.display = DisplayStyle.None;
            _overlay.RegisterCallback<ClickEvent>(e => { if (e.target == _overlay) Close(); });

            var card = new VisualElement();
            card.AddToClassList("mm-settings-card");
            card.AddToClassList("mm-glass");
            card.style.width = 760;
            card.style.maxHeight = new Length(85, LengthUnit.Percent);
            _overlay.Add(card);

            var head = new VisualElement();
            head.AddToClassList("mm-settings-head");
            var title = new Label("CREDITS");
            title.AddToClassList("mm-settings-title");
            var close = new Button(Close) { name = "credits-close", text = "✕" };
            close.AddToClassList("mm-close-btn");
            head.Add(title);
            head.Add(close);
            card.Add(head);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            card.Add(scroll);
            foreach (var (heading, lines) in GameCredits.Sections)
            {
                if (!string.IsNullOrEmpty(heading))
                {
                    var h = new Label(heading);
                    h.AddToClassList("mm-section-label");
                    scroll.Add(h);
                }
                foreach (var line in lines)
                {
                    var l = new Label(line);
                    l.style.whiteSpace = WhiteSpace.Normal;
                    l.style.fontSize = 14;
                    l.style.marginBottom = 5;
                    if (string.IsNullOrEmpty(heading)) { l.style.marginTop = 10; l.style.opacity = 0.7f; }
                    scroll.Add(l);
                }
            }

            root.Add(_overlay);
        }

        public void Open()
        {
            _overlay.style.display = DisplayStyle.Flex;
            _overlay.BringToFront();
            _overlay.schedule.Execute(() => _overlay.AddToClassList("show")).StartingIn(16);
        }

        public void Close()
        {
            _overlay.RemoveFromClassList("show");
            _overlay.schedule.Execute(() => _overlay.style.display = DisplayStyle.None).StartingIn(240);
        }
    }
}
