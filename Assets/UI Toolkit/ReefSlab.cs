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

        static readonly Color DefaultColor = new(0.02f, 0.09f, 0.16f);   // panel navy #051729
        const float DefaultLean = 0.21f;

        Color _color = DefaultColor;
        float _lean = DefaultLean, _notch;
        bool _left = true, _right = true;

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

            if (color == _color && Mathf.Approximately(lean, _lean) && left == _left && right == _right
                && Mathf.Approximately(notch, _notch)) return;
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
            p.BeginPath();
            p.MoveTo(new Vector2(l, 0f));
            if (notch > 0.5f)
            {
                // the bite: an arc centred on the corner, from the top edge round to the leaning right edge
                var corner = new Vector2(w, 0f);
                float edgeAngle = Mathf.Atan2(h, -r) * Mathf.Rad2Deg;   // which way the right edge leaves the corner
                p.LineTo(new Vector2(w - notch, 0f));
                p.Arc(corner, notch, 180f, edgeAngle, ArcDirection.CounterClockwise);
            }
            else p.LineTo(new Vector2(w, 0f));
            p.LineTo(new Vector2(w - r, h));
            p.LineTo(new Vector2(0f, h));
            p.ClosePath();
            p.Fill();
        }
    }
}
