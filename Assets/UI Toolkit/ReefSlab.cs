using UnityEngine;
using UnityEngine.UIElements;

namespace ReefRun
{
    /// <summary>
    /// The menus' and HUD's panel shape: an opaque block whose side edges lean like the italic display
    /// type, optionally with a bite out of its top-right corner. USS has no skew or clipping, so it
    /// paints itself with Painter2D, behind its children.
    /// It is styled from USS through custom properties (so hover and state classes can recolour it):
    ///   --slab-color   fill colour
    ///   --slab-lean    how far a side edge leans, as a fraction of the height (0.21 matches Barlow's italic)
    ///   --slab-left    1 = the left edge leans, 0 = it stays upright (a block flush to the screen's left)
    ///   --slab-right   the same for the right edge
    ///   --slab-notch   radius, in px, of the bite out of the top-right corner (0 = none)
    ///   --slab-outline        colour of a thin frame drawn along its edge (unset / clear = none): the "selected" look
    ///   --slab-outline-width  that frame's thickness in px (default 3)
    /// Set them on the element's own classes, and give it enough side padding to clear the lean.
    /// </summary>
    [UxmlElement]
    public partial class ReefSlab : VisualElement
    {
        static readonly CustomStyleProperty<Color> ColorProperty = new("--slab-color");
        static readonly CustomStyleProperty<float> LeanProperty = new("--slab-lean");
        static readonly CustomStyleProperty<float> LeftProperty = new("--slab-left");
        static readonly CustomStyleProperty<float> RightProperty = new("--slab-right");
        static readonly CustomStyleProperty<float> NotchProperty = new("--slab-notch");
        static readonly CustomStyleProperty<Color> OutlineProperty = new("--slab-outline");
        static readonly CustomStyleProperty<float> OutlineWidthProperty = new("--slab-outline-width");

        static readonly Color DefaultColor = new(0.02f, 0.09f, 0.16f);   // panel navy #051729
        const float DefaultLean = 0.21f;

        Color _color = DefaultColor;
        float _lean = DefaultLean, _notch;
        bool _left = true, _right = true;
        Color _outline = Color.clear;
        float _outlineWidth = 3f;

        public ReefSlab()
        {
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyle);
        }

        // Read from the defaults every time: a property that a class just stopped setting must fall back.
        void OnCustomStyle(CustomStyleResolvedEvent e)
        {
            var s = e.customStyle;
            Color color = s.TryGetValue(ColorProperty, out var c) ? c : DefaultColor;
            float lean = s.TryGetValue(LeanProperty, out var l) ? l : DefaultLean;
            bool left = !s.TryGetValue(LeftProperty, out var lf) || lf > 0.5f;
            bool right = !s.TryGetValue(RightProperty, out var rt) || rt > 0.5f;
            float notch = s.TryGetValue(NotchProperty, out var n) ? n : 0f;

            Color outline = s.TryGetValue(OutlineProperty, out var o) ? o : Color.clear;
            float outlineWidth = s.TryGetValue(OutlineWidthProperty, out var ow) ? ow : 3f;

            if (color == _color && Mathf.Approximately(lean, _lean) && left == _left && right == _right
                && Mathf.Approximately(notch, _notch) && outline == _outline && Mathf.Approximately(outlineWidth, _outlineWidth)) return;
            _outline = outline; _outlineWidth = outlineWidth;
            _color = color; _lean = lean; _left = left; _right = right; _notch = notch;
            MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext ctx)
        {
            float w = layout.width, h = layout.height;
            if (w < 1f || h < 1f || _color.a <= 0f) return;

            float lean = Mathf.Clamp(_lean * h, 0f, w * 0.5f);
            float l = _left ? lean : 0f, r = _right ? lean : 0f;
            float notch = Mathf.Clamp(_notch, 0f, Mathf.Min(h, w - l) * 0.9f);

            var p = ctx.painter2D;
            p.fillColor = _color;
            Outline(p, w, h, l, r, notch, 0f);
            p.Fill();

            // The frame, kept inside the block's own edge so a clipping parent can't cut it.
            if (_outline.a > 0f && _outlineWidth > 0.1f)
            {
                p.strokeColor = _outline;
                p.lineWidth = _outlineWidth;
                p.lineJoin = LineJoin.Miter;
                Outline(p, w, h, l, r, notch, _outlineWidth * 0.5f);
                p.Stroke();
            }
        }

        // The block's outline as a closed path, pulled in by `inset` px all round.
        static void Outline(Painter2D p, float w, float h, float l, float r, float notch, float inset)
        {
            float top = inset, bottom = h - inset;
            // A leaning edge moves sideways as it goes down: keep the inset edge parallel to the real one.
            float lTop = l + inset - (l / h) * inset, lBottom = inset + (l / h) * inset;
            float rTop = w - inset - (r / h) * inset, rBottom = w - r - inset + (r / h) * inset;
            p.BeginPath();
            p.MoveTo(new Vector2(lTop, top));
            if (notch > 0.5f)
            {
                // the bite: an arc centred on the corner, from the top edge round to the leaning right edge
                var corner = new Vector2(w, 0f);
                float radius = notch + inset;
                float edgeAngle = Mathf.Atan2(h, -r) * Mathf.Rad2Deg;   // which way the right edge leaves the corner
                p.LineTo(new Vector2(w - radius, top));
                p.Arc(corner, radius, 180f, edgeAngle, ArcDirection.CounterClockwise);
            }
            else p.LineTo(new Vector2(rTop, top));
            p.LineTo(new Vector2(rBottom, bottom));
            p.LineTo(new Vector2(lBottom, bottom));
            p.ClosePath();
        }
    }
}
