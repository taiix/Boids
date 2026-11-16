using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace FishGame
{
    /// <summary>
    /// Drives the UI Toolkit menus from a controller: the d-pad or stick moves between buttons, sliders,
    /// toggles and dropdowns, A presses, B backs out, and an orange frame shows where you are. Text boxes
    /// open Steam's on-screen keyboard. The mouse and keyboard keep working the way UI Toolkit does it.
    ///
    /// Each screen registers the part of the tree it owns (<see cref="Register"/>). The front-most one that's
    /// on screen is the only one the controller can move in, so nothing behind an open overlay can be
    /// reached, and its cancel action is what B does.
    /// </summary>
    public static class UiPad
    {
        /// <summary>Marks an element that isn't a button but should be reachable and pressable (see <see cref="MakePressable"/>).</summary>
        public const string PressableClass = "pad-pressable";

        class Scope
        {
            public VisualElement root;
            public Action cancel;
            public Func<VisualElement> first;
            public Func<bool> active;
            public int priority, order;
            public bool attached;
        }

        static readonly List<Scope> s_scopes = new List<Scope>();
        static readonly List<VisualElement> s_hooked = new List<VisualElement>();
        static readonly List<EventBase> s_mine = new List<EventBase>();   // events we sent ourselves, to let through the filter
        static int s_mineFrame;
        static readonly List<VisualElement> s_candidates = new List<VisualElement>();
        static int s_order;
        static VisualElement s_ring;
        static VisualElement s_grown;   // the control currently swollen under the ring
        static float s_grow;            // 0..1, how far its swell has got
        /// <summary>On the control the controller is on, so a style sheet can recolour it (":focus" also matches a mouse click).</summary>
        public const string FocusClass = "pad-focus";
        static Scope s_top;
        static Vector2 s_heldDir;
        static float s_nextRepeat;

        const float RepeatDelay = 0.4f, RepeatRate = 0.12f;
        static readonly Color RingColor = new Color(1f, 0.72f, 0.3f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_scopes.Clear();
            s_hooked.Clear();
            s_mine.Clear();
            s_ring = null;
            s_grown = null;
            s_top = null;
            s_heldDir = Vector2.zero;
        }

        /// <summary>True while some registered screen is up: the controller is in a menu.</summary>
        public static bool HasActiveScope => Top() != null;

        /// <summary>
        /// Let the controller move around <paramref name="root"/> while it's on screen.
        /// </summary>
        /// <param name="cancel">What B does here (close the overlay, resume). Null: nothing.</param>
        /// <param name="first">Where the frame starts. Null: the first control in the tree.</param>
        /// <param name="priority">Higher wins when several are on screen: 0 a screen, 10 an overlay on it, 20+ the pause menu.</param>
        /// <param name="active">Extra condition for "on screen" when the element isn't simply hidden with display.</param>
        public static void Register(VisualElement root, Action cancel = null, Func<VisualElement> first = null, int priority = 0, Func<bool> active = null)
        {
            if (root == null) return;
            Unregister(root);
            s_scopes.Add(new Scope { root = root, cancel = cancel, first = first, priority = priority, active = active, order = ++s_order });
        }

        public static void Unregister(VisualElement root) => s_scopes.RemoveAll(s => s.root == root);

        /// <summary>Make something that only answers to a click (a card, a link) reachable with the controller
        /// and pressable with A.</summary>
        public static void MakePressable(VisualElement element, Action onPress)
        {
            if (element == null) return;
            element.focusable = true;
            element.AddToClassList(PressableClass);
            element.RegisterCallback<NavigationSubmitEvent>(e => { onPress?.Invoke(); e.StopPropagation(); });
        }

        // ------------------------------------------------------------------ per frame

        internal static void Tick()
        {
            if (s_mine.Count > 0 && Time.frameCount > s_mineFrame + 1) s_mine.Clear();
            s_top = Top();
            if (s_top == null) { s_heldDir = Vector2.zero; return; }

            // The launch / loading overlay is up: the screen under it can't be seen, so it can't be used
            // either. Let go of whatever was selected, so nothing gets pressed blind.
            if (ReefRun.LaunchOverlay.IsPlaying)
            {
                (s_top.root.panel.focusController.focusedElement as VisualElement)?.Blur();
                Hook(s_top.root.panel);   // keeps UI Toolkit's own gamepad navigation filtered out meanwhile
                s_top = null;
                s_heldDir = Vector2.zero;
                return;
            }

            var panel = s_top.root.panel;
            Hook(panel);
            var focusController = panel.focusController;
            var focused = focusController.focusedElement as VisualElement;
            bool popup = PopupOpen(panel);
            bool inScope = focused != null && IsShown(focused) && (focused == s_top.root || s_top.root.Contains(focused));

            // On a controller there's always somewhere you are.
            if (GameInput.UsingPad && !inScope && !popup)
            {
                FocusFirst(s_top);
                focused = focusController.focusedElement as VisualElement;
                inScope = focused != null && s_top.root.Contains(focused);
            }

            Vector2 dir = ReadDirection();
            bool submit = GameInput.MenuSelect.WasPressedThisFrame();
            bool cancel = GameInput.MenuCancel.WasPressedThisFrame();
            if (dir == Vector2.zero && !submit && !cancel) return;

            // An open dropdown list is UI Toolkit's own: hand it the same events a keyboard would.
            if (popup)
            {
                var target = focused ?? panel.visualTree;
                if (dir != Vector2.zero) Send(NavigationMoveEvent.GetPooled(ToDirection(dir)), target);
                if (submit) Send(NavigationSubmitEvent.GetPooled(), target);
                if (cancel) Send(NavigationCancelEvent.GetPooled(), target);
                return;
            }

            if (cancel) { s_top.cancel?.Invoke(); return; }
            if (!inScope) return;

            if (dir != Vector2.zero)
            {
                var slider = Ancestor<Slider>(focused);
                if (slider != null && dir.x != 0f)
                    slider.value = Mathf.Clamp(slider.value + dir.x * (slider.highValue - slider.lowValue) * 0.05f, slider.lowValue, slider.highValue);
                else
                    Move(s_top, focused, dir);
            }

            if (submit)
            {
                var text = Ancestor<TextField>(focused);
                if (text != null) OpenKeyboard(text);
                else Send(NavigationSubmitEvent.GetPooled(), focused);
            }
        }

        internal static void LateTick() => UpdateRing();

        // The front-most registered screen that's actually showing.
        static Scope Top()
        {
            Scope best = null;
            for (int i = s_scopes.Count - 1; i >= 0; i--)
            {
                var s = s_scopes[i];
                if (s.root.panel != null) s.attached = true;
                else
                {
                    if (s.attached) s_scopes.RemoveAt(i);   // its screen was torn down
                    continue;
                }
                if (!IsShown(s.root) || (s.active != null && !s.active())) continue;
                if (best == null || s.priority > best.priority || (s.priority == best.priority && s.order > best.order)) best = s;
            }
            return best;
        }

        static bool IsShown(VisualElement e)
        {
            if (e == null || e.panel == null) return false;
            for (var x = e; x != null; x = x.hierarchy.parent)
                if (x.resolvedStyle.display == DisplayStyle.None || x.resolvedStyle.visibility == Visibility.Hidden) return false;
            return true;
        }

        static bool PopupOpen(IPanel panel)
        {
            var popup = panel.visualTree.Q(className: "unity-base-dropdown");
            return popup != null && IsShown(popup);
        }

        // ------------------------------------------------------------------ direction, with key repeat

        static Vector2 ReadDirection()
        {
            Vector2 held = GameInput.MenuUp.IsPressed() ? Vector2.up
                         : GameInput.MenuDown.IsPressed() ? Vector2.down
                         : GameInput.MenuLeft.IsPressed() ? Vector2.left
                         : GameInput.MenuRight.IsPressed() ? Vector2.right
                         : Vector2.zero;
            float now = Time.unscaledTime;
            if (held == Vector2.zero) { s_heldDir = held; return held; }
            if (held != s_heldDir)
            {
                s_heldDir = held;
                s_nextRepeat = now + RepeatDelay;
                return held;
            }
            if (now < s_nextRepeat) return Vector2.zero;
            s_nextRepeat = now + RepeatRate;
            return held;
        }

        static NavigationMoveEvent.Direction ToDirection(Vector2 d) =>
            d.y > 0f ? NavigationMoveEvent.Direction.Up
            : d.y < 0f ? NavigationMoveEvent.Direction.Down
            : d.x < 0f ? NavigationMoveEvent.Direction.Left
            : NavigationMoveEvent.Direction.Right;

        // ------------------------------------------------------------------ moving

        static bool IsControl(VisualElement e) =>
            e is Button || e is Toggle || e is Slider || e is SliderInt || e is DropdownField || e is TextField || e.ClassListContains(PressableClass);

        static void Gather(Scope scope)
        {
            s_candidates.Clear();
            scope.root.Query<VisualElement>().ForEach(e =>
            {
                if (IsControl(e) && e.enabledInHierarchy && IsShown(e)) s_candidates.Add(e);
            });
        }

        static void FocusFirst(Scope scope)
        {
            var first = scope.first?.Invoke();
            if (first == null || !first.enabledInHierarchy || !IsShown(first))
            {
                Gather(scope);
                first = s_candidates.Count > 0 ? s_candidates[0] : null;
            }
            if (first != null) Focus(first);
        }

        static void Focus(VisualElement e)
        {
            e.Focus();
            for (var p = e.hierarchy.parent; p != null; p = p.hierarchy.parent)
                if (p is ScrollView scroll) scroll.ScrollTo(e);
        }

        // The control that owns the focused element (a text box's or slider's inner part has the focus, not it).
        static VisualElement ControlOf(VisualElement focused)
        {
            for (var x = focused; x != null; x = x.hierarchy.parent)
                if (IsControl(x)) return x;
            return focused;
        }

        static T Ancestor<T>(VisualElement e) where T : VisualElement
        {
            for (var x = e; x != null; x = x.hierarchy.parent)
                if (x is T t) return t;
            return null;
        }

        // The nearest control in that direction: mostly how far along it, with anything off to the side
        // counting for more, so "down" goes to what's below rather than what's merely close.
        static void Move(Scope scope, VisualElement focused, Vector2 dir)
        {
            var from = ControlOf(focused);
            Rect a = from.worldBound;
            Vector2 d = new Vector2(dir.x, -dir.y);   // UI Toolkit's y runs down the screen
            Gather(scope);

            VisualElement best = null;
            float bestScore = float.MaxValue;
            foreach (var c in s_candidates)
            {
                if (c == from || c.Contains(from) || from.Contains(c)) continue;
                Rect b = c.worldBound;
                Vector2 delta = b.center - a.center;
                float along = Vector2.Dot(delta, d);
                if (along <= 1f) continue;
                // Gap between the two boxes along the way, and how far they miss each other sideways.
                float gap = d.x != 0f
                    ? (d.x > 0f ? b.xMin - a.xMax : a.xMin - b.xMax)
                    : (d.y > 0f ? b.yMin - a.yMax : a.yMin - b.yMax);
                float side = d.x != 0f
                    ? Mathf.Max(0f, Mathf.Max(b.yMin - a.yMax, a.yMin - b.yMax))
                    : Mathf.Max(0f, Mathf.Max(b.xMin - a.xMax, a.xMin - b.xMax));
                float sideCentre = d.x != 0f ? Mathf.Abs(delta.y) : Mathf.Abs(delta.x);
                float score = Mathf.Max(gap, 0f) + along * 0.25f + side * 4f + sideCentre * 0.5f;
                if (score < bestScore) { bestScore = score; best = c; }
            }

            if (best != null) { Focus(best); return; }

            // Nothing that way: if there's a list here, scroll it instead (credits, a long friends list).
            if (dir.y != 0f)
            {
                var scroll = Ancestor<ScrollView>(from) ?? scope.root.Q<ScrollView>();
                if (scroll != null && IsShown(scroll))
                    scroll.scrollOffset += new Vector2(0f, -dir.y * 140f);
            }
        }

        // ------------------------------------------------------------------ events

        static void Send(EventBase e, VisualElement target)
        {
            using (e)
            {
                e.target = target;
                s_mine.Add(e);
                s_mineFrame = Time.frameCount;
                target.SendEvent(e);
            }
        }

        // UI Toolkit also turns an Input System gamepad into navigation events of its own. Those would move
        // the frame a second time, and ignore which overlay is in front, so they're dropped here; the
        // keyboard's (arrows, Tab, Enter, Space, Esc) pass.
        static void Hook(IPanel panel)
        {
            var tree = panel.visualTree;
            if (s_hooked.Contains(tree)) return;
            s_hooked.RemoveAll(t => t.panel == null);
            s_hooked.Add(tree);
            tree.RegisterCallback<NavigationMoveEvent>(Filter, TrickleDown.TrickleDown);
            tree.RegisterCallback<NavigationSubmitEvent>(Filter, TrickleDown.TrickleDown);
            tree.RegisterCallback<NavigationCancelEvent>(Filter, TrickleDown.TrickleDown);
        }

        static void Filter(EventBase e)
        {
            if (s_mine.Remove(e)) return;
            if (FromKeyboard()) return;
            e.StopImmediatePropagation();
            (e.target as VisualElement)?.focusController?.IgnoreEvent(e);
        }

        static bool FromKeyboard()
        {
            var kb = Keyboard.current;
            if (kb == null) return false;
            return kb.upArrowKey.isPressed || kb.downArrowKey.isPressed || kb.leftArrowKey.isPressed || kb.rightArrowKey.isPressed
                || kb.tabKey.isPressed || kb.enterKey.isPressed || kb.numpadEnterKey.isPressed || kb.spaceKey.isPressed || kb.escapeKey.isPressed
                || kb.tabKey.wasReleasedThisFrame || kb.enterKey.wasReleasedThisFrame || kb.numpadEnterKey.wasReleasedThisFrame
                || kb.spaceKey.wasReleasedThisFrame || kb.escapeKey.wasReleasedThisFrame;
        }

        // ------------------------------------------------------------------ text entry

        static void OpenKeyboard(TextField field)
        {
            // Where the box is on the real screen, so Steam's floating keyboard keeps clear of it.
            Rect r = field.worldBound;
            float scale = field.panel.visualTree.layout.height > 0f ? Screen.height / field.panel.visualTree.layout.height : 1f;
            var onScreen = new RectInt(Mathf.RoundToInt(r.x * scale), Mathf.RoundToInt(r.y * scale),
                                       Mathf.RoundToInt(r.width * scale), Mathf.RoundToInt(r.height * scale));
            int max = field.maxLength > 0 ? field.maxLength : 64;
            string what = !string.IsNullOrEmpty(field.label) ? field.label : (field.tooltip ?? "");
            SteamInputService.ShowKeyboard(what, field.value, max, onScreen, text => field.value = text);
        }

        // ------------------------------------------------------------------ the frame around where you are

        static void UpdateRing()
        {
            VisualElement target = null;
            if (GameInput.UsingPad && s_top != null && s_top.root.panel != null && !PopupOpen(s_top.root.panel))
            {
                var focused = s_top.root.panel.focusController.focusedElement as VisualElement;
                if (focused != null && s_top.root.Contains(focused) && IsShown(focused)) target = ControlOf(focused);
            }

            // The selected control swells a little and takes the "pad-focus" class (the sheets recolour it).
            if (target != s_grown)
            {
                if (s_grown != null)
                {
                    s_grown.style.scale = StyleKeyword.Null;
                    s_grown.RemoveFromClassList(FocusClass);
                }
                s_grown = target;
                s_grow = 0f;
                target?.AddToClassList(FocusClass);
            }

            if (target == null)
            {
                if (s_ring != null) s_ring.style.display = DisplayStyle.None;
                return;
            }

            s_grow = Mathf.MoveTowards(s_grow, 1f, Time.unscaledDeltaTime / 0.12f);
            // Buttons grow the most; a wide slider or dropdown row only a touch, or it would spill off its panel.
            float grow = target is Button || target.ClassListContains(PressableClass) ? 0.07f : 0.025f;
            float eased = 1f - (1f - s_grow) * (1f - s_grow);
            float pulse = 1f + 0.012f * Mathf.Sin(Time.unscaledTime * 5f) * s_grow;
            float k = (1f + grow * eased) * pulse;
            target.style.scale = new Scale(new Vector3(k, k, 1f));

            // A slab button frames itself along its own leaning edge (--slab-outline on .pad-focus): no box round it.
            if (target.Q<ReefRun.ReefSlab>() != null)
            {
                if (s_ring != null) s_ring.style.display = DisplayStyle.None;
                return;
            }

            if (s_ring == null)
            {
                s_ring = new VisualElement { name = "pad-focus-ring", pickingMode = PickingMode.Ignore };
                s_ring.style.position = Position.Absolute;
                s_ring.style.backgroundColor = new Color(RingColor.r, RingColor.g, RingColor.b, 0.14f);   // a wash of the ring's colour over it
                s_ring.style.borderTopWidth = s_ring.style.borderBottomWidth = s_ring.style.borderLeftWidth = s_ring.style.borderRightWidth = 3;
                s_ring.style.borderTopColor = s_ring.style.borderBottomColor = s_ring.style.borderLeftColor = s_ring.style.borderRightColor = RingColor;
                s_ring.style.borderTopLeftRadius = s_ring.style.borderTopRightRadius = s_ring.style.borderBottomLeftRadius = s_ring.style.borderBottomRightRadius = 6;
            }

            // On top of everything in the panel the focused control is in.
            var tree = target.panel.visualTree;
            if (s_ring.parent != tree) tree.Add(s_ring);
            else if (tree.IndexOf(s_ring) != tree.childCount - 1) s_ring.BringToFront();

            Rect box = tree.WorldToLocal(target.worldBound);
            const float pad = 5f;
            s_ring.style.display = DisplayStyle.Flex;
            s_ring.style.left = box.x - pad;
            s_ring.style.top = box.y - pad;
            s_ring.style.width = box.width + pad * 2f;
            s_ring.style.height = box.height + pad * 2f;
        }
    }
}
