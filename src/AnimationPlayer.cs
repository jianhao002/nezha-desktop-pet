using System;
using System.Windows;
using System.Windows.Media;

namespace NezhaDesktop
{
    // One visual action owns the pet until its clip has finished. Elapsed time is
    // advanced by the UI clock, so a stalled dispatcher never skips eating poses.
    public sealed class AnimationArbiter
    {
        private int frameMilliseconds, frameCount;
        private bool loop, protect;
        public double ElapsedMilliseconds { get; private set; }
        public double ReactionMilliseconds { get; private set; }
        public bool ReactionActive { get { return ReactionMilliseconds > ElapsedMilliseconds; } }
        public bool IsProtected { get { return protect && ReactionActive; } }
        public int FrameIndex
        {
            get
            {
                int frame = (int)(ElapsedMilliseconds / frameMilliseconds);
                return loop ? frame % frameCount : Math.Min(frame, frameCount - 1);
            }
        }
        public void Start(int frameMs, int frames, bool loops)
        {
            if (frameMs <= 0 || frames <= 0) throw new ArgumentOutOfRangeException("frameMs");
            frameMilliseconds = frameMs; frameCount = frames; loop = loops;
            ElapsedMilliseconds = 0; ReactionMilliseconds = 0; protect = false;
        }
        public void ProtectReaction(double durationMilliseconds)
        {
            ReactionMilliseconds = Math.Max(frameMilliseconds * (double)frameCount, durationMilliseconds);
            protect = true;
        }
        public void Advance(double elapsedMilliseconds)
        {
            ElapsedMilliseconds += Math.Max(0, Math.Min(frameMilliseconds, elapsedMilliseconds));
        }
        public void ClearReaction() { ReactionMilliseconds = 0; protect = false; }
    }

    // Effects are drawn in WPF and remain click-through. Their shape and timing
    // are deterministic; the sprite pixels continue to define clickable areas.
    public sealed class PetEffects : FrameworkElement
    {
        private double haloElapsed = 4000, wheelsElapsed = 4000;
        public bool HaloActive { get { return haloElapsed < 2800; } }
        public bool WheelsActive { get { return wheelsElapsed < 2200; } }
        public PetEffects() { IsHitTestVisible = false; ClipToBounds = false; }
        public void Trigger(string part)
        {
            if (part == "halo") haloElapsed = 0;
            if (part == "wheels") wheelsElapsed = 0;
            InvalidateVisual();
        }
        public void Advance(double elapsedMilliseconds)
        {
            bool redraw = HaloActive || WheelsActive;
            double delta = Math.Max(0, Math.Min(150, elapsedMilliseconds));
            haloElapsed += delta; wheelsElapsed += delta;
            if (redraw) InvalidateVisual();
        }
        private static Color Tint(byte alpha, byte red, byte green, byte blue)
        { return Color.FromArgb(alpha, red, green, blue); }
        protected override void OnRender(DrawingContext drawing)
        {
            base.OnRender(drawing);
            if (ActualWidth <= 0 || ActualHeight <= 0) return;
            double width = ActualWidth, height = ActualHeight;
            if (HaloActive)
            {
                double t = haloElapsed / 2800, fade = Math.Min(1, (1 - t) * 5);
                double pulse = (0.66 + 0.34 * Math.Sin(haloElapsed / 180));
                byte light = (byte)(150 * pulse * fade);
                RadialGradientBrush glow = new RadialGradientBrush();
                glow.GradientStops.Add(new GradientStop(Tint(0, 255, 246, 224), 0));
                glow.GradientStops.Add(new GradientStop(Tint(0, 255, 246, 224), 0.54));
                glow.GradientStops.Add(new GradientStop(Tint((byte)(light * 0.38), 255, 176, 165), 0.66));
                glow.GradientStops.Add(new GradientStop(Tint(light, 255, 250, 233), 0.76));
                glow.GradientStops.Add(new GradientStop(Tint((byte)(light * 0.66), 255, 192, 178), 0.82));
                glow.GradientStops.Add(new GradientStop(Tint(0, 255, 204, 179), 1));
                Point center = new Point(width * 0.5, height * 0.24);
                double breath = 1 + 0.025 * Math.Sin(haloElapsed / 240);
                drawing.DrawEllipse(glow, null, center, width * 0.31 * breath, height * 0.245 * breath);
                for (int i = 0; i < 12; i++)
                {
                    double angle = i * Math.PI / 6 + t * 0.38;
                    Point star = new Point(center.X + Math.Cos(angle) * width * (0.23 + t * 0.055),
                        center.Y + Math.Sin(angle) * height * (0.185 + t * 0.04));
                    double radius = width * 0.006 * (0.6 + 0.4 * Math.Sin(haloElapsed / 170 + i));
                    Pen pen = new Pen(new SolidColorBrush(Tint((byte)(light * 0.9), 255, 247, 221)), 1);
                    drawing.DrawLine(pen, new Point(star.X - radius, star.Y), new Point(star.X + radius, star.Y));
                    drawing.DrawLine(pen, new Point(star.X, star.Y - radius), new Point(star.X, star.Y + radius));
                }
            }
            if (WheelsActive)
            {
                double t = wheelsElapsed / 2200, fade = Math.Min(1, (1 - t) * 5);
                for (int wheel = 0; wheel < 2; wheel++)
                {
                    double x = width * (wheel == 0 ? 0.37 : 0.64), y = height * 0.87;
                    StreamGeometry trail = new StreamGeometry();
                    using (StreamGeometryContext geometry = trail.Open())
                    {
                        geometry.BeginFigure(new Point(x, y), false, false);
                        geometry.BezierTo(new Point(x - width * 0.07, y - height * 0.025),
                            new Point(x - width * 0.18, y + height * 0.055),
                            new Point(x - width * 0.28, y + height * 0.012), true, false);
                    }
                    drawing.DrawGeometry(null, new Pen(new SolidColorBrush(Tint((byte)(80 * fade), 238, 50, 36)), width * 0.026), trail);
                    drawing.DrawGeometry(null, new Pen(new SolidColorBrush(Tint((byte)(190 * fade), 255, 208, 127)), width * 0.006), trail);
                    for (int i = 0; i < 10; i++)
                    {
                        double phase = (t * 3 + i / 10.0) % 1;
                        Point spark = new Point(x - phase * width * 0.28,
                            y + Math.Sin(i * 2.37 + phase * 4) * height * 0.035 - phase * height * 0.02);
                        byte alpha = (byte)(200 * (1 - phase) * fade);
                        drawing.DrawEllipse(new SolidColorBrush(Tint(alpha, 255, (byte)(120 + i * 10), 88)), null,
                            spark, width * 0.0045 * (1 - phase * 0.7), width * 0.0045 * (1 - phase * 0.7));
                    }
                }
            }
        }
    }
}
