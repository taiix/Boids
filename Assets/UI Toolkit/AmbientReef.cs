using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ReefRun
{
    /// <summary>
    /// Animated underwater ambience drawn with Painter2D: two swaying god-ray
    /// beams, drifting caustic blobs, dark fish silhouettes (ellipse body +
    /// triangle tail) moving left->right, and rising bubbles. Transparent, sits
    /// behind the UI. The base gradient + bottom glow is a baked texture set on
    /// the stage background (see the controller), not drawn here.
    /// </summary>
    public class AmbientReef : VisualElement
    {
        public new class UxmlFactory : UxmlFactory<AmbientReef> { }

        struct Fish  { public float x, y, s, sp, op, ph; }
        struct Bubble{ public float x, y, r, sp, op, ph; }
        struct Blob  { public float x, y, r, sp, ph; }

        readonly List<Fish>   _fish   = new();
        readonly List<Bubble> _bubs   = new();
        readonly List<Blob>   _blobs  = new();

        float _t;
        long  _lastMs;
        IVisualElementScheduledItem _ticker;

        static readonly Color FishCol = new(0.012f, 0.071f, 0.102f); // near-black silhouette
        static readonly Color Aqua    = new(0.184f, 0.788f, 0.741f); // #2fc9bd

        public AmbientReef()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += OnPaint;

            RegisterCallback<GeometryChangedEvent>(_ => Seed());
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                _lastMs = System.Environment.TickCount;
                _ticker = schedule.Execute(Tick).Every(16);
            });
            RegisterCallback<DetachFromPanelEvent>(_ => _ticker?.Pause());
        }

        void Seed()
        {
            float W = Mathf.Max(1, contentRect.width);
            float H = Mathf.Max(1, contentRect.height);
            if (_fish.Count > 0) return; // seed once

            for (int i = 0; i < 9; i++)
                _fish.Add(new Fish { x = Rnd(0, W), y = Rnd(110, H - 60),
                    s = Rnd(.5f, 1.55f), sp = Rnd(8, 28), op = Rnd(.10f, .26f), ph = Rnd(0, 6.28f) });

            for (int i = 0; i < 14; i++)
                _bubs.Add(new Bubble { x = Rnd(0, W), y = Rnd(0, H),
                    r = Rnd(1.2f, 3.4f), sp = Rnd(14, 30), op = Rnd(.08f, .22f), ph = Rnd(0, 6.28f) });

            for (int i = 0; i < 5; i++)
                _blobs.Add(new Blob { x = Rnd(0, W), y = Rnd(H * .3f, H),
                    r = Rnd(60, 120), sp = Rnd(4, 10), ph = Rnd(0, 6.28f) });
        }

        void Tick()
        {
            long now = System.Environment.TickCount;
            float dt = Mathf.Clamp((now - _lastMs) / 1000f, 0f, 0.05f);
            _lastMs = now;
            _t += dt;

            float W = contentRect.width, H = contentRect.height;
            if (W <= 1) return;

            for (int i = 0; i < _fish.Count; i++)
            {
                var f = _fish[i];
                f.x += f.sp * dt;
                if (f.x > W + 60) { f = new Fish { x = Rnd(-120, -40), y = Rnd(110, H - 60),
                    s = Rnd(.5f, 1.55f), sp = Rnd(8, 28), op = Rnd(.10f, .26f), ph = Rnd(0, 6.28f) }; }
                _fish[i] = f;
            }
            for (int i = 0; i < _bubs.Count; i++)
            {
                var b = _bubs[i];
                b.y -= b.sp * dt;
                b.x += Mathf.Sin(_t + b.ph) * 0.25f;
                if (b.y < -12) { b = new Bubble { x = Rnd(0, W), y = H + Rnd(5, 40),
                    r = Rnd(1.2f, 3.4f), sp = Rnd(14, 30), op = Rnd(.08f, .22f), ph = Rnd(0, 6.28f) }; }
                _bubs[i] = b;
            }
            MarkDirtyRepaint();
        }

        void OnPaint(MeshGenerationContext ctx)
        {
            float W = contentRect.width, H = contentRect.height;
            if (W <= 1 || H <= 1) return;
            var p = ctx.painter2D;

            // --- god rays (two swaying translucent beams from the surface) ---
            DrawRay(p, W * 0.24f,  140f,  9f, 0.10f, 0f, W, H);
            DrawRay(p, W * 0.64f,  170f, -7f, 0.08f, 2.1f, W, H);

            // --- caustic blobs (soft drifting light) ---
            foreach (var bl in _blobs)
            {
                float bx = bl.x + Mathf.Sin(_t * 0.3f + bl.ph) * 18f;
                float by = bl.y + Mathf.Cos(_t * 0.22f + bl.ph) * 12f;
                for (int k = 3; k >= 1; k--)
                {
                    float a = 0.025f * k;
                    p.fillColor = new Color(Aqua.r, Aqua.g, Aqua.b, a);
                    p.BeginPath();
                    p.Arc(new Vector2(bx, by), bl.r * (k / 3f), 0f, 360f);
                    p.Fill();
                }
            }

            // --- fish silhouettes ---
            foreach (var f in _fish)
            {
                float bob = Mathf.Sin(_t + f.ph) * 4f;
                var c = new Vector2(f.x, f.y + bob);
                p.fillColor = new Color(FishCol.r, FishCol.g, FishCol.b, f.op);
                FillEllipse(p, c, 18f * f.s, 7f * f.s);                       // body
                p.BeginPath();                                                // tail
                p.MoveTo(new Vector2(c.x - 16f * f.s, c.y));
                p.LineTo(new Vector2(c.x - 29f * f.s, c.y - 8f * f.s));
                p.LineTo(new Vector2(c.x - 29f * f.s, c.y + 8f * f.s));
                p.ClosePath();
                p.Fill();
            }

            // --- bubbles ---
            foreach (var b in _bubs)
            {
                p.fillColor = new Color(0.59f, 0.90f, 0.88f, b.op);
                p.BeginPath();
                p.Arc(new Vector2(b.x, b.y), b.r, 0f, 360f);
                p.Fill();
            }
        }

        void DrawRay(Painter2D p, float baseX, float width, float tilt, float alpha, float phase, float W, float H)
        {
            float sway = Mathf.Sin(_t * 0.25f + phase) * 26f;
            float topX = baseX + sway;
            float botX = baseX + sway + Mathf.Tan(tilt * Mathf.Deg2Rad) * H;

            p.fillColor = new Color(Aqua.r, Aqua.g, Aqua.b, alpha);
            p.BeginPath();
            p.MoveTo(new Vector2(topX, 0f));
            p.LineTo(new Vector2(topX + width, 0f));
            p.LineTo(new Vector2(botX + width * 1.4f, H));
            p.LineTo(new Vector2(botX, H));
            p.ClosePath();
            p.Fill();
        }

        static void FillEllipse(Painter2D p, Vector2 c, float rx, float ry)
        {
            const int seg = 22;
            p.BeginPath();
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                var pt = new Vector2(c.x + Mathf.Cos(a) * rx, c.y + Mathf.Sin(a) * ry);
                if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
            p.ClosePath();
            p.Fill();
        }

        static float Rnd(float a, float b) => a + Random.value * (b - a);
    }
}
