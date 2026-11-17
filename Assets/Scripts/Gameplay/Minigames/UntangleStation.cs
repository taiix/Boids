using UnityEngine;
using UnityEngine.UIElements;

namespace FishGame
{
    /// <summary>
    /// A buoy snagged in a ball of fishing line. The minigame is a calm puzzle: knots joined by lines -
    /// drag the knots until no two lines cross (red lines cross something, teal ones are clear). With a
    /// controller the right stick moves the picked knot and the confirm button picks the next.
    /// Untangled, the line comes away in neat strands.
    /// </summary>
    public class UntangleStation : MinigameStation
    {
        protected override string Layout => "Untangle";
        protected override string Prompt => "untangle the line";
        protected override string TaskName => "Line untangled";
        protected override float RewardSeconds => 15f;
        protected override bool FreeCursor => true;
        protected override string DoneWord => "FREE!";

        // A drawing with no crossings exists: the knots round a circle in this order.
        static readonly Vector2Int[] Edges =
        {
            new(0, 1), new(1, 2), new(2, 3), new(3, 4), new(4, 5), new(5, 6), new(6, 0),
            new(0, 2), new(0, 4), new(4, 6),
        };
        const int Knots = 7;

        Transform _tangle, _neat;
        KnotBoard _board;
        Label _count;
        float _solvedAt = -1f;

        protected override void BuildLooks()
        {
            var art = MinigameArt.Get();
            float yaw = (transform.position.x * 37f + transform.position.z * 11f) % 360f;
            MinigameArt.FitTop(MinigameArt.Spawn(art.rock, transform, Vector3.zero, Quaternion.Euler(0f, yaw, 0f), 1.3f), 0.42f);   // under the line's dome
            _tangle = new GameObject("Tangle").transform;
            _tangle.SetParent(transform, false);
            _tangle.localRotation = Quaternion.Euler(0f, yaw * 0.5f, 0f);
            if (MinigameArt.Spawn(art.tangle, _tangle, new Vector3(0f, 0.02f, 0f), Quaternion.identity, 1.05f) == null)
            {
                // no prop assigned: the old stand-in
                var rng = new System.Random(Mathf.RoundToInt(transform.position.x * 11f + transform.position.z * 3f));
                for (int i = 0; i < 16; i++)
                {
                    Vector3 a = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble(), (float)rng.NextDouble() - 0.5f) * 1.6f;
                    Vector3 b = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble(), (float)rng.NextDouble() - 0.5f) * 1.6f;
                    Rope(_tangle, a + Vector3.up * 0.3f, b + Vector3.up * 0.3f, art.rope);
                }
            }
            MinigameArt.Spawn(art.floatProp, _tangle, new Vector3(0.2f, 0.42f, 0.12f), Quaternion.Euler(25f, 0f, 12f), 1.3f);
            // freed: the float bobs up off the rock on a short end of line
            _neat = new GameObject("Free").transform;
            _neat.SetParent(transform, false);
            MinigameArt.Spawn(art.floatProp, _neat, new Vector3(0f, 1.4f, 0f), Quaternion.identity, 1.3f);
        }

        void Rope(Transform parent, Vector3 a, Vector3 b, Material m)
        {
            Vector3 d = b - a;
            if (d.sqrMagnitude < 1e-4f) return;
            Part(PrimitiveType.Cylinder, parent, (a + b) * 0.5f, new Vector3(0.035f, d.magnitude * 0.5f, 0.035f), m,
                 Quaternion.FromToRotation(Vector3.up, d.normalized));
        }

        protected override void ShowDone(bool done)
        {
            if (_tangle != null) _tangle.gameObject.SetActive(!done);
            if (_neat != null) _neat.gameObject.SetActive(done);
        }

        protected override void BeginGame(VisualElement ui)
        {
            _board = ui.Q<KnotBoard>("board");
            _count = ui.Q<Label>("count");
            _board.edges = Edges;
            _board.nodes = new Vector2[Knots];
            _board.selected = 0;
            // scramble until it's properly tangled
            for (int tries = 0; tries < 200; tries++)
            {
                for (int i = 0; i < Knots; i++)
                {
                    // spread out: no two knots on top of each other
                    Vector2 p = default;
                    for (int k = 0; k < 30; k++)
                    {
                        p = new Vector2(Random.Range(0.05f, 0.95f), Random.Range(0.05f, 0.95f));
                        bool clear = true;
                        for (int j = 0; j < i; j++) if ((_board.nodes[j] - p).sqrMagnitude < 0.22f * 0.22f) clear = false;
                        if (clear) break;
                    }
                    _board.nodes[i] = p;
                }
                if (_board.Crossings() >= 5) break;
            }
            _board.MarkDirtyRepaint();
            _solvedAt = -1f;
            var drag = ui.Q<Label>("drag-key");
            if (drag != null) drag.text = GameInput.UsingPad ? "R STICK" : "DRAG";
            var quit = ui.Q<Label>("quit-key");
            if (quit != null) quit.text = MinigameHud.KeyText(GameInput.Interact);
            MinigameHud.SetHint(GameInput.UsingPad ? "Right stick moves a knot, A picks the next. No red lines left and it's free"
                                                   : "Drag knots until no line is red");
            UpdateCount();
        }

        void UpdateCount()
        {
            int n = _board.Crossings();
            if (_count != null)
            {
                _count.text = n == 0 ? "CLEAR" : $"{n} CROSSED";
            }
        }

        protected override void TickGame(float dt)
        {
            // controller: move the picked knot with the right stick, pick the next with confirm
            Vector2 stick = GameInput.Look.ReadPad();
            if (GameInput.PuzzleSubmit.WasPressedThisFrame()) { _board.selected = (_board.selected + 1) % Knots; _board.MarkDirtyRepaint(); }
            if (stick.sqrMagnitude > 0.04f && _board.selected >= 0)
            {
                var n = _board.nodes[_board.selected] + new Vector2(stick.x, -stick.y) * dt * 0.6f;
                _board.nodes[_board.selected] = new Vector2(Mathf.Clamp01(n.x), Mathf.Clamp01(n.y));
                _board.MarkDirtyRepaint();
            }
            UpdateCount();

            if (_board.Crossings() == 0)
            {
                if (_solvedAt < 0f) _solvedAt = Time.unscaledTime;
                else if (Time.unscaledTime - _solvedAt > 0.6f) Solve();
            }
            else _solvedAt = -1f;
        }
    }
}
