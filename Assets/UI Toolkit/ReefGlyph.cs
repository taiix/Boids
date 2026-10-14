using UnityEngine;
using UnityEngine.UIElements;

namespace ReefRun
{
    /// <summary>
    /// Small stroked icon (plus, check, cross, eye) drawn with Painter2D, so the menus don't depend
    /// on a font having the glyph. Size it with width/height; it scales its strokes to fit.
    /// </summary>
    [UxmlElement]
    public partial class ReefGlyph : VisualElement
    {
        public enum Kind { Plus, Check, Cross, Eye, Mic, MicOff }

        Kind _kind = Kind.Plus;
        Color _color = new(1f, 0.72f, 0.3f); // in-game arrow orange #ffb84d

        [UxmlAttribute]
        public Kind kind { get => _kind; set { _kind = value; MarkDirtyRepaint(); } }

        [UxmlAttribute]
        public Color color { get => _color; set { _color = value; MarkDirtyRepaint(); } }

        public ReefGlyph()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        public ReefGlyph(Kind kind, Color color, float size) : this()
        {
            _kind = kind;
            _color = color;
            style.width = size;
            style.height = size;
        }

        void Draw(MeshGenerationContext ctx)
        {
            float w = contentRect.width, h = contentRect.height;
            if (w < 1f || h < 1f) return;
            float s = Mathf.Min(w, h);
            var p = ctx.painter2D;
            p.strokeColor = _color;
            p.lineWidth = Mathf.Max(1.5f, s * 0.17f);
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;

            Vector2 P(float x, float y) => new(w * 0.5f + (x - 0.5f) * s, h * 0.5f + (y - 0.5f) * s);

            p.BeginPath();
            switch (_kind)
            {
                case Kind.Plus:
                    p.MoveTo(P(0.5f, 0.12f)); p.LineTo(P(0.5f, 0.88f));
                    p.MoveTo(P(0.12f, 0.5f)); p.LineTo(P(0.88f, 0.5f));
                    break;
                case Kind.Check:
                    p.MoveTo(P(0.14f, 0.52f)); p.LineTo(P(0.4f, 0.78f)); p.LineTo(P(0.88f, 0.24f));
                    break;
                case Kind.Cross:
                    p.MoveTo(P(0.18f, 0.18f)); p.LineTo(P(0.82f, 0.82f));
                    p.MoveTo(P(0.82f, 0.18f)); p.LineTo(P(0.18f, 0.82f));
                    break;
                case Kind.Eye:
                    p.lineWidth = Mathf.Max(1.2f, s * 0.1f);
                    p.MoveTo(P(0.04f, 0.5f));
                    p.BezierCurveTo(P(0.3f, 0.12f), P(0.7f, 0.12f), P(0.96f, 0.5f));
                    p.BezierCurveTo(P(0.7f, 0.88f), P(0.3f, 0.88f), P(0.04f, 0.5f));
                    p.ClosePath();
                    p.Stroke();
                    p.fillColor = _color;
                    p.BeginPath();
                    p.Arc(P(0.5f, 0.5f), s * 0.14f, 0f, 360f);
                    p.Fill();
                    return;
                case Kind.Mic:
                case Kind.MicOff:
                    p.lineWidth = Mathf.Max(1.4f, s * 0.11f);
                    // capsule
                    p.MoveTo(P(0.38f, 0.22f)); p.LineTo(P(0.38f, 0.48f));
                    p.Arc(P(0.5f, 0.48f), s * 0.12f, 180f, 0f, ArcDirection.CounterClockwise);
                    p.LineTo(P(0.62f, 0.22f));
                    p.Arc(P(0.5f, 0.22f), s * 0.12f, 0f, 180f, ArcDirection.CounterClockwise);
                    // cradle + stand
                    p.MoveTo(P(0.24f, 0.46f));
                    p.BezierCurveTo(P(0.24f, 0.78f), P(0.76f, 0.78f), P(0.76f, 0.46f));
                    p.MoveTo(P(0.5f, 0.72f)); p.LineTo(P(0.5f, 0.88f));
                    p.MoveTo(P(0.34f, 0.88f)); p.LineTo(P(0.66f, 0.88f));
                    if (_kind == Kind.MicOff) { p.MoveTo(P(0.14f, 0.12f)); p.LineTo(P(0.86f, 0.9f)); }
                    break;
            }
            p.Stroke();
        }
    }
}
