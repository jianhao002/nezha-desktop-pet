using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using NezhaDesktop;
using NezhaDesktop.MiniGames;

namespace NezhaGames
{
    public sealed class BreakoutBoard
    {
        public const double Width = 300, Height = 360, Radius = 4, PaddleY = 334, PaddleWidth = 64;
        public readonly bool[] Bricks;
        public readonly double Speed;
        public double Paddle { get; private set; }
        public double BallX { get; private set; }
        public double BallY { get; private set; }
        public double VX { get; private set; }
        public double VY { get; private set; }
        public int Lives { get; private set; }
        public int Score { get; private set; }
        public bool Serving { get; private set; }
        public bool Won { get { return Bricks.All(v => !v); } }
        public bool Ended { get { return Lives == 0 || Won; } }
        public BreakoutBoard(int rows, double speed)
        { if (rows < 1 || rows > 8 || speed < 50 || speed > 500) throw new ArgumentOutOfRangeException(); Bricks = Enumerable.Repeat(true, rows * 8).ToArray(); Speed = speed; Lives = 3; Paddle = Width / 2; ResetBall(); }
        private void ResetBall() { BallX = Paddle; BallY = PaddleY - Radius - 1; VX = Speed * 0.35; VY = -Math.Sqrt(Speed * Speed - VX * VX); Serving = true; }
        public void SetPaddle(double center) { if (Ended || Double.IsNaN(center) || Double.IsInfinity(center)) return; Paddle = Math.Max(PaddleWidth / 2, Math.Min(Width - PaddleWidth / 2, center)); if (Serving) BallX = Paddle; }
        public void Launch() { if (!Ended) Serving = false; }
        public void Step(double seconds, int direction)
        {
            if (Ended || seconds <= 0 || Double.IsNaN(seconds) || Double.IsInfinity(seconds)) return;
            seconds = Math.Min(seconds, 0.05); SetPaddle(Paddle + Math.Sign(direction) * 260 * seconds); if (Serving) return;
            int steps = Math.Max(1, (int)Math.Ceiling(Speed * seconds / Radius)); double dt = seconds / steps;
            for (int step = 0; step < steps && !Ended && !Serving; step++) {
                double oldX = BallX, oldY = BallY; BallX += VX * dt; BallY += VY * dt;
                if (BallX < Radius) { BallX = Radius; VX = Math.Abs(VX); } else if (BallX > Width - Radius) { BallX = Width - Radius; VX = -Math.Abs(VX); }
                if (BallY < Radius) { BallY = Radius; VY = Math.Abs(VY); }
                if (VY > 0 && oldY <= PaddleY - Radius && BallY >= PaddleY - Radius && Math.Abs(BallX - Paddle) <= PaddleWidth / 2 + Radius) {
                    BallY = PaddleY - Radius - 0.1; VX = Speed * 0.8 * Math.Max(-1, Math.Min(1, (BallX - Paddle) / (PaddleWidth / 2))); VY = -Math.Sqrt(Speed * Speed - VX * VX);
                }
                for (int i = 0; i < Bricks.Length; i++) {
                    if (!Bricks[i]) continue; double left = 10 + i % 8 * 35, top = 30 + i / 8 * 21;
                    if (BallX + Radius < left || BallX - Radius > left + 32 || BallY + Radius < top || BallY - Radius > top + 17) continue;
                    Bricks[i] = false; Score += 10;
                    if (oldY + Radius <= top || oldY - Radius >= top + 17) { VY = -VY; BallY = oldY; } else { VX = -VX; BallX = oldX; }
                    break;
                }
                if (BallY > Height + Radius) { Lives--; ResetBall(); }
            }
        }
    }
    public sealed class BreakoutGame : MiniGameControl
    {
        public BreakoutBoard Board { get; private set; }
        private ComboBox difficulty;
        private Canvas canvas;
        private TextBlock score, message;
        private Button pause;
        private DispatcherTimer timer;
        private int direction, tier;
        private bool hostPaused, manualPaused = true, recorded, disposed;
        private bool Paused { get { return hostPaused || manualPaused || disposed; } }
        protected override UIElement Build()
        {
            Grid layout = new Grid(); foreach (GridLength row in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto }) layout.RowDefinitions.Add(new RowDefinition { Height = row });
            WrapPanel top = new WrapPanel(); difficulty = new ComboBox { ItemsSource = new[] { "轻松", "标准", "挑战" }, SelectedIndex = 0, Width = 72, Margin = new Thickness(0, 0, 6, 6) };
            top.Children.Add(difficulty); top.Children.Add(Ui.Button("新一局", NewGame, false)); pause = Ui.Button("开始", Toggle, true); pause.Margin = new Thickness(6, 0, 0, 6); top.Children.Add(pause); layout.Children.Add(top);
            score = Ui.Text("", 12, Ui.Muted); Grid.SetRow(score, 1); layout.Children.Add(score);
            canvas = new Canvas { Width = 300, Height = 360, Background = Ui.Color("#F2F1E9") }; canvas.MouseMove += OnMouse;
            Viewbox box = new Viewbox { Child = canvas, Stretch = Stretch.Uniform, Margin = new Thickness(0, 8, 0, 8) }; Grid.SetRow(box, 2); layout.Children.Add(box);
            UniformGrid arrows = new UniformGrid { Columns = 2 }; foreach (int side in new[] { -1, 1 }) {
                int move = side; Button arrow = Ui.Button(side < 0 ? "←" : "→", delegate { if (!Paused && !Board.Ended) { Board.SetPaddle(Board.Paddle + move * 24); Paint(); Focus(); } }, false); arrow.Margin = new Thickness(2); arrow.Padding = new Thickness(5); arrow.HorizontalAlignment = HorizontalAlignment.Stretch; arrows.Children.Add(arrow);
            }
            Grid.SetRow(arrows, 3); layout.Children.Add(arrows); message = Ui.Text("", 11, Ui.Muted); message.Margin = new Thickness(0, 6, 0, 0); Grid.SetRow(message, 4); layout.Children.Add(message);
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) }; timer.Tick += OnTick; PreviewKeyDown += OnKey; PreviewKeyUp += OnKeyUp; NewGame(); return layout;
        }
        private void NewGame() { timer.Stop(); tier = Math.Max(0, difficulty.SelectedIndex); Board = new BreakoutBoard(3 + tier, new[] { 190.0, 240.0, 290.0 }[tier]); direction = 0; recorded = false; manualPaused = true; ApplyPause(); }
        private void Toggle() { if (Board.Ended) NewGame(); if (Board.Serving) { Board.Launch(); manualPaused = false; } else manualPaused = !manualPaused; ApplyPause(); Focus(); }
        private void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space) { Toggle(); e.Handled = true; }
            else if (e.Key == Key.Left || e.Key == Key.A || e.Key == Key.Right || e.Key == Key.D) { if (!Paused) direction = e.Key == Key.Left || e.Key == Key.A ? -1 : 1; e.Handled = true; }
        }
        private void OnKeyUp(object sender, KeyEventArgs e) { if (e.Key == Key.Left || e.Key == Key.A || e.Key == Key.Right || e.Key == Key.D) direction = 0; }
        private void OnMouse(object sender, MouseEventArgs e) { if (!Paused && !Board.Ended) { Board.SetPaddle(e.GetPosition(canvas).X); Paint(); } }
        private void OnTick(object sender, EventArgs e)
        {
            if (Paused || Board.Ended) return; Board.Step(0.02, direction);
            if (Board.Ended) { timer.Stop(); if (!recorded) { recorded = true; try { Host.Record("score-" + tier, Board.Score, false); } catch (Exception ex) { Host.Notify("成绩未保存：" + ex.Message); } } }
            Paint();
        }
        private void ApplyPause() { timer.Stop(); if (!Paused && !Board.Ended) timer.Start(); Paint(); }
        private void Rect(double x, double y, double width, double height, Brush fill)
        { Rectangle shape = new Rectangle { Width = width, Height = height, RadiusX = 3, RadiusY = 3, Fill = fill }; Canvas.SetLeft(shape, x); Canvas.SetTop(shape, y); canvas.Children.Add(shape); }
        private void Paint()
        {
            canvas.Children.Clear(); for (int i = 0; i < Board.Bricks.Length; i++) if (Board.Bricks[i]) Rect(10 + i % 8 * 35, 30 + i / 8 * 21, 32, 17, i / 8 % 2 == 0 ? Ui.Red : Ui.Gold);
            Rect(Board.Paddle - BreakoutBoard.PaddleWidth / 2, BreakoutBoard.PaddleY, BreakoutBoard.PaddleWidth, 8, Ui.Color("#608575"));
            Ellipse ball = new Ellipse { Width = 8, Height = 8, Fill = Ui.Ink }; Canvas.SetLeft(ball, Board.BallX - 4); Canvas.SetTop(ball, Board.BallY - 4); canvas.Children.Add(ball);
            score.Text = "分数 " + Board.Score + " · 机会 " + Board.Lives + " · 最佳 " + Host.Best("score-" + tier, 0);
            pause.Content = Board.Ended ? "再来" : Board.Serving ? "发球" : manualPaused ? "开始 / 继续" : "暂停";
            message.Text = Board.Won ? "砖块清空啦！" : Board.Ended ? "机会用完了，再开一局吧。" : Paused ? "已暂停 · 点击发球 / 继续，或按空格" : Board.Serving ? "按空格发球 · 还剩 " + Board.Lives + " 次机会" : "鼠标 / ← → 控制挡板 · 空格暂停";
        }
        public override void SetHostPaused(bool value) { hostPaused = value; direction = 0; if (timer != null) ApplyPause(); }
        public override void Dispose() { disposed = true; if (timer != null) { timer.Stop(); timer.Tick -= OnTick; } PreviewKeyDown -= OnKey; PreviewKeyUp -= OnKeyUp; if (canvas != null) canvas.MouseMove -= OnMouse; }
    }
}
