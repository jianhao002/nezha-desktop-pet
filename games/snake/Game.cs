using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using NezhaDesktop;
using NezhaDesktop.MiniGames;

namespace NezhaGames
{
    public sealed class SnakeBoard
    {
        public readonly int Size;
        public readonly bool Wrap;
        public readonly List<int> Body = new List<int>();
        public int Food { get; private set; }
        public int Score { get; private set; }
        public bool Ended { get; private set; }
        public bool Won { get; private set; }
        private int dx = 1, dy, nextX = 1, nextY;
        private bool turnQueued;
        private readonly Random random;
        public SnakeBoard(int size, bool wrap, Random random)
        {
            if (size < 5 || size > 32) throw new ArgumentOutOfRangeException("size");
            Size = size; Wrap = wrap; this.random = random;
            int head = (size / 2) * size + size / 2; Body.Add(head); Body.Add(head - 1); Body.Add(head - 2); PlaceFood();
        }
        public bool Turn(int x, int y)
        {
            if (Ended || turnQueued || Math.Abs(x) + Math.Abs(y) != 1 || (x == -dx && y == -dy)) return false;
            if (x == dx && y == dy) return false;
            nextX = x; nextY = y; turnQueued = true; return true;
        }
        public void Step()
        {
            if (Ended) return;
            dx = nextX; dy = nextY; turnQueued = false;
            int x = Body[0] % Size + dx, y = Body[0] / Size + dy;
            if (Wrap) { x = (x + Size) % Size; y = (y + Size) % Size; }
            else if (x < 0 || y < 0 || x >= Size || y >= Size) { Ended = true; return; }
            int next = y * Size + x; bool grows = next == Food;
            // Entering the previous tail is legal when that tail moves away.
            if (Body.Take(Body.Count - (grows ? 0 : 1)).Contains(next)) { Ended = true; return; }
            Body.Insert(0, next);
            if (grows) { Score += 10; PlaceFood(); } else Body.RemoveAt(Body.Count - 1);
        }
        private void PlaceFood()
        {
            HashSet<int> occupied = new HashSet<int>(Body);
            List<int> available = Enumerable.Range(0, Size * Size).Where(i => !occupied.Contains(i)).ToList();
            if (available.Count == 0) { Won = Ended = true; Food = -1; return; }
            Food = available[random.Next(available.Count)];
        }
    }
    public sealed class SnakeGame : MiniGameControl
    {
        public SnakeBoard Board { get; private set; }
        private Canvas canvas;
        private DispatcherTimer timer;
        private TextBlock score, message;
        private ComboBox mode, speed;
        private Button pause;
        private bool hostPaused, manualPaused = true, recorded;
        protected override UIElement Build()
        {
            Grid layout = new Grid();
            foreach (GridLength height in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
                layout.RowDefinitions.Add(new RowDefinition { Height = height });
            WrapPanel controls = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            mode = new ComboBox { ItemsSource = new[] { "经典", "穿墙" }, SelectedIndex = 0, Width = 64, Margin = new Thickness(0, 0, 6, 5) };
            speed = new ComboBox { ItemsSource = new[] { "慢速", "标准", "快速" }, SelectedIndex = 1, Width = 64, Margin = new Thickness(0, 0, 6, 5) };
            controls.Children.Add(mode); controls.Children.Add(speed); controls.Children.Add(Ui.Button("新一局", NewGame, false));
            pause = Ui.Button("开始", TogglePause, true); pause.Margin = new Thickness(6, 0, 0, 5); controls.Children.Add(pause); layout.Children.Add(controls);
            score = Ui.Text("", 12, Ui.Muted); Grid.SetRow(score, 1); layout.Children.Add(score);
            canvas = new Canvas { Width = 280, Height = 280, Background = Ui.Color("#F2F1E9") };
            Viewbox box = new Viewbox { Child = canvas, Stretch = Stretch.Uniform, Margin = new Thickness(0, 10, 0, 8) }; Grid.SetRow(box, 2); layout.Children.Add(box);
            message = Ui.Text("方向键 / WASD · 空格暂停", 12, Ui.Muted); message.MinHeight = 32; Grid.SetRow(message, 3); layout.Children.Add(message);
            PreviewKeyDown += OnKey;
            timer = new DispatcherTimer(); timer.Tick += delegate {
                if (!Paused && !Board.Ended) { Board.Step(); if (Board.Ended) Finish(); Paint(); }
            };
            NewGame(); return layout;
        }
        private bool Paused { get { return hostPaused || manualPaused; } }
        private void NewGame()
        {
            timer.Stop(); Board = new SnakeBoard(14, mode.SelectedIndex == 1, new Random());
            timer.Interval = TimeSpan.FromMilliseconds(speed.SelectedIndex == 0 ? 220 : speed.SelectedIndex == 2 ? 95 : 150);
            manualPaused = true; recorded = false; message.Text = "方向键 / WASD · 空格暂停"; ApplyPause(); Paint(); Focus();
        }
        private void TogglePause()
        {
            if (Board.Ended) NewGame(); manualPaused = !manualPaused; ApplyPause(); Focus();
        }
        private void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space) { TogglePause(); e.Handled = true; return; }
            int x = 0, y = 0;
            if (e.Key == Key.Left || e.Key == Key.A) x = -1;
            else if (e.Key == Key.Right || e.Key == Key.D) x = 1;
            else if (e.Key == Key.Up || e.Key == Key.W) y = -1;
            else if (e.Key == Key.Down || e.Key == Key.S) y = 1;
            else return;
            if (!Paused) Board.Turn(x, y); e.Handled = true;
        }
        private void Finish()
        {
            timer.Stop(); message.Text = Board.Won ? "把棋盘都填满了，太厉害啦！" : "这一局结束，按空格再来。";
            if (!recorded) {
                recorded = true;
                try { if (Host.Record(Board.Wrap ? "wrap" : "classic", Board.Score, false)) message.Text += " 新纪录！"; }
                catch (Exception ex) { message.Text += " 成绩未保存：" + ex.Message; }
            }
            pause.Content = "再来";
        }
        private void Paint()
        {
            canvas.Children.Clear();
            for (int y = 0; y < Board.Size; y++) for (int x = 0; x < Board.Size; x++)
                if ((x + y) % 2 == 0) AddCell(y * Board.Size + x, Ui.Color("#E8EADD"), 0);
            if (Board.Food >= 0) AddCell(Board.Food, Ui.Gold, 6);
            for (int i = Board.Body.Count - 1; i >= 0; i--) AddCell(Board.Body[i], i == 0 ? Ui.Red : Ui.Color("#608575"), 4);
            int head = Board.Body[0]; Ellipse eye = new Ellipse { Width = 4, Height = 4, Fill = Brushes.White };
            Canvas.SetLeft(eye, (head % Board.Size) * 20 + 10); Canvas.SetTop(eye, (head / Board.Size) * 20 + 5); canvas.Children.Add(eye);
            score.Text = "本局 " + Board.Score + "   ·   最佳 " + Host.Best(Board.Wrap ? "wrap" : "classic", 0) + (Paused && !Board.Ended ? "   ·   已暂停" : "");
        }
        private void AddCell(int index, Brush color, double corner)
        {
            Rectangle cell = new Rectangle { Width = 18, Height = 18, RadiusX = corner, RadiusY = corner, Fill = color };
            Canvas.SetLeft(cell, index % Board.Size * 20 + 1); Canvas.SetTop(cell, index / Board.Size * 20 + 1); canvas.Children.Add(cell);
        }
        private void ApplyPause()
        {
            if (Paused || Board.Ended) timer.Stop(); else timer.Start();
            pause.Content = Board.Ended ? "再来" : manualPaused ? "开始 / 继续" : "暂停";
            if (canvas != null) Paint();
        }
        public override void SetHostPaused(bool paused) { hostPaused = paused; if (timer != null) ApplyPause(); }
        public override void Dispose() { if (timer != null) timer.Stop(); PreviewKeyDown -= OnKey; }
    }
}
