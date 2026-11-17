using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace FishGame
{
    /// <summary>HUD palette (MatchHud.uss) and small drawing helpers for the code-drawn minigame pieces.</summary>
    static class Mg
    {
        public static readonly Color Navy = new(0.02f, 0.09f, 0.16f);
        public static readonly Color Orange = new(1f, 0.722f, 0.302f);
        public static readonly Color Red = new(0.851f, 0.251f, 0.251f);
        public static readonly Color Amber = new(0.902f, 0.702f, 0.2f);
        public static readonly Color Dim = new(0.561f, 0.69f, 0.733f);
        public static readonly Color Text = new(0.933f, 0.965f, 0.969f);
        public static readonly Color RopeLight = new(0.84f, 0.78f, 0.64f);        // old fishing line / net
        public static readonly Color RopeDark = new(0.55f, 0.48f, 0.36f);
        public static readonly Color Backing = new(0.02f, 0.09f, 0.16f, 0.72f);
        public static Color A(Color c, float a) { c.a = a; return c; }

        public static void Disc(Painter2D p, Vector2 c, float r, Color col)
        {
            p.fillColor = col; p.BeginPath(); p.Arc(c, r, 0f, 360f); p.Fill();
        }

        public static void Line(Painter2D p, Vector2 a, Vector2 b, float w, Color col)
        {
            p.strokeColor = col; p.lineWidth = w; p.BeginPath(); p.MoveTo(a); p.LineTo(b); p.Stroke();
        }

        /// <summary>A rope between two points: dark core, light strand, and the twist marks along it.</summary>
        public static void Rope(Painter2D p, Vector2 a, Vector2 b, float w, Color light, Color dark)
        {
            p.lineCap = LineCap.Round;
            Line(p, a, b, w + 3f, A(Navy, 0.85f));
            Line(p, a, b, w, light);
            Vector2 d = b - a; float len = d.magnitude;
            if (len < 1f) return;
            Vector2 n = d / len, s = new(-n.y, n.x);
            p.lineCap = LineCap.Butt;
            for (float t = w * 0.9f; t < len - w * 0.5f; t += w * 1.15f)
            {
                Vector2 m = a + n * t;
                Line(p, m - s * w * 0.45f - n * w * 0.25f, m + s * w * 0.45f + n * w * 0.25f, w * 0.28f, dark);
            }
            p.lineCap = LineCap.Round;
        }

        /// <summary>Same thing along an arc (degrees).</summary>
        public static void RopeArc(Painter2D p, Vector2 c, float r, float from, float to, float w, Color light, Color dark)
        {
            p.lineCap = LineCap.Round;
            p.strokeColor = A(Navy, 0.85f); p.lineWidth = w + 3f; p.BeginPath(); p.Arc(c, r, from, to); p.Stroke();
            p.strokeColor = light; p.lineWidth = w; p.BeginPath(); p.Arc(c, r, from, to); p.Stroke();
            float step = Mathf.Rad2Deg * (w * 1.15f / Mathf.Max(r, 1f));
            p.lineCap = LineCap.Butt;
            for (float a = from + step * 0.7f; a < to - step * 0.4f; a += step)
            {
                float ra = a * Mathf.Deg2Rad;
                Vector2 dir = new(Mathf.Cos(ra), Mathf.Sin(ra)), tan = new(-dir.y, dir.x);
                Vector2 m = c + dir * r;
                Line(p, m - dir * w * 0.45f - tan * w * 0.25f, m + dir * w * 0.45f + tan * w * 0.25f, w * 0.28f, dark);
            }
            p.lineCap = LineCap.Round;
        }

        /// <summary>A small fish seen from the side, nose along <paramref name="dir"/>.</summary>
        public static void Fish(Painter2D p, Vector2 c, Vector2 dir, float size, Color body)
        {
            dir = dir.normalized; Vector2 s = new(-dir.y, dir.x);
            Vector2 P(float f, float side) => c + dir * f * size + s * side * size;
            p.fillColor = body;
            p.BeginPath();
            p.MoveTo(P(1f, 0f));
            p.BezierCurveTo(P(0.8f, 0.55f), P(-0.3f, 0.6f), P(-0.7f, 0.1f));
            p.LineTo(P(-1.15f, 0.55f)); p.LineTo(P(-1.05f, 0f)); p.LineTo(P(-1.15f, -0.55f));
            p.LineTo(P(-0.7f, -0.1f));
            p.BezierCurveTo(P(-0.3f, -0.6f), P(0.8f, -0.55f), P(1f, 0f));
            p.ClosePath(); p.Fill();
            Disc(p, P(0.55f, 0.12f), size * 0.12f, Navy);
        }

        /// <summary>A hand-drawn wobble: the same for the same seed, different along the way.</summary>
        public static float Wobble(int seed, float t) => Mathf.PerlinNoise(seed * 7.13f, t) - 0.5f;
    }

    // ===================================================================== plug the leak

    /// <summary>
    /// The pressure tube: upright, the safe band (orange) narrowing as the leak is plugged, the needle
    /// (a white bar with a notch) and the bolts down its side that tighten as the plug holds.
    /// Value 0 = bottom (no pressure), 1 = top.
    /// </summary>
    [UxmlElement]
    public partial class PressureGauge : VisualElement
    {
        public float value = 0.5f;
        public float bandCentre = 0.5f, bandWidth = 0.3f;
        public float danger;             // 0-1, how close to bursting
        public float seal;               // 0-1, the bolts
        public int bolts = 5;

        public PressureGauge() { pickingMode = PickingMode.Ignore; generateVisualContent += Draw; }

        void Draw(MeshGenerationContext ctx)
        {
            float w = contentRect.width, h = contentRect.height;
            if (w < 4f || h < 4f) return;
            var p = ctx.painter2D;
            float tubeW = w * 0.34f, cx = w * 0.62f, top = 24f, bot = h - 24f;
            float x0 = cx - tubeW / 2, x1 = cx + tubeW / 2;

            // backing: a rounded tube a size up, and a strip for the bolts
            Capsule(p, x0 - 12, x1 + 12, top - 14, bot + 14, Mg.Backing);
            Capsule(p, w * 0.06f, w * 0.3f, top + 10, bot - 10, Mg.A(Mg.Navy, 0.55f));

            // the glass
            Capsule(p, x0, x1, top, bot, Mg.A(Mg.Dim, 0.16f));
            float Y(float v) => Mathf.Lerp(bot - tubeW / 2, top + tubeW / 2, Mathf.Clamp01(v));

            // danger at both ends, warmer as it nears bursting
            float dz = (bot - top) * 0.07f;
            Rect(p, x0 + 4, x1 - 4, top + 4, top + dz, Mg.A(Mg.Red, 0.35f + 0.55f * danger));
            Rect(p, x0 + 4, x1 - 4, bot - dz, bot - 4, Mg.A(Mg.Red, 0.35f + 0.55f * danger));

            // the safe band: what to aim for
            float b0 = Y(bandCentre + bandWidth / 2), b1 = Y(bandCentre - bandWidth / 2);
            Rect(p, x0 + 4, x1 - 4, b0, b1, Mg.A(Mg.Orange, 0.32f));
            p.strokeColor = Mg.Orange; p.lineWidth = 3f;
            p.BeginPath(); p.MoveTo(new Vector2(x0 - 6, b0)); p.LineTo(new Vector2(x1 + 6, b0)); p.Stroke();
            p.BeginPath(); p.MoveTo(new Vector2(x0 - 6, b1)); p.LineTo(new Vector2(x1 + 6, b1)); p.Stroke();

            // the rim goes red as it nears bursting
            p.strokeColor = Color.Lerp(Mg.A(Mg.Dim, 0.6f), Mg.Red, danger); p.lineWidth = 3f + 2f * danger;
            CapsulePath(p, x0, x1, top, bot); p.Stroke();

            // the needle
            bool inBand = Mathf.Abs(value - bandCentre) <= bandWidth / 2;
            float ny = Y(value);
            Color nc = inBand ? Mg.Text : Mg.Red;
            Rect(p, x0 - 14, x1 + 14, ny - 4, ny + 4, nc);
            p.fillColor = nc; p.BeginPath();
            p.MoveTo(new Vector2(x1 + 14, ny - 10)); p.LineTo(new Vector2(x1 + 26, ny)); p.LineTo(new Vector2(x1 + 14, ny + 10)); p.ClosePath(); p.Fill();

            // bolts: filled from the bottom up as the plug holds
            float bx = w * 0.18f;
            for (int i = 0; i < bolts; i++)
            {
                float by = Mathf.Lerp(bot - 50f, top + 50f, bolts == 1 ? 0.5f : i / (float)(bolts - 1));
                float fill = Mathf.Clamp01(seal * bolts - i);
                Mg.Disc(p, new Vector2(bx, by), 15f, Mg.A(Mg.Dim, 0.25f));
                if (fill > 0f)
                {
                    p.fillColor = Mg.Orange; p.BeginPath();
                    p.MoveTo(new Vector2(bx, by)); p.Arc(new Vector2(bx, by), 15f, -90f, -90f + 360f * fill); p.ClosePath(); p.Fill();
                }
                // the slot, turning as it tightens
                float a = (fill * 90f + 20f) * Mathf.Deg2Rad;
                Vector2 d = new(Mathf.Cos(a), Mathf.Sin(a));
                p.lineCap = LineCap.Round;
                Mg.Line(p, new Vector2(bx, by) - d * 8f, new Vector2(bx, by) + d * 8f, 3f, Mg.Navy);
            }
        }

        static void Rect(Painter2D p, float a, float b, float t, float u, Color c)
        {
            p.fillColor = c; p.BeginPath();
            p.MoveTo(new Vector2(a, t)); p.LineTo(new Vector2(b, t)); p.LineTo(new Vector2(b, u)); p.LineTo(new Vector2(a, u));
            p.ClosePath(); p.Fill();
        }

        static void CapsulePath(Painter2D p, float a, float b, float t, float u)
        {
            float r = (b - a) / 2, cx = (a + b) / 2;
            p.BeginPath();
            p.Arc(new Vector2(cx, t + r), r, 180f, 360f);
            p.LineTo(new Vector2(b, u - r));
            p.Arc(new Vector2(cx, u - r), r, 0f, 180f);
            p.ClosePath();
        }

        static void Capsule(Painter2D p, float a, float b, float t, float u, Color c)
        {
            p.fillColor = c; CapsulePath(p, a, b, t, u); p.Fill();
        }
    }

    // ===================================================================== scrub the coral

    /// <summary>
    /// The scrub disc: the coral in the middle (bleached and furred with algae, pinking up as it's
    /// cleaned), a sponge going round it with a trail behind it - short when slow, a long orange sweep
    /// at a good pace, red when it's too fast - and a thin line round the rim for how clean it is.
    /// </summary>
    [UxmlElement]
    public partial class ScrubPad : VisualElement
    {
        public Vector2 cursor;            // -1..1 each
        public float speed01;             // 0 = still, 1 = far too fast
        public float turn = 1f;           // which way it's going round (+1 / -1)
        public float goodMin = 0.25f, goodMax = 0.7f;
        public float clean, sand;

        public ScrubPad() { pickingMode = PickingMode.Ignore; generateVisualContent += Draw; }

        void Draw(MeshGenerationContext ctx)
        {
            float w = contentRect.width, h = contentRect.height;
            if (w < 4f || h < 4f) return;
            var p = ctx.painter2D;
            var c = new Vector2(w * 0.5f, h * 0.5f);
            float R = Mathf.Min(w, h) * 0.5f - 4f, r = R * 0.72f;

            Mg.Disc(p, c, R, Mg.Backing);

            // the coral: tapered, slightly bent branches from one foot
            Color coral = Color.Lerp(new Color(0.9f, 0.88f, 0.82f), new Color(0.95f, 0.45f, 0.58f), clean);
            Vector2 foot = c + new Vector2(0f, r * 0.55f);
            for (int i = 0; i < 8; i++)
            {
                float a = Mathf.Deg2Rad * (-150f + i * 17f + Mg.Wobble(i, 0.3f) * 14f);
                float len = r * (0.6f + 0.22f * Mathf.PerlinNoise(i * 1.7f, 0.5f));
                Vector2 dir = new(Mathf.Cos(a), Mathf.Sin(a));
                Vector2 tip = foot + dir * len, bend = foot + dir * len * 0.55f + new Vector2(-dir.y, dir.x) * len * 0.12f * Mg.Wobble(i, 2f) * 2f;
                p.lineCap = LineCap.Round;
                p.strokeColor = coral; p.lineWidth = r * 0.085f;
                p.BeginPath(); p.MoveTo(foot); p.QuadraticCurveTo(bend, tip); p.Stroke();
                Mg.Disc(p, tip, r * 0.06f, coral);
            }
            // algae: furry clumps, thinning out as it's cleaned
            for (int i = 0; i < 22; i++)
            {
                if (Mathf.PerlinNoise(i * 0.91f, 3.3f) < clean * 1.05f) continue;
                float a = i * 2.39996f, rr = r * (0.12f + 0.5f * ((i * 0.618f) % 1f));
                Vector2 at = c + new Vector2(Mathf.Cos(a) * rr, Mathf.Sin(a) * rr * 0.8f - r * 0.08f);
                float s = r * (0.07f + 0.05f * Mathf.PerlinNoise(i, 7f));
                Mg.Disc(p, at, s, new Color(0.33f, 0.4f, 0.2f, 0.9f));
                Mg.Disc(p, at + new Vector2(s * 0.6f, -s * 0.3f), s * 0.7f, new Color(0.4f, 0.47f, 0.24f, 0.9f));
            }

            // the trail behind the sponge: its length is your pace
            float ang = Mathf.Atan2(cursor.y, cursor.x) * Mathf.Rad2Deg;
            float len01 = Mathf.Clamp01(speed01 / 0.85f);
            bool tooFast = speed01 > 0.85f, good = speed01 >= goodMin && speed01 <= goodMax;
            Color trail = tooFast ? Mg.Red : good ? Mg.Orange : Mg.A(Mg.Dim, 0.6f);
            float sweep = 15f + 160f * len01;
            p.lineCap = LineCap.Round;
            for (int k = 0; k < 4; k++)   // fading toward its tail
            {
                float a0 = ang - turn * sweep * (k + 1) / 4f, a1 = ang - turn * sweep * k / 4f;
                p.strokeColor = Mg.A(trail, 0.85f - k * 0.2f); p.lineWidth = r * (0.12f - k * 0.022f);
                p.BeginPath(); p.Arc(c, r, Mathf.Min(a0, a1), Mathf.Max(a0, a1)); p.Stroke();
            }
            // the sponge
            var sp = c + cursor * r;
            Mg.Disc(p, sp, r * 0.13f, Mg.Navy);
            Mg.Disc(p, sp, r * 0.105f, new Color(0.98f, 0.86f, 0.42f));
            Mg.Disc(p, sp + new Vector2(-r * 0.03f, -r * 0.02f), r * 0.025f, new Color(0.85f, 0.7f, 0.3f));
            Mg.Disc(p, sp + new Vector2(r * 0.035f, r * 0.03f), r * 0.02f, new Color(0.85f, 0.7f, 0.3f));

            // sand kicked up
            if (sand > 0.01f)
                for (int i = 0; i < 9; i++)
                {
                    float a = i * 0.7f + Time.realtimeSinceStartup * 0.4f;
                    Mg.Disc(p, c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r * 0.45f, r * (0.3f + 0.25f * sand), new Color(0.78f, 0.72f, 0.55f, 0.5f * sand));
                }

            // how clean, round the rim
            if (clean > 0.005f)
            {
                p.strokeColor = Mg.Orange; p.lineWidth = 5f;
                p.BeginPath(); p.Arc(c, R - 6f, -90f, -90f + 360f * clean); p.Stroke();
            }
        }
    }

    // ===================================================================== unpick the line

    /// <summary>
    /// The knot board: knots joined by lengths of old line. Drag knots with the mouse; a line that crosses
    /// another shows red, a clear one is plain rope. Positions are 0-1 across the board.
    /// </summary>
    [UxmlElement]
    public partial class KnotBoard : VisualElement
    {
        public Vector2[] nodes = Array.Empty<Vector2>();
        public Vector2Int[] edges = Array.Empty<Vector2Int>();
        public int selected = -1;
        public event Action Changed;
        public event Action Released;

        int _drag = -1;
        const float Pad = 46f;

        public KnotBoard()
        {
            AddToClassList("mg-pick");
            generateVisualContent += Draw;
            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
            RegisterCallback<PointerLeaveEvent>(_ => EndDrag());
        }

        Vector2 ToBoard(Vector2 local) => new(
            Mathf.Clamp01((local.x - Pad) / Mathf.Max(1f, contentRect.width - Pad * 2)),
            Mathf.Clamp01((local.y - Pad) / Mathf.Max(1f, contentRect.height - Pad * 2)));
        Vector2 ToLocal(Vector2 b) => new(Pad + b.x * (contentRect.width - Pad * 2), Pad + b.y * (contentRect.height - Pad * 2));

        void OnDown(PointerDownEvent e)
        {
            float best = 44f * 44f;
            _drag = -1;
            for (int i = 0; i < nodes.Length; i++)
            {
                float d = (ToLocal(nodes[i]) - (Vector2)e.localPosition).sqrMagnitude;
                if (d < best) { best = d; _drag = i; }
            }
            if (_drag >= 0) { selected = _drag; this.CapturePointer(e.pointerId); MarkDirtyRepaint(); }
        }

        void OnMove(PointerMoveEvent e)
        {
            if (_drag < 0) return;
            nodes[_drag] = ToBoard(e.localPosition);
            Changed?.Invoke();
            MarkDirtyRepaint();
        }

        void OnUp(PointerUpEvent e)
        {
            if (this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId);
            EndDrag();
        }

        void EndDrag()
        {
            if (_drag < 0) return;
            _drag = -1;
            Released?.Invoke();
        }

        public bool Crosses(int i)
        {
            for (int j = 0; j < edges.Length; j++)
            {
                if (i == j) continue;
                var a = edges[i]; var b = edges[j];
                if (a.x == b.x || a.x == b.y || a.y == b.x || a.y == b.y) continue;
                if (SegmentsCross(nodes[a.x], nodes[a.y], nodes[b.x], nodes[b.y])) return true;
            }
            return false;
        }

        public int Crossings()
        {
            int n = 0;
            for (int i = 0; i < edges.Length; i++)
                for (int j = i + 1; j < edges.Length; j++)
                {
                    var a = edges[i]; var b = edges[j];
                    if (a.x == b.x || a.x == b.y || a.y == b.x || a.y == b.y) continue;
                    if (SegmentsCross(nodes[a.x], nodes[a.y], nodes[b.x], nodes[b.y])) n++;
                }
            return n;
        }

        static float Side(Vector2 p, Vector2 q, Vector2 r) => (q.x - p.x) * (r.y - p.y) - (q.y - p.y) * (r.x - p.x);
        static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d) =>
            Side(a, b, c) * Side(a, b, d) < 0f && Side(c, d, a) * Side(c, d, b) < 0f;

        void Draw(MeshGenerationContext ctx)
        {
            float w = contentRect.width, h = contentRect.height;
            if (w < 4f) return;
            var p = ctx.painter2D;

            // backing: the HUD's leaning slab shape, sized to the board
            float lean = h * 0.12f;
            p.fillColor = Mg.Backing; p.BeginPath();
            p.MoveTo(new Vector2(lean, 0f)); p.LineTo(new Vector2(w, 0f)); p.LineTo(new Vector2(w - lean, h)); p.LineTo(new Vector2(0f, h));
            p.ClosePath(); p.Fill();

            for (int i = 0; i < edges.Length; i++)
            {
                bool bad = Crosses(i);
                Mg.Rope(p, ToLocal(nodes[edges[i].x]), ToLocal(nodes[edges[i].y]), 7f,
                        bad ? Mg.Red : Mg.RopeLight, bad ? new Color(0.55f, 0.12f, 0.12f) : Mg.RopeDark);
            }
            for (int i = 0; i < nodes.Length; i++)
            {
                var c = ToLocal(nodes[i]);
                if (i == selected) Mg.Disc(p, c, 25f, Mg.A(Mg.Text, 0.9f));
                // a knot: a fat loop of the same line, orange so you know to grab it
                Mg.Disc(p, c, 19f, Mg.Navy);
                Mg.Disc(p, c, 16f, Mg.Orange);
                p.strokeColor = new Color(0.75f, 0.5f, 0.15f); p.lineWidth = 3f; p.lineCap = LineCap.Round;
                p.BeginPath(); p.Arc(c, 9f, 200f, 470f); p.Stroke();
            }
        }
    }

    // ===================================================================== chew the net

    /// <summary>
    /// The net round the turtle: rope strands turning round it, one weak (orange). The middle is left
    /// open - the real turtle shows through it (the net minigame keeps this ring over the turtle on
    /// screen). Your fish sits at the top, nose down at the notch; while you're biting, a line round its
    /// head runs down. A thin line under the ring is how far through the ropes the pair is.
    /// </summary>
    [UxmlElement]
    public partial class NetRing : VisualElement
    {
        public int strands = 10;
        public float rotation;
        public int weak;
        public float window = 0.28f;
        public float flash;
        public bool flashGood = true;
        public float biting;              // 0-1 of the bite left
        public float progress;            // 0-1 the pair's ropes

        public NetRing() { pickingMode = PickingMode.Ignore; generateVisualContent += Draw; }

        public float StrandAngle(int i) => rotation + i * Mathf.PI * 2f / strands;

        void Draw(MeshGenerationContext ctx)
        {
            float w = contentRect.width, h = contentRect.height;
            if (w < 4f || h < 4f) return;
            var p = ctx.painter2D;
            var c = new Vector2(w * 0.5f, h * 0.5f);
            float r = Mathf.Min(w, h) * 0.33f;

            // backing: a band under the strands only - the middle stays open on the real turtle
            p.lineCap = LineCap.Butt;
            p.strokeColor = Mg.A(Mg.Navy, 0.55f); p.lineWidth = r * 0.34f;
            p.BeginPath(); p.Arc(c, r, 0f, 360f); p.Stroke();

            // the notch where the strand gets bitten
            float top = -90f, win = window * Mathf.Rad2Deg;
            p.strokeColor = Mg.A(Mg.Text, 0.14f + 0.3f * biting); p.lineWidth = r * 0.24f; p.lineCap = LineCap.Butt;
            p.BeginPath(); p.Arc(c, r, top - win, top + win); p.Stroke();

            // strands, each a little off true like real net
            float span = 360f / strands * 0.6f;
            for (int i = 0; i < strands; i++)
            {
                float a = StrandAngle(i) * Mathf.Rad2Deg;
                float rr = r * (1f + Mg.Wobble(i, 0.5f) * 0.06f);
                bool isWeak = i == weak;
                Color light = isWeak ? Mg.Orange : Mg.RopeLight, dark = isWeak ? new Color(0.7f, 0.42f, 0.1f) : Mg.RopeDark;
                if (isWeak && flash > 0f) light = Color.Lerp(light, flashGood ? Mg.Text : Mg.Red, flash);
                Mg.RopeArc(p, c, rr, a - span / 2, a + span / 2, isWeak ? r * 0.085f : r * 0.05f, light, dark);
            }

            // you: nose down at the notch, the bite running out round your head
            var fishAt = c + new Vector2(0f, -r - r * 0.36f);
            if (biting > 0f)
            {
                p.strokeColor = Mg.Orange; p.lineWidth = 5f; p.lineCap = LineCap.Round;
                p.BeginPath(); p.Arc(fishAt, r * 0.27f, -90f, -90f + 360f * biting); p.Stroke();
            }
            Mg.Fish(p, fishAt, Vector2.up, r * 0.19f, new Color(1f, 0.6f, 0.2f));

            // how far through the ropes
            float y = c.y + r * 1.5f, half = r * 0.9f;
            Mg.Line(p, new Vector2(c.x - half, y), new Vector2(c.x + half, y), 6f, Mg.A(Mg.Dim, 0.25f));
            if (progress > 0.005f) Mg.Line(p, new Vector2(c.x - half, y), new Vector2(c.x - half + 2f * half * progress, y), 6f, Mg.Orange);
        }

    }

    // ===================================================================== under the sand

    /// <summary>A heartbeat trace on a strip that fades out at its ends; brackets mark where to tap.</summary>
    [UxmlElement]
    public partial class Heartbeat : VisualElement
    {
        public float phase;
        public float interval = 1.1f;
        public float flash;
        public bool flashGood = true;

        public Heartbeat() { pickingMode = PickingMode.Ignore; generateVisualContent += Draw; }

        void Draw(MeshGenerationContext ctx)
        {
            float w = contentRect.width, h = contentRect.height;
            if (w < 4f || h < 4f) return;
            var p = ctx.painter2D;
            float mid = h * 0.58f, cx = w * 0.5f, secondsAcross = 3.4f;
            float pxPerBeat = w / secondsAcross * interval;

            // backing: darkest in the middle, gone at the ends
            for (int i = 0; i < 12; i++)
            {
                float f0 = i / 12f, f1 = (i + 1) / 12f;
                float a = 0.7f * Mathf.Sin(Mathf.PI * (f0 + f1) * 0.5f);
                p.fillColor = Mg.A(Mg.Navy, a); p.BeginPath();
                p.MoveTo(new Vector2(w * f0, 0f)); p.LineTo(new Vector2(w * f1, 0f)); p.LineTo(new Vector2(w * f1, h)); p.LineTo(new Vector2(w * f0, h));
                p.ClosePath(); p.Fill();
            }

            Color line = flash > 0f ? Color.Lerp(Mg.A(Mg.Text, 0.85f), flashGood ? Mg.Orange : Mg.Red, flash) : Mg.A(Mg.Text, 0.85f);
            p.strokeColor = line; p.lineWidth = 3.5f; p.lineJoin = LineJoin.Round; p.lineCap = LineCap.Round;
            p.BeginPath();
            for (int i = 0; i <= 260; i++)
            {
                float x = w * i / 260f;
                float u = (x - cx) / pxPerBeat + phase;
                float f = u - Mathf.Floor(u);
                float y = f < 0.06f ? -Mathf.Sin(f / 0.06f * Mathf.PI) * h * 0.4f
                        : f < 0.11f ? Mathf.Sin((f - 0.06f) / 0.05f * Mathf.PI) * h * 0.14f : 0f;
                y += Mg.Wobble(3, x * 0.05f) * 2f;   // a hand on the pen
                if (i == 0) p.MoveTo(new Vector2(x, mid + y)); else p.LineTo(new Vector2(x, mid + y));
            }
            p.Stroke();

            // brackets where the beat should be when you tap
            p.strokeColor = Mg.Orange; p.lineWidth = 3f;
            float bw = 16f;
            p.BeginPath(); p.MoveTo(new Vector2(cx - bw, 8f)); p.LineTo(new Vector2(cx - bw, 2f)); p.LineTo(new Vector2(cx + bw, 2f)); p.LineTo(new Vector2(cx + bw, 8f)); p.Stroke();
            p.BeginPath(); p.MoveTo(new Vector2(cx - bw, h - 8f)); p.LineTo(new Vector2(cx - bw, h - 2f)); p.LineTo(new Vector2(cx + bw, h - 2f)); p.LineTo(new Vector2(cx + bw, h - 8f)); p.Stroke();
        }
    }

    /// <summary>Breath as a row of bubbles; the last one shrinks, spent ones are gone.</summary>
    [UxmlElement]
    public partial class BreathBubbles : VisualElement
    {
        public float breath = 1f;
        public int count = 8;

        public BreathBubbles() { pickingMode = PickingMode.Ignore; generateVisualContent += Draw; }

        void Draw(MeshGenerationContext ctx)
        {
            float w = contentRect.width, h = contentRect.height;
            if (w < 4f || h < 4f) return;
            var p = ctx.painter2D;
            float step = w / count, r = Mathf.Min(step * 0.36f, h * 0.42f);
            float left = Mathf.Clamp01(breath) * count;
            bool low = breath < 0.25f;
            for (int i = 0; i < count; i++)
            {
                var c = new Vector2(step * (i + 0.5f), h * 0.5f + Mg.Wobble(i, 1f) * 4f);
                float f = Mathf.Clamp01(left - i);
                if (f <= 0f) { Mg.Disc(p, c, 2f, Mg.A(Mg.Dim, 0.4f)); continue; }
                float rr = r * Mathf.Lerp(0.45f, 1f, f) * (0.9f + 0.2f * Mathf.PerlinNoise(i * 3f, 0f));
                Mg.Disc(p, c, rr, Mg.A(low ? Mg.Red : Mg.Text, 0.18f));
                p.strokeColor = low ? Mg.Red : Mg.A(Mg.Text, 0.9f); p.lineWidth = 2.5f;
                p.BeginPath(); p.Arc(c, rr, 0f, 360f); p.Stroke();
                Mg.Disc(p, c + new Vector2(-rr * 0.35f, -rr * 0.35f), rr * 0.22f, Mg.A(Color.white, 0.8f));
            }
        }
    }

    /// <summary>Buried: the screen's edges go dark and sandy.</summary>
    [UxmlElement]
    public partial class SandVignette : VisualElement
    {
        static Texture2D s_tex;

        public SandVignette()
        {
            pickingMode = PickingMode.Ignore;
            if (s_tex == null)
            {
                const int n = 64;
                s_tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                        float d = Mathf.Sqrt(dx * dx * 0.8f + dy * dy);
                        float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 1.3f, d)) * 0.5f;
                        px[y * n + x] = new Color(0.16f, 0.13f, 0.08f, a);
                    }
                s_tex.SetPixels32(px);
                s_tex.Apply();
            }
            style.backgroundImage = s_tex;
            style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
        }
    }

    // ===================================================================== pushing together

    /// <summary>Who's pushing: a little fish per pusher the trash needs, lit when one's on it.</summary>
    [UxmlElement]
    public partial class PusherPips : VisualElement
    {
        public int needed = 2, playing;

        public PusherPips() { pickingMode = PickingMode.Ignore; generateVisualContent += Draw; }

        void Draw(MeshGenerationContext ctx)
        {
            float h = contentRect.height;
            if (h < 4f || needed < 2) return;
            var p = ctx.painter2D;
            for (int i = 0; i < needed; i++)
                Mg.Fish(p, new Vector2(h * 0.9f + i * h * 1.9f, h * 0.5f), Vector2.right, h * 0.42f,
                        i < playing ? Mg.Navy : Mg.A(Mg.Navy, 0.3f));
        }
    }
}
