using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    // Shared colours of the dark lunar terminal. USS mirrors these values.
    public static class TerminalPalette
    {
        public static readonly Color Ink = new Color(.027f, .031f, .039f), Panel = new Color(.07f, .075f, .09f), Text = new Color(.925f, .925f, .902f);
        public static readonly Color Yellow = new Color(1, .882f, .102f), Cyan = new Color(.247f, .878f, .816f), Red = new Color(1, .302f, .227f), Green = new Color(.247f, .878f, .478f);
        public static readonly Color Line = new Color(1, 1, 1, .12f);
        public static Color A(Color c, float alpha) { c.a *= alpha; return c; }
    }

    // Tweens driven by element schedulers. Each animation restores inline styles when it ends so layout checks see the resting state.
    public static class TerminalMotion
    {
        private const string k_Glyphs = "ABCDEFGHJKLMNPQRSTUVWXYZ0123456789/#<>";
        private static readonly Dictionary<(VisualElement, string), IVisualElementScheduledItem> s_Channels = new Dictionary<(VisualElement, string), IVisualElementScheduledItem>();
        public static double Now => Time.realtimeSinceStartupAsDouble;
        public static float OutExpo(float t) => t >= 1 ? 1 : 1 - Mathf.Pow(2, -10 * t);
        public static float OutCubic(float t) { t = 1 - Mathf.Clamp01(t); return 1 - t * t * t; }
        public static float OutBack(float t) { const float c1 = 1.70158f, c3 = c1 + 1; t = Mathf.Clamp01(t) - 1; return 1 + c3 * t * t * t + c1 * t * t; }
        public static float InOutCubic(float t) { t = Mathf.Clamp01(t); return t < .5f ? 4 * t * t * t : 1 - Mathf.Pow(-2 * t + 2, 3) / 2; }
        public static float Window(float age, float delay, float duration) => Mathf.Clamp01((age - delay) / Mathf.Max(.0001f, duration));

        public static IVisualElementScheduledItem Tween(VisualElement owner, float duration, Action<float> step, float delay = 0, Action done = null, string channel = null)
        {
            if (channel != null && s_Channels.TryGetValue((owner, channel), out var previous)) { previous.Pause(); s_Channels.Remove((owner, channel)); }
            double start = Now + delay; step(0); IVisualElementScheduledItem item = null;
            item = owner.schedule.Execute(() =>
            {
                if (Now < start) return;
                float t = duration <= 0 ? 1 : Mathf.Clamp01((float)((Now - start) / duration)); step(t);
                if (t < 1) return;
                item.Pause(); if (channel != null) s_Channels.Remove((owner, channel)); done?.Invoke();
            }).Every(16);
            if (channel != null) s_Channels[(owner, channel)] = item;
            return item;
        }
        public static void Reveal(VisualElement e, float delay = 0, float dx = 0, float dy = 18, float duration = .55f)
        {
            Tween(e, duration, t => { float k = OutExpo(t); e.style.opacity = k; e.style.translate = new Translate(dx * (1 - k), dy * (1 - k)); }, delay,
                () => { e.style.opacity = StyleKeyword.Null; e.style.translate = StyleKeyword.Null; }, "reveal");
        }
        // Opacity only: used where tests read worldBound.
        public static void Fade(VisualElement e, float delay = 0, float duration = .35f)
        {
            Tween(e, duration, t => e.style.opacity = OutCubic(t), delay, () => e.style.opacity = StyleKeyword.Null, "reveal");
        }
        public static float Cascade(VisualElement parent, float delay = 0, float stagger = .06f, float dx = 0, float dy = 18)
        {
            foreach (var child in parent.Children())
            {
                if (child is AnimatedPainter || child.resolvedStyle.display == DisplayStyle.None) continue;
                Reveal(child, delay, dx, dy); delay += stagger;
            }
            return delay;
        }
        public static void Scramble(Label label, float delay = 0, float duration = .6f)
        {
            string target = label.text ?? ""; var chars = new char[target.Length]; int seed = 0;
            Tween(label, duration, t =>
            {
                int solved = Mathf.FloorToInt(target.Length * OutCubic(t)); seed++;
                for (int i = 0; i < target.Length; i++)
                    chars[i] = i < solved || target[i] == ' ' || target[i] == '/' ? target[i] : k_Glyphs[(i * 7 + seed * 13 + (i * seed) % 11) % k_Glyphs.Length];
                label.text = new string(chars);
            }, delay, () => label.text = target, "text");
        }
        public static void CountUp(Label label, float target, string format = "{0:N0}", float delay = 0, float duration = 1.1f)
        {
            Tween(label, duration, t => label.text = string.Format(format, Mathf.Round(target * OutExpo(t))), delay, () => label.text = string.Format(format, target), "text");
        }
        public static void Wipe(VisualElement host, string caption)
        {
            var wipe = new PageWipe(caption); host.Add(wipe); wipe.Play();
        }
        internal static void Ellipse(Painter2D p, Vector2 c, float rx, float ry)
        {
            const float k = .5523f; float ox = rx * k, oy = ry * k;
            p.MoveTo(c + new Vector2(-rx, 0));
            p.BezierCurveTo(c + new Vector2(-rx, -oy), c + new Vector2(-ox, -ry), c + new Vector2(0, -ry));
            p.BezierCurveTo(c + new Vector2(ox, -ry), c + new Vector2(rx, -oy), c + new Vector2(rx, 0));
            p.BezierCurveTo(c + new Vector2(rx, oy), c + new Vector2(ox, ry), c + new Vector2(0, ry));
            p.BezierCurveTo(c + new Vector2(-ox, ry), c + new Vector2(-rx, oy), c + new Vector2(-rx, 0));
            p.ClosePath();
        }
        internal static void Rect(Painter2D p, float x, float y, float w, float h, Color color)
        {
            p.fillGradient = default; p.fillColor = color; p.BeginPath();
            p.MoveTo(new Vector2(x, y)); p.LineTo(new Vector2(x + w, y)); p.LineTo(new Vector2(x + w, y + h)); p.LineTo(new Vector2(x, y + h)); p.ClosePath(); p.Fill();
        }
        internal static void GradientRect(Painter2D p, float x, float y, float w, float h, Color from, Color to, bool vertical = true)
        {
            p.fillGradient = TerminalMotion.Linear(from, to, new Vector2(x, y), vertical ? new Vector2(x, y + h) : new Vector2(x + w, y), AddressMode.Clamp);
            p.BeginPath(); p.MoveTo(new Vector2(x, y)); p.LineTo(new Vector2(x + w, y)); p.LineTo(new Vector2(x + w, y + h)); p.LineTo(new Vector2(x, y + h)); p.ClosePath(); p.Fill();
            p.fillGradient = default;
        }
        // Colour overloads of FillGradient drop alpha, so gradients go through explicit alpha keys.
        internal static Gradient Keys(Color a, Color b)
        {
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(a, 0), new GradientColorKey(b, 1) }, new[] { new GradientAlphaKey(a.a, 0), new GradientAlphaKey(b.a, 1) }); return g;
        }
        internal static FillGradient Linear(Color a, Color b, Vector2 from, Vector2 to, AddressMode mode = AddressMode.Clamp) => FillGradient.MakeLinearGradient(Keys(a, b), from, to, mode);
        internal static FillGradient Radial(Color a, Color b, Vector2 center, float radius, Vector2 focus, AddressMode mode = AddressMode.Clamp) => FillGradient.MakeRadialGradient(Keys(a, b), center, radius, focus, mode);
        internal static void Line(Painter2D p, Vector2 a, Vector2 b, Color color, float width = 1)
        {
            p.strokeColor = color; p.lineWidth = width; p.BeginPath(); p.MoveTo(a); p.LineTo(b); p.Stroke();
        }
        internal static void Circle(Painter2D p, Vector2 c, float r, Color color)
        {
            p.fillGradient = default; p.fillColor = color; p.BeginPath(); p.Arc(c, r, 0, 360); p.Fill();
        }
        internal static void Ring(Painter2D p, Vector2 c, float r, Color color, float width = 1, float from = 0, float to = 360)
        {
            p.strokeColor = color; p.lineWidth = width; p.BeginPath(); p.Arc(c, r, from, to); p.Stroke();
        }
        internal static void Dashed(Painter2D p, float dash, float gap, float offset) { p.SetDashPattern(dash, gap); p.dashOffset = offset; }
        internal static void Solid(Painter2D p) { p.SetDashPattern(ReadOnlySpan<float>.Empty); p.dashOffset = 0; }
        internal static void Diamond(Painter2D p, Vector2 c, float r, Color color)
        {
            p.fillGradient = default; p.fillColor = color; p.BeginPath();
            p.MoveTo(c + new Vector2(0, -r)); p.LineTo(c + new Vector2(r, 0)); p.LineTo(c + new Vector2(0, r)); p.LineTo(c + new Vector2(-r, 0)); p.ClosePath(); p.Fill();
        }
        internal static float Hash(int n) { n = (n << 13) ^ n; return 1f - ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / 1073741824f * .5f; }
    }

    // Base for decorative vector layers. Repaints on its own scheduler while attached and while Animating is true.
    public abstract class AnimatedPainter : VisualElement
    {
        private IVisualElementScheduledItem m_Tick;
        private double m_Born;
        protected int m_Interval = 33;
        protected float Age => (float)(TerminalMotion.Now - m_Born);
        protected virtual bool Animating => true;
        protected AnimatedPainter()
        {
            pickingMode = PickingMode.Ignore; m_Born = TerminalMotion.Now;
            generateVisualContent += ctx => { var r = contentRect; if (r.width >= 1 && r.height >= 1) Draw(ctx.painter2D, r.width, r.height); };
            RegisterCallback<AttachToPanelEvent>(_ => { if (m_Tick == null) m_Tick = schedule.Execute(Tick).Every(m_Interval); else m_Tick.Resume(); });
            RegisterCallback<DetachFromPanelEvent>(_ => m_Tick?.Pause());
        }
        private void Tick() { if (Animating) MarkDirtyRepaint(); }
        public void Restart() { m_Born = TerminalMotion.Now; MarkDirtyRepaint(); }
        protected abstract void Draw(Painter2D p, float w, float h);
    }

    // Chamfered panel frame that draws its border in, then rests.
    public sealed class TechFrame : AnimatedPainter
    {
        private static readonly CustomStyleProperty<Color> s_Fill = new CustomStyleProperty<Color>("--frame-fill"), s_Line = new CustomStyleProperty<Color>("--frame-line"), s_Accent = new CustomStyleProperty<Color>("--frame-accent");
        private static readonly CustomStyleProperty<float> s_Cut = new CustomStyleProperty<float>("--frame-cut");
        private Color m_Fill = new Color(.06f, .066f, .08f, .9f), m_Line = new Color(1, 1, 1, .14f), m_Accent = TerminalPalette.Yellow;
        private float m_Cut = 16;
        private readonly float m_Delay;
        protected override bool Animating => Age < m_Delay + 1.4f;
        public TechFrame(float delay = 0)
        {
            m_Delay = delay; AddToClassList("tech-frame");
            style.position = Position.Absolute; style.left = 0; style.top = 0; style.right = 0; style.bottom = 0;
            RegisterCallback<CustomStyleResolvedEvent>(e =>
            {
                if (e.customStyle.TryGetValue(s_Fill, out var fill)) m_Fill = fill;
                if (e.customStyle.TryGetValue(s_Line, out var line)) m_Line = line;
                if (e.customStyle.TryGetValue(s_Accent, out var accent)) m_Accent = accent;
                if (e.customStyle.TryGetValue(s_Cut, out var cut)) m_Cut = cut;
                MarkDirtyRepaint();
            });
        }
        public static TechFrame Attach(VisualElement panel, float delay = 0) { var frame = new TechFrame(delay); panel.Insert(0, frame); return frame; }
        protected override void Draw(Painter2D p, float w, float h)
        {
            float c = Mathf.Min(m_Cut, w * .3f, h * .3f), age = Age - m_Delay;
            float fade = TerminalMotion.OutCubic(age / .45f), draw = TerminalMotion.OutCubic(age / .75f);
            var pts = new[] { new Vector2(0, 0), new Vector2(w - c, 0), new Vector2(w, c), new Vector2(w, h), new Vector2(c, h), new Vector2(0, h - c), new Vector2(0, 0) };
            p.fillGradient = default; p.fillColor = TerminalPalette.A(m_Fill, fade); p.BeginPath(); p.MoveTo(pts[0]);
            for (int i = 1; i < pts.Length - 1; i++) p.LineTo(pts[i]);
            p.ClosePath(); p.Fill();
            if (age > .2f && age < 1.2f)
            {
                p.PushClip();
                float g = TerminalMotion.InOutCubic((age - .2f) / 1f), x = Mathf.Lerp(-w * .3f, w * 1.3f, g);
                p.fillGradient = TerminalMotion.Linear(new Color(1, 1, 1, 0), new Color(1, 1, 1, .07f), new Vector2(x - 80, 0), new Vector2(x, 0), AddressMode.Mirror);
                p.BeginPath(); p.MoveTo(new Vector2(x - 80, 0)); p.LineTo(new Vector2(x + 80, 0)); p.LineTo(new Vector2(x + 80 - h * .6f, h)); p.LineTo(new Vector2(x - 80 - h * .6f, h)); p.ClosePath(); p.Fill();
                p.fillGradient = default; p.PopClip();
            }
            float total = 0; for (int i = 1; i < pts.Length; i++) total += Vector2.Distance(pts[i - 1], pts[i]);
            float left = total * draw; p.strokeColor = m_Line; p.lineWidth = 1; p.lineJoin = LineJoin.Miter; p.BeginPath(); p.MoveTo(pts[0]);
            for (int i = 1; i < pts.Length && left > 0; i++)
            {
                float len = Vector2.Distance(pts[i - 1], pts[i]);
                p.LineTo(len <= left ? pts[i] : Vector2.Lerp(pts[i - 1], pts[i], left / len)); left -= len;
            }
            p.Stroke();
            float bar = 48 * TerminalMotion.OutExpo(age / .6f);
            TerminalMotion.Rect(p, 0, 0, bar, 3, m_Accent);
            TerminalMotion.Rect(p, w - c - 14, 5, 5, 5, TerminalPalette.A(m_Accent, fade));
            var corner = TerminalPalette.A(m_Accent, .8f * fade); p.strokeColor = corner; p.lineWidth = 1.5f;
            p.BeginPath(); p.MoveTo(new Vector2(w - 12, h - 1)); p.LineTo(new Vector2(w - 1, h - 1)); p.LineTo(new Vector2(w - 1, h - 12)); p.Stroke();
            p.strokeColor = TerminalPalette.A(m_Line, fade); p.lineWidth = 1; p.BeginPath();
            for (int i = 0; i < 6; i++) { float x = c + 10 + i * 5; p.MoveTo(new Vector2(x, h - 6)); p.LineTo(new Vector2(x, h - 2)); }
            p.Stroke();
        }
    }

    public enum BackdropMode { Ship, Page, Login }

    // Full-screen moon, orbits, horizon grid and dust behind the terminal.
    public sealed class LunarBackdrop : AnimatedPainter
    {
        private BackdropMode m_Mode;
        private readonly Label m_Tag, m_Coords;
        private Vector2 m_Center; private float m_Radius;
        public BackdropMode Mode { get => m_Mode; set { if (m_Mode == value) return; m_Mode = value; Restart(); Place(); } }
        public LunarBackdrop(BackdropMode mode)
        {
            m_Mode = mode; m_Interval = 33; AddToClassList("lunar-backdrop");
            m_Tag = new Label("SELENE  /  384,400 KM") { pickingMode = PickingMode.Ignore }; m_Tag.AddToClassList("lunar-backdrop-tag"); Add(m_Tag);
            m_Coords = new Label("LAT 26.13N  LON 3.63E\nMARE IMBRIUM / SECTOR 07") { pickingMode = PickingMode.Ignore }; m_Coords.AddToClassList("lunar-backdrop-coords"); Add(m_Coords);
            RegisterCallback<GeometryChangedEvent>(_ => Place());
        }
        private void Layout(float w, float h)
        {
            switch (m_Mode)
            {
                case BackdropMode.Ship: m_Center = new Vector2(w * .6f, h * .43f); m_Radius = h * .33f; break;
                case BackdropMode.Login: m_Center = new Vector2(w * .68f, h * .46f); m_Radius = h * .4f; break;
                default: m_Center = new Vector2(w * .9f, h * .16f); m_Radius = h * .26f; break;
            }
        }
        private void Place()
        {
            var r = contentRect; if (r.width < 1) return; Layout(r.width, r.height);
            m_Tag.style.left = m_Center.x + m_Radius * 1.05f; m_Tag.style.top = m_Center.y - m_Radius * 1.12f;
            m_Coords.style.left = Mathf.Min(m_Center.x + m_Radius * 1.05f, r.width - 280); m_Coords.style.top = m_Center.y + m_Radius * .95f;
            bool show = m_Mode != BackdropMode.Page; m_Tag.style.display = show ? DisplayStyle.Flex : DisplayStyle.None; m_Coords.style.display = m_Tag.style.display;
        }
        private Vector2 Orbit(float theta, float rx, float ry, float tilt)
        {
            var q = new Vector2(Mathf.Cos(theta) * rx, Mathf.Sin(theta) * ry); float s = Mathf.Sin(tilt), co = Mathf.Cos(tilt);
            return m_Center + new Vector2(q.x * co - q.y * s, q.x * s + q.y * co);
        }
        private void DrawOrbit(Painter2D p, float rx, float ry, float tilt, bool front, Color color, float t)
        {
            float from = front ? 0 : Mathf.PI, to = from + Mathf.PI; TerminalMotion.Dashed(p, 6, 6, -t * 18);
            p.strokeColor = color; p.lineWidth = 1; p.BeginPath();
            for (int i = 0; i <= 48; i++) { var v = Orbit(Mathf.Lerp(from, to, i / 48f), rx, ry, tilt); if (i == 0) p.MoveTo(v); else p.LineTo(v); }
            p.Stroke(); TerminalMotion.Solid(p);
        }
        private void DrawSatellite(Painter2D p, float theta, float rx, float ry, float tilt, bool front, Color color)
        {
            float s = Mathf.Sin(theta); if (front != s >= 0) return;
            for (int i = 1; i <= 8; i++) TerminalMotion.Line(p, Orbit(theta - i * .045f, rx, ry, tilt), Orbit(theta - (i - 1) * .045f, rx, ry, tilt), TerminalPalette.A(color, (1 - i / 9f) * .8f), 1.5f);
            var pos = Orbit(theta, rx, ry, tilt); TerminalMotion.Diamond(p, pos, 4, color);
            TerminalMotion.Ring(p, pos, 9, TerminalPalette.A(color, .35f), 1);
        }
        protected override void Draw(Painter2D p, float w, float h)
        {
            Layout(w, h); float t = Age, intro = TerminalMotion.OutExpo(t / 1.6f); var c = m_Center; float R = m_Radius * Mathf.Lerp(.92f, 1, intro);
            bool page = m_Mode == BackdropMode.Page; float a = page ? .55f : 1;
            TerminalMotion.GradientRect(p, 0, 0, w, h, new Color(.04f, .046f, .062f), new Color(.012f, .014f, .02f));
            if (page)
            {
                p.strokeColor = new Color(1, 1, 1, .035f); p.lineWidth = 1; p.BeginPath();
                for (float x = 0; x < w; x += 64) { p.MoveTo(new Vector2(x, 0)); p.LineTo(new Vector2(x, h)); }
                for (float y = 0; y < h; y += 64) { p.MoveTo(new Vector2(0, y)); p.LineTo(new Vector2(w, y)); }
                p.Stroke();
                p.fillGradient = default; p.fillColor = new Color(1, 1, 1, .08f); p.BeginPath();
                for (float x = 0; x < w; x += 64) for (float y = 0; y < h; y += 64) { p.MoveTo(new Vector2(x - 1, y)); p.LineTo(new Vector2(x + 1, y)); p.LineTo(new Vector2(x + 1, y + 1)); p.LineTo(new Vector2(x - 1, y + 1)); p.ClosePath(); }
                p.Fill();
            }
            p.fillGradient = TerminalMotion.Radial(TerminalPalette.A(TerminalPalette.Cyan, .13f * a * intro), new Color(.247f, .878f, .816f, 0), c, R * 2.1f, c, AddressMode.Clamp);
            p.BeginPath(); p.Arc(c, R * 2.1f, 0, 360); p.Fill(); p.fillGradient = default;
            float tilt = -14 * Mathf.Deg2Rad, rx = R * 1.75f, ry = R * .38f, rx2 = R * 2.25f, ry2 = R * .62f, tilt2 = 21 * Mathf.Deg2Rad;
            Color orbit = new Color(1, 1, 1, .16f * a * intro), orbit2 = TerminalPalette.A(TerminalPalette.Cyan, .22f * a * intro);
            DrawOrbit(p, rx, ry, tilt, false, orbit, t); DrawOrbit(p, rx2, ry2, tilt2, false, orbit2, -t);
            float s1 = t * .32f + 1, s2 = -t * .21f + 4;
            DrawSatellite(p, s1, rx, ry, tilt, false, TerminalPalette.A(TerminalPalette.Yellow, a)); DrawSatellite(p, s2, rx2, ry2, tilt2, false, TerminalPalette.A(TerminalPalette.Cyan, a));
            // disc
            var light = c + new Vector2(-R * .4f, -R * .45f);
            p.fillGradient = TerminalMotion.Radial(new Color(.3f, .31f, .33f, a), new Color(.05f, .055f, .065f, a), light, R * 1.55f, light, AddressMode.Clamp);
            p.BeginPath(); p.Arc(c, R, 0, 360); p.Fill(); p.fillGradient = default;
            p.BeginPath(); p.Arc(c, R, 0, 360); p.PushClip();
            float spin = t * .03f;
            for (int i = 0; i < 26; i++)
            {
                float lat = (TerminalMotion.Hash(i * 3 + 1) * 2 - 1) * 1.25f, lon = TerminalMotion.Hash(i * 3 + 2) * Mathf.PI * 2 + spin, size = .04f + TerminalMotion.Hash(i * 3 + 3) * .11f;
                float depth = Mathf.Cos(lon); if (depth < .05f) continue;
                var pos = c + new Vector2(R * Mathf.Cos(lat) * Mathf.Sin(lon), R * Mathf.Sin(lat)); float rr = size * R;
                p.fillColor = new Color(0, 0, 0, .28f * a * depth); p.BeginPath(); TerminalMotion.Ellipse(p, pos, rr * depth * Mathf.Cos(lat * .7f), rr * Mathf.Cos(lat * .5f)); p.Fill();
                p.strokeColor = new Color(1, 1, 1, .12f * a * depth); p.lineWidth = 1; p.BeginPath(); TerminalMotion.Ellipse(p, pos + new Vector2(rr * .12f, rr * .12f), rr * depth * Mathf.Cos(lat * .7f), rr * Mathf.Cos(lat * .5f)); p.Stroke();
            }
            p.strokeColor = TerminalPalette.A(TerminalPalette.Cyan, .09f * a); p.lineWidth = 1;
            for (int i = 0; i < 6; i++)
            {
                float lon = i * Mathf.PI / 6 + spin * 4 % (Mathf.PI / 6); float sx = Mathf.Abs(Mathf.Sin(lon)) * R;
                p.BeginPath(); TerminalMotion.Ellipse(p, c, Mathf.Max(1, sx), R); p.Stroke();
            }
            p.BeginPath();
            for (int i = -4; i <= 4; i++) { float yy = c.y + R * i / 5f, hw = Mathf.Sqrt(Mathf.Max(0, R * R - (yy - c.y) * (yy - c.y))); p.MoveTo(new Vector2(c.x - hw, yy)); p.LineTo(new Vector2(c.x + hw, yy)); }
            p.Stroke();
            p.fillGradient = default;
            p.fillColor = new Color(.01f, .012f, .018f, .5f * a); p.BeginPath(); p.Arc(c + new Vector2(R * .5f, R * .32f), R * 1.04f, 0, 360); p.Fill();
            p.fillColor = new Color(.01f, .012f, .018f, .45f * a); p.BeginPath(); p.Arc(c + new Vector2(R * .72f, R * .48f), R * .98f, 0, 360); p.Fill();
            float scan = Mathf.Repeat(t * .25f, 1.4f) - .2f; float sy = c.y - R + scan * R * 2;
            TerminalMotion.GradientRect(p, c.x - R, sy - 30, R * 2, 30, new Color(.247f, .878f, .816f, 0), TerminalPalette.A(TerminalPalette.Cyan, .16f * a));
            TerminalMotion.Rect(p, c.x - R, sy, R * 2, 1, TerminalPalette.A(TerminalPalette.Cyan, .5f * a));
            p.PopClip();
            TerminalMotion.Ring(p, c, R, new Color(1, 1, 1, .1f * a), 1);
            TerminalMotion.Ring(p, c, R + .5f, TerminalPalette.A(TerminalPalette.Cyan, .55f * a), 1.5f, 175, 290);
            // instrumentation rings
            float rot = t * 4, r1 = R * 1.14f; p.strokeColor = new Color(1, 1, 1, .22f * a * intro); p.lineWidth = 1; p.BeginPath();
            for (int i = 0; i < 120; i++)
            {
                float ang = (i * 3 + rot) * Mathf.Deg2Rad, len = i % 10 == 0 ? 10 : i % 5 == 0 ? 6 : 3; var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                p.MoveTo(c + dir * r1); p.LineTo(c + dir * (r1 + len));
            }
            p.Stroke();
            float rb = R * 1.24f, crot = -t * 9;
            for (int i = 0; i < 3; i++) TerminalMotion.Ring(p, c, rb, TerminalPalette.A(TerminalPalette.Yellow, .7f * a * intro), 2, crot + i * 120, crot + i * 120 + 38 * intro);
            TerminalMotion.Dashed(p, 2, 7, t * 10); TerminalMotion.Ring(p, c, R * 1.33f, new Color(1, 1, 1, .14f * a * intro), 1); TerminalMotion.Solid(p);
            var marker = c + new Vector2(Mathf.Cos((crot + 19) * Mathf.Deg2Rad), Mathf.Sin((crot + 19) * Mathf.Deg2Rad)) * rb;
            TerminalMotion.Diamond(p, marker, 4, TerminalPalette.A(TerminalPalette.Yellow, a * intro));
            if (!page)
            {
                var anchor = c + new Vector2(R * .74f, -R * .74f); var knee = anchor + new Vector2(R * .22f, -R * .22f);
                TerminalMotion.Line(p, anchor, knee, TerminalPalette.A(TerminalPalette.Yellow, .7f * intro)); TerminalMotion.Line(p, knee, knee + new Vector2(R * .7f * intro, 0), TerminalPalette.A(TerminalPalette.Yellow, .7f * intro));
                TerminalMotion.Circle(p, anchor, 3, TerminalPalette.Yellow);
                TerminalMotion.Ring(p, anchor, 6 + Mathf.Repeat(t * 14, 18), TerminalPalette.A(TerminalPalette.Yellow, 1 - Mathf.Repeat(t * 14, 18) / 18), 1);
            }
            DrawOrbit(p, rx, ry, tilt, true, orbit, t); DrawOrbit(p, rx2, ry2, tilt2, true, orbit2, -t);
            DrawSatellite(p, s1, rx, ry, tilt, true, TerminalPalette.A(TerminalPalette.Yellow, a)); DrawSatellite(p, s2, rx2, ry2, tilt2, true, TerminalPalette.A(TerminalPalette.Cyan, a));
            if (!page) Surface(p, w, h, t);
            // dust
            p.fillGradient = default;
            for (int i = 0; i < 70; i++)
            {
                float x = Mathf.Repeat(TerminalMotion.Hash(i * 5 + 11) * w + t * (4 + TerminalMotion.Hash(i) * 10), w), y = Mathf.Repeat(TerminalMotion.Hash(i * 5 + 12) * h - t * (2 + TerminalMotion.Hash(i + 3) * 6), h);
                float tw = .5f + .5f * Mathf.Sin(t * (1 + TerminalMotion.Hash(i + 7) * 2) + i), sz = 1 + TerminalMotion.Hash(i + 9) * 1.5f;
                TerminalMotion.Rect(p, x, y, sz, sz, new Color(1, 1, 1, .25f * tw * a));
            }
            float band = Mathf.Repeat(t / 7f, 1) * (h + 200) - 100;
            TerminalMotion.GradientRect(p, 0, band - 90, w, 90, new Color(.247f, .878f, .816f, 0), TerminalPalette.A(TerminalPalette.Cyan, .035f));
            p.fillGradient = TerminalMotion.Radial(new Color(0, 0, 0, 0), new Color(.008f, .01f, .014f, .82f), new Vector2(w * .5f, h * .5f), Mathf.Max(w, h) * .78f, new Vector2(w * .5f, h * .5f), AddressMode.Clamp);
            p.BeginPath(); p.MoveTo(Vector2.zero); p.LineTo(new Vector2(w, 0)); p.LineTo(new Vector2(w, h)); p.LineTo(new Vector2(0, h)); p.ClosePath(); p.Fill(); p.fillGradient = default;
        }
        private void Surface(Painter2D p, float w, float h, float t)
        {
            float y0 = h * .8f, vx = w * .5f;
            TerminalMotion.GradientRect(p, 0, y0 - 140, w, 140, new Color(.247f, .878f, .816f, 0), TerminalPalette.A(TerminalPalette.Cyan, .06f));
            TerminalMotion.GradientRect(p, 0, y0, w, h - y0, new Color(.06f, .066f, .078f), new Color(.016f, .018f, .024f));
            p.BeginPath(); p.MoveTo(new Vector2(0, y0)); p.LineTo(new Vector2(w, y0)); p.LineTo(new Vector2(w, h)); p.LineTo(new Vector2(0, h)); p.ClosePath(); p.PushClip();
            p.strokeColor = new Color(1, 1, 1, .07f); p.lineWidth = 1; p.BeginPath();
            for (int i = -24; i <= 24; i++) { p.MoveTo(new Vector2(vx + i * w * .012f, y0)); p.LineTo(new Vector2(vx + i * w * .11f, h)); }
            p.Stroke();
            float shift = Mathf.Repeat(t * .6f, 1);
            for (int k = 0; k < 14; k++)
            {
                float z = 1 + k - shift; if (z <= .2f) continue; float y = y0 + (h - y0) * .9f / z;
                TerminalMotion.Line(p, new Vector2(0, y), new Vector2(w, y), new Color(1, 1, 1, Mathf.Clamp01(.16f / Mathf.Sqrt(z))));
            }
            p.PopClip();
            TerminalMotion.Line(p, new Vector2(0, y0), new Vector2(w, y0), new Color(1, 1, 1, .28f));
            TerminalMotion.Line(p, new Vector2(w * .08f, y0), new Vector2(w * .08f + 180, y0), TerminalPalette.A(TerminalPalette.Yellow, .9f), 2);
            p.strokeColor = new Color(1, 1, 1, .3f); p.lineWidth = 1; p.BeginPath();
            for (int i = 0; i < 40; i++) { float x = w * .08f + i * 24; p.MoveTo(new Vector2(x, y0 + 4)); p.LineTo(new Vector2(x, y0 + (i % 5 == 0 ? 12 : 7))); }
            p.Stroke();
        }
    }

    // Diagonal yellow / ink bands that sweep across the screen on page change.
    public sealed class PageWipe : VisualElement
    {
        private readonly Label m_Caption;
        private float m_T;
        public PageWipe(string caption)
        {
            pickingMode = PickingMode.Ignore; AddToClassList("terminal-wipe");
            style.position = Position.Absolute; style.left = 0; style.top = 0; style.right = 0; style.bottom = 0;
            m_Caption = new Label(caption) { pickingMode = PickingMode.Ignore }; m_Caption.AddToClassList("terminal-wipe-caption"); Add(m_Caption);
            generateVisualContent += ctx => Paint(ctx.painter2D, contentRect.width, contentRect.height);
        }
        public void Play()
        {
            TerminalMotion.Tween(this, .72f, t =>
            {
                m_T = t; MarkDirtyRepaint(); float w = contentRect.width;
                float x = Mathf.Lerp(-w * .35f, w * 1.15f, TerminalMotion.InOutCubic(t));
                m_Caption.style.translate = new Translate(x, 0); m_Caption.style.opacity = Mathf.Sin(t * Mathf.PI);
            }, 0, RemoveFromHierarchy);
        }
        private static void Band(Painter2D p, float x, float width, float skew, float h, Color color)
        {
            p.fillGradient = default; p.fillColor = color; p.BeginPath();
            p.MoveTo(new Vector2(x + skew, 0)); p.LineTo(new Vector2(x + width + skew, 0)); p.LineTo(new Vector2(x + width, h)); p.LineTo(new Vector2(x, h)); p.ClosePath(); p.Fill();
        }
        private void Paint(Painter2D p, float w, float h)
        {
            if (w < 1 || h < 1) return; float skew = h * .35f;
            float lead = TerminalMotion.InOutCubic(m_T), tail = TerminalMotion.InOutCubic(Mathf.Clamp01(m_T * 1.25f - .25f));
            float x0 = Mathf.Lerp(-w * .4f - skew, w * 1.1f, tail), x1 = Mathf.Lerp(-w * .4f - skew, w * 1.1f, lead);
            Band(p, x0, Mathf.Max(0, x1 - x0) + w * .06f, skew, h, new Color(.02f, .024f, .03f, .92f));
            Band(p, x1, w * .045f, skew, h, TerminalPalette.Yellow);
            Band(p, x1 + w * .055f, w * .008f, skew, h, TerminalPalette.A(TerminalPalette.Yellow, .7f));
            Band(p, x1 + w * .075f, 2, skew, h, TerminalPalette.A(TerminalPalette.Cyan, .8f));
            for (int i = 0; i < 4; i++)
            {
                float y = h * (.18f + i * .21f), len = w * (.12f + i * .03f);
                TerminalMotion.Line(p, new Vector2(x1 + skew * (1 - y / h) + w * .09f, y), new Vector2(x1 + skew * (1 - y / h) + w * .09f + len, y), TerminalPalette.A(TerminalPalette.Text, .35f));
            }
        }
    }

    // Static screen furniture: corner brackets, edge rails and rulers.
    public sealed class TerminalChrome : AnimatedPainter
    {
        protected override bool Animating => Age < 1.6f;
        public TerminalChrome()
        {
            AddToClassList("terminal-chrome"); style.position = Position.Absolute; style.left = 0; style.top = 0; style.right = 0; style.bottom = 0;
        }
        protected override void Draw(Painter2D p, float w, float h)
        {
            float k = TerminalMotion.OutExpo(Age / 1.2f), m = 10, L = 26 * k; var line = new Color(1, 1, 1, .45f);
            p.strokeColor = line; p.lineWidth = 1.5f; p.BeginPath();
            p.MoveTo(new Vector2(m, m + L)); p.LineTo(new Vector2(m, m)); p.LineTo(new Vector2(m + L, m));
            p.MoveTo(new Vector2(w - m - L, m)); p.LineTo(new Vector2(w - m, m)); p.LineTo(new Vector2(w - m, m + L));
            p.MoveTo(new Vector2(m, h - m - L)); p.LineTo(new Vector2(m, h - m)); p.LineTo(new Vector2(m + L, h - m));
            p.MoveTo(new Vector2(w - m - L, h - m)); p.LineTo(new Vector2(w - m, h - m)); p.LineTo(new Vector2(w - m, h - m - L));
            p.Stroke();
            p.strokeColor = new Color(1, 1, 1, .16f); p.lineWidth = 1; p.BeginPath();
            float top = h * .2f, span = h * .6f * k;
            for (float y = 0; y <= span; y += 8) { float len = Mathf.RoundToInt(y / 8) % 5 == 0 ? 8 : 4; p.MoveTo(new Vector2(4, top + y)); p.LineTo(new Vector2(4 + len, top + y)); p.MoveTo(new Vector2(w - 4, top + y)); p.LineTo(new Vector2(w - 4 - len, top + y)); }
            p.Stroke();
            TerminalMotion.Rect(p, 4, top - 18, 3, 12, TerminalPalette.A(TerminalPalette.Yellow, k));
            TerminalMotion.Rect(p, w - 7, top + span + 6, 3, 12, TerminalPalette.A(TerminalPalette.Cyan, k));
        }
    }

    // Repeating diagonal warning stripes.
    public sealed class HazardStripes : AnimatedPainter
    {
        private readonly Color m_Color;
        private readonly bool m_Moving;
        protected override bool Animating => m_Moving;
        public HazardStripes(Color color, bool moving = true)
        {
            m_Color = color; m_Moving = moving; m_Interval = 33; AddToClassList("hazard-stripes");
        }
        protected override void Draw(Painter2D p, float w, float h)
        {
            float step = 14, off = m_Moving ? Mathf.Repeat(Age * 22, step) : 0;
            p.BeginPath(); p.MoveTo(Vector2.zero); p.LineTo(new Vector2(w, 0)); p.LineTo(new Vector2(w, h)); p.LineTo(new Vector2(0, h)); p.ClosePath(); p.PushClip();
            p.fillGradient = default; p.fillColor = m_Color; p.BeginPath();
            for (float x = -h - step + off; x < w + h; x += step) { p.MoveTo(new Vector2(x, h)); p.LineTo(new Vector2(x + h, 0)); p.LineTo(new Vector2(x + h + step * .45f, 0)); p.LineTo(new Vector2(x + step * .45f, h)); p.ClosePath(); }
            p.Fill(); p.PopClip();
        }
    }

    // Animated exploded view of the halo weapon.
    public sealed class HaloSchematic : AnimatedPainter
    {
        private readonly List<Vector2> m_Nodes = new List<Vector2>();
        public void SetNodes(IEnumerable<Vector2> percentPositions) { m_Nodes.Clear(); m_Nodes.AddRange(percentPositions); MarkDirtyRepaint(); }
        public HaloSchematic() { m_Interval = 33; AddToClassList("halo-schematic"); }
        protected override void Draw(Painter2D p, float w, float h)
        {
            float t = Age, intro = TerminalMotion.OutExpo(t / 1.4f); var c = new Vector2(w * .5f, h * .47f); float r = Mathf.Min(w * .2f, h * .25f) * Mathf.Lerp(.6f, 1, intro);
            p.strokeColor = new Color(1, 1, 1, .05f); p.lineWidth = 1; p.BeginPath();
            for (float x = 0; x < w; x += 32) { p.MoveTo(new Vector2(x, 0)); p.LineTo(new Vector2(x, h)); }
            for (float y = 0; y < h; y += 32) { p.MoveTo(new Vector2(0, y)); p.LineTo(new Vector2(w, y)); }
            p.Stroke();
            TerminalMotion.Line(p, new Vector2(c.x, 0), new Vector2(c.x, h), TerminalPalette.A(TerminalPalette.Cyan, .12f));
            TerminalMotion.Line(p, new Vector2(0, c.y), new Vector2(w, c.y), TerminalPalette.A(TerminalPalette.Cyan, .12f));
            p.fillGradient = TerminalMotion.Radial(TerminalPalette.A(TerminalPalette.Yellow, .22f * intro), new Color(1, .882f, .102f, 0), c, r * 1.6f, c, AddressMode.Clamp);
            p.BeginPath(); p.Arc(c, r * 1.6f, 0, 360); p.Fill(); p.fillGradient = default;
            TerminalMotion.Ring(p, c, r, TerminalPalette.A(TerminalPalette.Text, .85f), 3, -90, -90 + 360 * intro);
            TerminalMotion.Dashed(p, 10, 6, t * 30); TerminalMotion.Ring(p, c, r * .8f, TerminalPalette.A(TerminalPalette.Cyan, .7f), 1.5f); TerminalMotion.Solid(p);
            TerminalMotion.Dashed(p, 2, 5, -t * 20); TerminalMotion.Ring(p, c, r * 1.22f, new Color(1, 1, 1, .25f), 1); TerminalMotion.Solid(p);
            for (int i = 0; i < 4; i++) { float a0 = t * 40 + i * 90; TerminalMotion.Ring(p, c, r * 1.12f, TerminalPalette.A(TerminalPalette.Yellow, .9f), 2.5f, a0, a0 + 22); }
            for (int i = 0; i < 2; i++) { float a0 = -t * 25 + i * 180; TerminalMotion.Ring(p, c, r * .62f, TerminalPalette.A(TerminalPalette.Text, .5f), 1, a0, a0 + 70); }
            float pulse = .5f + .5f * Mathf.Sin(t * 3);
            TerminalMotion.Circle(p, c, 9 + pulse * 3, TerminalPalette.A(TerminalPalette.Yellow, .25f)); TerminalMotion.Circle(p, c, 6, TerminalPalette.Yellow);
            TerminalMotion.Ring(p, c, 10 + Mathf.Repeat(t * 30, 40), TerminalPalette.A(TerminalPalette.Yellow, 1 - Mathf.Repeat(t * 30, 40) / 40), 1);
            for (int i = 0; i < m_Nodes.Count; i++)
            {
                float k = TerminalMotion.OutCubic(TerminalMotion.Window(t, .3f + i * .08f, .5f)); if (k <= 0) continue;
                var target = new Vector2((m_Nodes[i].x + 10) * .01f * w, m_Nodes[i].y * .01f * h + 21);
                var dir = (target - c).normalized; var from = c + dir * r * 1.22f; var knee = new Vector2(Mathf.Lerp(from.x, target.x, .5f), target.y);
                var end = Vector2.Lerp(from, target, k); p.strokeColor = TerminalPalette.A(TerminalPalette.Text, .35f); p.lineWidth = 1; p.BeginPath(); p.MoveTo(from);
                if (k < .5f) p.LineTo(Vector2.Lerp(from, knee, k * 2)); else { p.LineTo(knee); p.LineTo(Vector2.Lerp(knee, target, k * 2 - 1)); }
                p.Stroke(); TerminalMotion.Diamond(p, from, 3, TerminalPalette.Yellow);
                float pt = Mathf.Repeat(t * .6f + i * .37f, 1); var pp = pt < .5f ? Vector2.Lerp(from, knee, pt * 2) : Vector2.Lerp(knee, target, pt * 2 - 1);
                if (k >= 1) TerminalMotion.Circle(p, pp, 2, TerminalPalette.Cyan);
                _ = end;
            }
        }
    }

    // Animated tactical map of the lunar surface sector.
    public sealed class LunarSchematic : AnimatedPainter
    {
        public LunarSchematic() { m_Interval = 33; AddToClassList("lunar-schematic"); }
        protected override void Draw(Painter2D p, float w, float h)
        {
            float t = Age, intro = TerminalMotion.OutExpo(t / 1.2f); var c = new Vector2(w * .52f, h * .52f); float R = Mathf.Min(w * .44f, h * .46f);
            p.strokeColor = new Color(1, 1, 1, .045f); p.lineWidth = 1; p.BeginPath();
            for (int x = 0; x <= 20; x++) { p.MoveTo(new Vector2(w * x / 20, 0)); p.LineTo(new Vector2(w * x / 20, h)); }
            for (int y = 0; y <= 12; y++) { p.MoveTo(new Vector2(0, h * y / 12)); p.LineTo(new Vector2(w, h * y / 12)); }
            p.Stroke();
            for (int i = 1; i <= 7; i++)
            {
                float rr = R * i / 7f * intro; p.strokeColor = TerminalPalette.A(TerminalPalette.Cyan, .06f + i * .015f); p.lineWidth = 1; p.BeginPath();
                for (int s = 0; s <= 64; s++)
                {
                    float a = s / 64f * Mathf.PI * 2, wob = 1 + .07f * Mathf.Sin(a * 3 + i) + .05f * Mathf.Sin(a * 5 - i * 2);
                    var v = c + new Vector2(Mathf.Cos(a) * rr * wob * 1.2f, Mathf.Sin(a) * rr * wob * .8f); if (s == 0) p.MoveTo(v); else p.LineTo(v);
                }
                p.Stroke();
            }
            float sweep = t * 60 % 360;
            for (int i = 0; i < 12; i++) TerminalMotion.Ring(p, c, R * .6f, TerminalPalette.A(TerminalPalette.Cyan, .14f * (1 - i / 12f)), R * 1.2f, sweep - i * 3 - 3, sweep - i * 3);
            TerminalMotion.Line(p, c, c + new Vector2(Mathf.Cos(sweep * Mathf.Deg2Rad), Mathf.Sin(sweep * Mathf.Deg2Rad)) * R * 1.2f, TerminalPalette.A(TerminalPalette.Cyan, .6f));
            var route = new[] { c + new Vector2(-R * .95f, R * .5f), c + new Vector2(-R * .45f, R * .22f), c + new Vector2(-R * .2f, -R * .35f), c + new Vector2(R * .35f, -R * .18f), c + new Vector2(R * .75f, -R * .55f) };
            for (int i = 0; i < 9; i++)
            {
                float a = i * 2.39996f, rr = R * Mathf.Sqrt((i + .5f) / 9f) * .9f; var cache = c + new Vector2(Mathf.Cos(a) * rr * 1.15f, Mathf.Sin(a) * rr * .75f);
                float ang = (Mathf.Atan2(cache.y - c.y, cache.x - c.x) * Mathf.Rad2Deg + 360) % 360, lit = Mathf.Clamp01(1 - Mathf.Repeat(sweep - ang, 360) / 120);
                p.strokeColor = TerminalPalette.A(TerminalPalette.Text, .35f + lit * .65f); p.lineWidth = 1; p.BeginPath();
                p.MoveTo(cache + new Vector2(-5, -5)); p.LineTo(cache + new Vector2(5, -5)); p.LineTo(cache + new Vector2(5, 5)); p.LineTo(cache + new Vector2(-5, 5)); p.ClosePath(); p.Stroke();
                if (lit > 0) TerminalMotion.Rect(p, cache.x - 2, cache.y - 2, 4, 4, TerminalPalette.A(TerminalPalette.Yellow, lit));
            }
            TerminalMotion.Dashed(p, 8, 5, -t * 24); p.strokeColor = TerminalPalette.Yellow; p.lineWidth = 2; p.BeginPath(); p.MoveTo(route[0]);
            float progress = intro * (route.Length - 1);
            for (int i = 1; i < route.Length; i++) { float seg = Mathf.Clamp01(progress - (i - 1)); if (seg <= 0) break; p.LineTo(Vector2.Lerp(route[i - 1], route[i], seg)); }
            p.Stroke(); TerminalMotion.Solid(p);
            foreach (var v in route) TerminalMotion.Diamond(p, v, 3.5f, TerminalPalette.Yellow);
            var beacon = route[route.Length - 1];
            for (int i = 0; i < 3; i++) { float k = Mathf.Repeat(t * .7f + i / 3f, 1); TerminalMotion.Ring(p, beacon, 8 + k * 46, TerminalPalette.A(TerminalPalette.Green, 1 - k), 1.5f); }
            TerminalMotion.Circle(p, beacon, 6, TerminalPalette.Green);
            var start = route[0]; TerminalMotion.Ring(p, start, 10, TerminalPalette.A(TerminalPalette.Text, .8f), 1.5f);
            TerminalMotion.Line(p, start + new Vector2(-16, 0), start + new Vector2(16, 0), TerminalPalette.A(TerminalPalette.Text, .6f));
            TerminalMotion.Line(p, start + new Vector2(0, -16), start + new Vector2(0, 16), TerminalPalette.A(TerminalPalette.Text, .6f));
        }
    }

    // Top-down blueprint of the orbital ship.
    public sealed class ShipBlueprint : AnimatedPainter
    {
        public ShipBlueprint() { m_Interval = 33; AddToClassList("ship-blueprint"); }
        protected override void Draw(Painter2D p, float w, float h)
        {
            float t = Age, k = TerminalMotion.OutCubic(t / 1.6f); var c = new Vector2(w * .5f, h * .5f); float L = Mathf.Min(w * .44f, h * 1.1f), H = L * .26f;
            p.strokeColor = new Color(1, 1, 1, .045f); p.lineWidth = 1; p.BeginPath();
            for (float x = 0; x < w; x += 24) { p.MoveTo(new Vector2(x, 0)); p.LineTo(new Vector2(x, h)); }
            for (float y = 0; y < h; y += 24) { p.MoveTo(new Vector2(0, y)); p.LineTo(new Vector2(w, y)); }
            p.Stroke();
            var hull = new[] { new Vector2(-L, 0), new Vector2(-L * .7f, -H), new Vector2(L * .45f, -H), new Vector2(L * .7f, -H * .5f), new Vector2(L, -H * .2f), new Vector2(L, H * .2f), new Vector2(L * .7f, H * .5f), new Vector2(L * .45f, H), new Vector2(-L * .7f, H), new Vector2(-L, 0) };
            float total = 0; for (int i = 1; i < hull.Length; i++) total += Vector2.Distance(hull[i - 1], hull[i]);
            float left = total * k; p.strokeColor = TerminalPalette.A(TerminalPalette.Cyan, .85f); p.lineWidth = 1.5f; p.BeginPath(); p.MoveTo(c + hull[0]);
            for (int i = 1; i < hull.Length && left > 0; i++) { float len = Vector2.Distance(hull[i - 1], hull[i]); p.LineTo(c + (len <= left ? hull[i] : Vector2.Lerp(hull[i - 1], hull[i], left / len))); left -= len; }
            p.Stroke();
            float[] bays = { -.55f, -.18f, .19f, .56f }; string[] state = { "on", "off", "off", "off" };
            for (int i = 0; i < bays.Length; i++)
            {
                float a = TerminalMotion.OutCubic(TerminalMotion.Window(t, .5f + i * .12f, .5f)); float x = c.x + bays[i] * L - L * .15f, bw = L * .3f, bh = H * 1.2f;
                bool on = state[i] == "on"; var col = on ? TerminalPalette.Green : TerminalPalette.A(TerminalPalette.Text, .4f);
                p.strokeColor = TerminalPalette.A(col, a); p.lineWidth = 1; p.BeginPath();
                p.MoveTo(new Vector2(x, c.y - bh * .5f)); p.LineTo(new Vector2(x + bw, c.y - bh * .5f)); p.LineTo(new Vector2(x + bw, c.y + bh * .5f)); p.LineTo(new Vector2(x, c.y + bh * .5f)); p.ClosePath(); p.Stroke();
                if (on) TerminalMotion.Rect(p, x + 2, c.y - bh * .5f + 2, (bw - 4) * (.5f + .5f * Mathf.Sin(t * 2)) * a, 2, TerminalPalette.Green);
                else { TerminalMotion.Line(p, new Vector2(x, c.y - bh * .5f), new Vector2(x + bw, c.y + bh * .5f), TerminalPalette.A(TerminalPalette.Red, .35f * a)); }
            }
            float scan = Mathf.Repeat(t * .35f, 1); float sx = c.x - L + scan * L * 2;
            TerminalMotion.GradientRect(p, sx - 40, c.y - H - 10, 40, H * 2 + 20, new Color(.247f, .878f, .816f, 0), TerminalPalette.A(TerminalPalette.Cyan, .18f), false);
            TerminalMotion.Line(p, new Vector2(sx, c.y - H - 14), new Vector2(sx, c.y + H + 14), TerminalPalette.A(TerminalPalette.Cyan, .7f));
            for (int i = 0; i < 3; i++) { float f = .5f + .5f * Mathf.Sin(t * 8 + i); TerminalMotion.Rect(p, c.x - L - 14 - f * 14, c.y - H * .3f + i * H * .3f - 2, 10 + f * 14, 3, TerminalPalette.A(TerminalPalette.Yellow, .5f + f * .5f)); }
        }
    }

    // Idle carrier wave for the quiet comms channel.
    public sealed class SignalWave : AnimatedPainter
    {
        public SignalWave() { m_Interval = 33; AddToClassList("signal-wave"); }
        protected override void Draw(Painter2D p, float w, float h)
        {
            float t = Age, mid = h * .5f;
            TerminalMotion.Line(p, new Vector2(0, mid), new Vector2(w, mid), new Color(1, 1, 1, .08f));
            for (int layer = 0; layer < 3; layer++)
            {
                p.strokeColor = layer == 0 ? TerminalPalette.Cyan : TerminalPalette.A(TerminalPalette.Cyan, .3f - layer * .08f); p.lineWidth = layer == 0 ? 1.5f : 1; p.BeginPath();
                for (int i = 0; i <= 160; i++)
                {
                    float x = w * i / 160f, env = Mathf.Sin(Mathf.PI * i / 160f), amp = h * (.06f + .04f * Mathf.Sin(t * .7f + layer));
                    float y = mid + Mathf.Sin(x * .045f - t * (3 + layer) + layer) * amp * env + Mathf.Sin(x * .13f + t * 5) * amp * .25f * env;
                    if (i == 0) p.MoveTo(new Vector2(x, y)); else p.LineTo(new Vector2(x, y));
                }
                p.Stroke();
            }
            float scan = Mathf.Repeat(t * .25f, 1) * w; TerminalMotion.Line(p, new Vector2(scan, 0), new Vector2(scan, h), TerminalPalette.A(TerminalPalette.Yellow, .6f));
            p.fillGradient = default; p.fillColor = new Color(1, 1, 1, .25f); p.BeginPath();
            for (int i = 0; i < 48; i++) { float x = w * i / 48f, bh = 2 + Mathf.Abs(Mathf.Sin(i * 1.7f + t * 2.3f)) * h * .08f; p.MoveTo(new Vector2(x, h - bh)); p.LineTo(new Vector2(x + 3, h - bh)); p.LineTo(new Vector2(x + 3, h)); p.LineTo(new Vector2(x, h)); p.ClosePath(); }
            p.Fill();
        }
    }

    // Circular gauge for a single value.
    public sealed class ArcMeter : AnimatedPainter
    {
        private readonly float m_Value;
        private readonly Color m_Color;
        protected override bool Animating => Age < 2f;
        public ArcMeter(float value, Color color) { m_Value = Mathf.Clamp01(value); m_Color = color; AddToClassList("arc-meter"); }
        protected override void Draw(Painter2D p, float w, float h)
        {
            var c = new Vector2(w * .5f, h * .5f); float r = Mathf.Min(w, h) * .5f - 4, k = TerminalMotion.OutExpo(Age / 1.4f);
            TerminalMotion.Ring(p, c, r, new Color(1, 1, 1, .1f), 3, 135, 405);
            if (m_Value > 0) TerminalMotion.Ring(p, c, r, m_Color, 3, 135, 135 + 270 * m_Value * k);
            p.strokeColor = new Color(1, 1, 1, .25f); p.lineWidth = 1; p.BeginPath();
            for (int i = 0; i <= 27; i++) { float a = (135 + i * 10) * Mathf.Deg2Rad; var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); p.MoveTo(c + d * (r - 7)); p.LineTo(c + d * (r - (i % 9 == 0 ? 13 : 9))); }
            p.Stroke();
        }
    }
}
