using UnityEngine;
using UnityEngine.UIElements;

namespace FishGame
{
    /// <summary>
    /// A bleached coral smothered in algae. The minigame: scrub it by tracing circles with the mouse (or the
    /// right stick) - at a steady pace the algae comes off and the coral's colour returns; go too fast and
    /// you stir up a sand cloud that hides it, and if the cloud gets too thick you lose some of the work.
    /// </summary>
    public class CoralScrubStation : MinigameStation
    {
        protected override string Layout => "Coral";
        protected override string Prompt => "scrub the coral";
        protected override string TaskName => "Coral cleaned";
        protected override float RewardSeconds => 15f;
        protected override bool TakesMouse => true;
        protected override float TriggerRadius => 3f;
        protected override string DoneWord => "SPOTLESS";

        const float ScrubSeconds = 9f;       // steady scrubbing for this long cleans it
        const float MaxRate = 14f;           // rad/s that reads as 1 on the speed dial
        const float GoodMin = 0.22f, GoodMax = 0.7f, TooFast = 0.85f;

        static readonly Color Algae = new(0.5f, 0.56f, 0.34f);   // multiplied over the coral's own colours

        Renderer[] _branches;

        ScrubPad _pad;
        Vector2 _cursor = Vector2.up;
        float _angle, _speed01, _clean, _sand, _stunUntil, _turn = 1f;

        protected override void BuildLooks()
        {
            var art = MinigameArt.Get();
            var root = new GameObject("Coral").transform;
            root.SetParent(transform, false);
            var rng = new System.Random(GetInstanceIdSafe());
            MinigameArt.FitTop(MinigameArt.Spawn(art.flatRock != null ? art.flatRock : art.rock, root, Vector3.zero, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), 1.4f), 0.28f);
            var list = new System.Collections.Generic.List<Renderer>();
            if (art.corals != null && art.corals.Length > 0)
            {
                // a little garden over the top of the rock: bigger ones in the middle
                for (int i = 0; i < 6; i++)
                {
                    float a = i * 2.4f + (float)rng.NextDouble(), r = i == 0 ? 0f : 0.3f + (float)rng.NextDouble() * 0.35f;
                    var pos = new Vector3(Mathf.Cos(a) * r, 0.22f - r * r * 0.35f, Mathf.Sin(a) * r * 0.9f);
                    var prefab = art.corals[(i + rng.Next(art.corals.Length)) % art.corals.Length];
                    float scale = (i == 0 ? 0.55f : 0.42f) + (float)rng.NextDouble() * 0.12f;   // reef-pack corals are 1-2 m
                    var c = MinigameArt.Spawn(prefab, root, pos, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), scale);
                    if (c != null) list.AddRange(c.GetComponentsInChildren<Renderer>());
                }
            }
            _branches = list.ToArray();
            Paint(0f);
        }

        int GetInstanceIdSafe() => Mathf.RoundToInt(transform.position.x * 13f + transform.position.z * 7f);

        // How clean it looks (0 = bleached and smothered, 1 = healthy).
        // Algae-murky (dark, green-brown) to its own colours as it's scrubbed.
        void Paint(float clean)
        {
            if (_branches == null) return;
            var c = Color.Lerp(Algae, Color.white, clean * clean);
            foreach (var r in _branches) MinigameArt.Tint(r, c);
        }

        protected override void ShowDone(bool done) => Paint(done ? 1f : 0f);

        protected override void BeginGame(VisualElement ui)
        {
            _pad = ui.Q<ScrubPad>("pad");
            var key = ui.Q<Label>("scrub-key");
            if (key != null) key.text = GameInput.UsingPad ? "R STICK" : "MOUSE";
            var quit = ui.Q<Label>("quit-key");
            if (quit != null) quit.text = MinigameHud.KeyText(GameInput.Interact);
            _clean = 0f; _sand = 0f; _speed01 = 0f; _stunUntil = 0f;
            _cursor = Vector2.up; _angle = Mathf.Atan2(_cursor.y, _cursor.x);
            MinigameHud.SetHint("Keep the trail orange. Too fast and you kick up sand");
        }

        protected override void EndGame()
        {
            if (!Done) Paint(0f);
        }

        protected override void TickGame(float dt)
        {
            // Where the scrubber is: the mouse pushes it round the ring; the stick points it.
            Vector2 mouse = GameInput.Look.ReadPointer();
            Vector2 stick = GameInput.Look.ReadPad();
            if (stick.sqrMagnitude > 0.25f) _cursor = new Vector2(stick.x, -stick.y).normalized;
            else if (mouse.sqrMagnitude > 0.01f) _cursor = (_cursor + new Vector2(mouse.x, -mouse.y) * 0.012f).normalized;

            float a = Mathf.Atan2(_cursor.y, _cursor.x);
            float da = Mathf.DeltaAngle(_angle * Mathf.Rad2Deg, a * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            _angle = a;
            if (Mathf.Abs(da) > 1e-4f) _turn = Mathf.Sign(da);
            float rate = Mathf.Abs(da) / Mathf.Max(dt, 1e-4f);
            _speed01 = Mathf.Lerp(_speed01, Mathf.Clamp01(rate / MaxRate), 1f - Mathf.Exp(-8f * dt));

            bool stunned = Time.unscaledTime < _stunUntil;
            if (!stunned && _speed01 >= GoodMin && _speed01 <= GoodMax && _sand < 0.6f)
                _clean = Mathf.Min(1f, _clean + dt / ScrubSeconds);
            if (_speed01 > TooFast) _sand += dt * 1.1f;
            else _sand = Mathf.Max(0f, _sand - dt * 0.35f);
            if (_sand >= 1f && !stunned)
            {
                _stunUntil = Time.unscaledTime + 1.5f;
                _clean = Mathf.Max(0f, _clean - 0.08f);
                MinigameHud.Flash("SAND!", false);
            }
            _sand = Mathf.Min(_sand, 1f);

            _pad.cursor = _cursor;
            _pad.speed01 = _speed01;
            _pad.goodMin = GoodMin; _pad.goodMax = GoodMax;
            _pad.clean = _clean;
            _pad.sand = _sand;
            _pad.turn = _turn;
            _pad.MarkDirtyRepaint();
            Paint(_clean);

            if (_clean >= 1f) Solve();
        }
    }
}
