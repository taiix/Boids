using UnityEngine;
using UnityEngine.UIElements;

namespace FishGame
{
    /// <summary>
    /// The trash minigame's gauge: a grey arc, a yellow line straight up the middle, a red needle that
    /// sweeps from end to end and one orange box on each side of the line. Drawn with Painter2D, so it
    /// scales with its box (size it in TrashMinigame.uss). Values are set by TrashMinigameUI every frame.
    /// Positions are -1 (left end of the arc) to 1 (right end).
    /// </summary>
    [UxmlElement]
    public partial class TrashGauge : VisualElement
    {
        public enum BoxState { Ready, Hit, Missed, Spent }

        float _needle, _left = -0.55f, _right = 0.55f, _boxHalf = 0.09f;

        [UxmlAttribute] public float needle { get => _needle; set { _needle = Mathf.Clamp(value, -1f, 1f); MarkDirtyRepaint(); } }
        [UxmlAttribute] public float leftBox { get => _left; set { _left = value; MarkDirtyRepaint(); } }
        [UxmlAttribute] public float rightBox { get => _right; set { _right = value; MarkDirtyRepaint(); } }
        /// <summary>Half a box's width, in the same -1..1 units.</summary>
        [UxmlAttribute] public float boxHalf { get => _boxHalf; set { _boxHalf = Mathf.Max(0.01f, value); MarkDirtyRepaint(); } }

        public BoxState leftState { get; set; }
        public BoxState rightState { get; set; }
        /// <summary>The box to hit next: -1 left, 1 right, 0 none. It glows and gets an arrow; the other dims.</summary>
        public int activeSide { get; set; }
        /// <summary>0-1 flash after a box changes state (hit / miss), set by the controller.</summary>
        public float leftFlash { get; set; }
        public float rightFlash { get; set; }

        [UxmlAttribute] public float halfAngle { get; set; } = 55f;     // degrees each side of straight up
        [UxmlAttribute] public Color trackColor { get; set; } = new(0.561f, 0.69f, 0.733f, 0.55f);   // HUD --dim
        [UxmlAttribute] public Color boxColor { get; set; } = new(1f, 0.722f, 0.302f);           // HUD --orange #ffb84d
        [UxmlAttribute] public Color hitColor { get; set; } = new(0.173f, 0.765f, 0.769f);       // HUD --teal-l #2cc3c4
        [UxmlAttribute] public Color missColor { get; set; } = new(0.851f, 0.251f, 0.251f);      // HUD --red #d94040
        [UxmlAttribute] public Color targetColor { get; set; } = new(0.902f, 0.702f, 0.2f);      // HUD --amber #e6b333
        [UxmlAttribute] public Color needleColor { get; set; } = new(0.851f, 0.251f, 0.251f);    // HUD --red

        public TrashGauge()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext ctx)
        {
            float w = contentRect.width, h = contentRect.height;
            if (w < 4f || h < 4f) return;

            // Circle centre sits below the box so only the top cap of the circle shows.
            float half = halfAngle * Mathf.Deg2Rad;
            float r = Mathf.Min(w * 0.5f / Mathf.Sin(half) * 0.9f, h * 0.78f / (1f - Mathf.Cos(half) + 0.22f));
            var c = new Vector2(w * 0.5f, h * 0.3f + r);   // room above the arc for the yellow line and arrows
            float thick = Mathf.Max(3f, r * 0.016f);
            var p = ctx.painter2D;

            float Deg(float t) => -90f + t * halfAngle;   // t in [-1,1] -> UI angle (0 = right, -90 = up)
            Vector2 At(float t, float radius) { float a = Deg(t) * Mathf.Deg2Rad; return c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius; }

            // backing: a dark band following the arc (no panel behind the gauge), with room for the boxes
            p.lineCap = LineCap.Butt;
            p.strokeColor = new Color(0.02f, 0.09f, 0.16f, 0.6f);
            p.lineWidth = r * 0.26f;
            p.BeginPath();
            p.Arc(c, r + r * 0.03f, Deg(-1.06f), Deg(1.06f));
            p.Stroke();

            // the arc
            p.lineCap = LineCap.Round;
            p.strokeColor = trackColor;
            p.lineWidth = thick;
            p.BeginPath();
            p.Arc(c, r, Deg(-1f), Deg(1f));
            p.Stroke();

            // orange boxes: outlined, straddling the arc, leaning with it
            DrawBox(p, c, r, thick, _left, leftState, leftFlash, activeSide == -1, Deg, At);
            DrawBox(p, c, r, thick, _right, rightState, rightFlash, activeSide == 1, Deg, At);

            // yellow line through the middle
            p.lineCap = LineCap.Round;
            p.strokeColor = targetColor;
            p.lineWidth = thick * 1.2f;
            p.BeginPath();
            p.MoveTo(At(0f, r - r * 0.1f));
            p.LineTo(At(0f, r + r * 0.16f));
            p.Stroke();

            // red needle: a curved blade crossing the arc
            Vector2 tip = At(_needle, r + r * 0.17f), root = At(_needle, r - r * 0.09f);
            Vector2 dir = (tip - root).normalized, side = new(-dir.y, dir.x);
            float bw = thick * 2f;
            p.fillColor = needleColor;
            p.BeginPath();
            p.MoveTo(tip);
            p.LineTo(Vector2.Lerp(root, tip, 0.35f) + side * bw);
            p.LineTo(root);
            p.LineTo(Vector2.Lerp(root, tip, 0.35f) - side * bw * 0.4f);
            p.ClosePath();
            p.Fill();
        }

        void DrawBox(Painter2D p, Vector2 c, float r, float thick, float centre, BoxState state, float flash, bool active,
                     System.Func<float, float> Deg, System.Func<float, float, Vector2> At)
        {
            float t0 = centre - _boxHalf, t1 = centre + _boxHalf;
            float inner = r - r * 0.08f, outer = r + r * 0.08f;

            // The box to hit next is bright and pulses; the other waits, dimmed (unless it's flashing a result).
            bool next = active && state == BoxState.Ready;
            float pulse = next ? 0.5f + 0.5f * Mathf.Sin(Time.realtimeSinceStartup * 10f) : 0f;
            Color col = state switch
            {
                BoxState.Hit => Color.Lerp(boxColor, hitColor, flash),
                BoxState.Missed => Color.Lerp(boxColor, missColor, flash),
                _ => boxColor,
            };
            if (!next) col.a *= Mathf.Lerp(0.3f, 1f, flash);

            // the arrow over the next box, bobbing toward it
            if (next)
            {
                Vector2 a = At(centre, outer + r * (0.04f + 0.025f * pulse));
                Vector2 down = (c - a).normalized, across = new(-down.y, down.x);
                float s = r * 0.065f;
                p.fillColor = boxColor;
                p.BeginPath();
                p.MoveTo(a);
                p.LineTo(a - down * s * 1.4f + across * s);
                p.LineTo(a - down * s * 1.4f - across * s);
                p.ClosePath();
                p.Fill();
            }

            // translucent fill, then the outline
            var fill = col; fill.a *= 0.25f + 0.35f * flash + (next ? 0.25f + 0.25f * pulse : 0f);
            p.fillColor = fill;
            p.BeginPath();
            p.Arc(c, outer, Deg(t0), Deg(t1));
            p.LineTo(At(t1, inner));
            p.Arc(c, inner, Deg(t1), Deg(t0), ArcDirection.CounterClockwise);
            p.ClosePath();
            p.Fill();

            p.strokeColor = col;
            p.lineWidth = thick * (1.1f + flash * 0.8f + (next ? 0.6f + 0.4f * pulse : 0f));
            p.lineJoin = LineJoin.Round;
            p.Stroke();
        }
    }

    /// <summary>The "speed bar": a circle that fills clockwise from the top with the push speed (0-1).</summary>
    [UxmlElement]
    public partial class SpeedRing : VisualElement
    {
        float _value;

        [UxmlAttribute] public float value { get => _value; set { _value = Mathf.Clamp01(value); MarkDirtyRepaint(); } }
        [UxmlAttribute] public Color trackColor { get; set; } = new(0.561f, 0.69f, 0.733f, 0.16f);   // HUD --track
        [UxmlAttribute] public Color lowColor { get; set; } = new(0.8f, 0.55f, 0.22f);           // a duller orange when slow
        [UxmlAttribute] public Color highColor { get; set; } = new(1f, 0.722f, 0.302f);          // HUD --orange at full
        [UxmlAttribute] public float thickness { get; set; } = 10f;

        public SpeedRing()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext ctx)
        {
            float w = contentRect.width, h = contentRect.height;
            if (w < 4f || h < 4f) return;
            var c = new Vector2(w * 0.5f, h * 0.5f);
            float r = Mathf.Min(w, h) * 0.5f - thickness - 1f;
            var p = ctx.painter2D;

            // its own backing disc
            p.fillColor = new Color(0.02f, 0.09f, 0.16f, 0.78f);
            p.BeginPath(); p.Arc(c, r + thickness, 0f, 360f); p.Fill();

            p.lineWidth = Mathf.Max(2f, thickness * 0.4f);
            p.strokeColor = trackColor;
            p.BeginPath();
            p.Arc(c, r, 0f, 360f);
            p.Stroke();

            if (_value <= 0.001f) return;
            p.lineCap = LineCap.Round;
            p.lineWidth = thickness;
            p.strokeColor = Color.Lerp(lowColor, highColor, _value);
            p.BeginPath();
            p.Arc(c, r, -90f, -90f + 360f * _value);
            p.Stroke();
        }
    }
}
