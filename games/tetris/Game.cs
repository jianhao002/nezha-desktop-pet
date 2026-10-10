using System;
using System.Collections.Generic;
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
    public sealed class BlockBoard
    {
        public const int Width = 10, Height = 20;
        public readonly int[] Cells = new int[Width * Height];
        private static readonly int[][] Shapes = {
            new[] { 4, 5, 6, 7 }, new[] { 1, 2, 5, 6 }, new[] { 1, 4, 5, 6 },
            new[] { 0, 4, 5, 6 }, new[] { 2, 4, 5, 6 }, new[] { 1, 2, 4, 5 }, new[] { 0, 1, 5, 6 }
        };
        private readonly Queue<int> bag = new Queue<int>();
        private readonly Random random;
        private int[] shape;
        public int Kind { get; private set; }
        public int NextKind { get; private set; }
        public int X { get; private set; }
        public int Y { get; private set; }
        public int Score { get; private set; }
        public int Lines { get; private set; }
        public int Level { get { return 1 + Lines / 10; } }
        public bool Ended { get; private set; }
        public BlockBoard(Random random) { this.random = random; NextKind = Take(); Spawn(); }
        private int Take()
        {
            if (bag.Count == 0) { int[] kinds = Enumerable.Range(0, 7).ToArray(); for (int i = 6; i > 0; i--) { int j = random.Next(i + 1), old = kinds[i]; kinds[i] = kinds[j]; kinds[j] = old; } foreach (int kind in kinds) bag.Enqueue(kind); }
            return bag.Dequeue();
        }
        private void Spawn() { Kind = NextKind; NextKind = Take(); shape = (int[])Shapes[Kind].Clone(); X = 3; Y = 0; if (!Fits(shape, X, Y)) Ended = true; }
        private bool Fits(int[] piece, int x, int y)
        {
            foreach (int part in piece) { int nx = x + part % 4, ny = y + part / 4; if (nx < 0 || nx >= Width || ny < 0 || ny >= Height || Cells[ny * Width + nx] != 0) return false; } return true;
        }
        public int[] ActiveCells() { return shape.Select(p => (Y + p / 4) * Width + X + p % 4).ToArray(); }
        public int[] GhostCells() { int y = Y; while (Fits(shape, X, y + 1)) y++; return shape.Select(p => (y + p / 4) * Width + X + p % 4).ToArray(); }
        public int[] PreviewCells() { return (int[])Shapes[NextKind].Clone(); }
        public bool Move(int dx, int dy)
        {
            if (Ended || Math.Abs(dx) + Math.Abs(dy) != 1 || dy < 0 || !Fits(shape, X + dx, Y + dy)) return false;
            X += dx; Y += dy; return true;
        }
        public bool Rotate()
        {
            if (Ended || Kind == 1) return false; int[] rotated = shape.Select(p => p % 4 * 4 + 3 - p / 4).ToArray();
            foreach (int dy in new[] { 0, -1, -2 }) foreach (int dx in new[] { 0, -1, 1, -2, 2 })
                if (Fits(rotated, X + dx, Y + dy)) { shape = rotated; X += dx; Y += dy; return true; }
            return false;
        }
        public void Step() { if (Ended) return; if (!Move(0, 1)) Lock(); }
        public void SoftDrop() { if (Ended) return; if (Move(0, 1)) Score++; else Lock(); }
        public void HardDrop() { if (Ended) return; while (Move(0, 1)) Score += 2; Lock(); }
        private void Lock()
        {
            foreach (int i in ActiveCells()) Cells[i] = Kind + 1;
            int cleared = ClearLines(); Score += new[] { 0, 100, 300, 500, 800 }[cleared] * Level; Lines += cleared; Spawn();
        }
        public int ClearLines()
        {
            int removed = 0;
            for (int y = Height - 1; y >= 0; y--) {
                if (!Enumerable.Range(0, Width).All(x => Cells[y * Width + x] != 0)) continue;
                for (int row = y; row > 0; row--) Array.Copy(Cells, (row - 1) * Width, Cells, row * Width, Width);
                Array.Clear(Cells, 0, Width); removed++; y++;
            }
            return removed;
        }
    }
    public sealed class BlockGame : MiniGameControl
    {
        public BlockBoard Board { get; private set; }
        private static readonly string[] Colors = { "#C6BEB1", "#4E94A0", "#C49B58", "#98749D", "#607CA6", "#C28356", "#608575", "#B45A56" };
        private ComboBox speed;
        private Canvas canvas;
        private TextBlock score, message;
        private Button pause;
        private DispatcherTimer timer;
        private int tier, baseInterval;
        private bool hostPaused, manualPaused = true, recorded, disposed;
        private bool Paused { get { return hostPaused || manualPaused || disposed; } }
        protected override UIElement Build()
        {
            Grid layout = new Grid(); foreach (GridLength row in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto }) layout.RowDefinitions.Add(new RowDefinition { Height = row });
            WrapPanel top = new WrapPanel(); speed = new ComboBox { ItemsSource = new[] { "悠闲", "标准", "快速", "挑战" }, SelectedIndex = 1, Width = 72, Margin = new Thickness(0, 0, 6, 6) };
            top.Children.Add(speed); top.Children.Add(Ui.Button("新一局", NewGame, false)); pause = Ui.Button("开始", Toggle, true); pause.Margin = new Thickness(6, 0, 0, 6); top.Children.Add(pause); layout.Children.Add(top);
            score = Ui.Text("", 12, Ui.Muted); Grid.SetRow(score, 1); layout.Children.Add(score);
            canvas = new Canvas { Width = 264, Height = 400, Background = Ui.Color("#F1EBE0") }; Viewbox box = new Viewbox { Child = canvas, Stretch = Stretch.Uniform, Margin = new Thickness(0, 8, 0, 8) }; Grid.SetRow(box, 2); layout.Children.Add(box);
            UniformGrid actions = new UniformGrid { Columns = 4 }; string[] labels = { "←", "旋转", "→", "直落" };
            for (int i = 0; i < labels.Length; i++) { int choice = i; Button button = Ui.Button(labels[i], delegate { Act(choice); }, false); button.Padding = new Thickness(5); button.Margin = new Thickness(2); button.HorizontalAlignment = HorizontalAlignment.Stretch; actions.Children.Add(button); }
            Grid.SetRow(actions, 3); layout.Children.Add(actions); message = Ui.Text("", 11, Ui.Muted); message.Margin = new Thickness(0, 6, 0, 0); Grid.SetRow(message, 4); layout.Children.Add(message);
            timer = new DispatcherTimer(); timer.Tick += OnTick; PreviewKeyDown += OnKey; NewGame(); return layout;
        }
        private void NewGame() { timer.Stop(); tier = Math.Max(0, speed.SelectedIndex); baseInterval = new[] { 900, 650, 420, 250 }[tier]; Board = new BlockBoard(new Random()); manualPaused = true; recorded = false; ApplyPause(); }
        private void Toggle() { if (Board.Ended) NewGame(); manualPaused = !manualPaused; ApplyPause(); Focus(); }
        private void OnTick(object sender, EventArgs e) { if (!Paused && !Board.Ended) { Board.Step(); AfterMove(); } }
        private void Act(int action)
        { if (Paused || Board.Ended) return; if (action == 0) Board.Move(-1, 0); else if (action == 1) Board.Rotate(); else if (action == 2) Board.Move(1, 0); else Board.HardDrop(); AfterMove(); Focus(); }
        private void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.P) { Toggle(); e.Handled = true; return; }
            if (e.Key == Key.Left || e.Key == Key.A) Act(0); else if (e.Key == Key.Up || e.Key == Key.W) Act(1); else if (e.Key == Key.Right || e.Key == Key.D) Act(2); else if (e.Key == Key.Space) Act(3);
            else if (e.Key == Key.Down || e.Key == Key.S) { if (!Paused && !Board.Ended) { Board.SoftDrop(); AfterMove(); } } else return; e.Handled = true;
        }
        private void AfterMove()
        {
            if (Board.Ended) { timer.Stop(); if (!recorded) { recorded = true; try { Host.Record("score-" + tier, Board.Score, false); } catch (Exception ex) { Host.Notify("成绩未保存：" + ex.Message); } } }
            timer.Interval = TimeSpan.FromMilliseconds(Math.Max(100, baseInterval - (Board.Level - 1) * 60)); Paint();
        }
        private void ApplyPause() { timer.Stop(); timer.Interval = TimeSpan.FromMilliseconds(Math.Max(100, baseInterval - (Board.Level - 1) * 60)); if (!Paused && !Board.Ended) timer.Start(); Paint(); }
        private void AddBlock(int x, int y, int value, double cell)
        { Rectangle part = new Rectangle { Width = cell - 2, Height = cell - 2, RadiusX = 2, RadiusY = 2, Fill = Ui.Color(Colors[value]) }; Canvas.SetLeft(part, x + 1); Canvas.SetTop(part, y + 1); canvas.Children.Add(part); }
        private void Paint()
        {
            canvas.Children.Clear(); for (int i = 0; i < Board.Cells.Length; i++) if (Board.Cells[i] > 0) AddBlock(i % 10 * 20, i / 10 * 20, Board.Cells[i], 20);
            if (!Board.Ended) { foreach (int i in Board.GhostCells()) AddBlock(i % 10 * 20, i / 10 * 20, 0, 20); foreach (int i in Board.ActiveCells()) AddBlock(i % 10 * 20, i / 10 * 20, Board.Kind + 1, 20); }
            TextBlock next = Ui.Text("下一块", 12, Ui.Muted); Canvas.SetLeft(next, 209); Canvas.SetTop(next, 12); canvas.Children.Add(next);
            foreach (int p in Board.PreviewCells()) AddBlock(207 + p % 4 * 13, 38 + p / 4 * 13, Board.NextKind + 1, 13);
            Rectangle separator = new Rectangle { Width = 1, Height = 400, Fill = Ui.Line }; Canvas.SetLeft(separator, 201); canvas.Children.Add(separator);
            score.Text = "分数 " + Board.Score + " · 消行 " + Board.Lines + " · Lv." + Board.Level + " · 最佳 " + Host.Best("score-" + tier, 0);
            pause.Content = Board.Ended ? "再来" : manualPaused ? "开始 / 继续" : "暂停";
            message.Text = Board.Ended ? "方块堆到顶了，按 P 再来。" : Paused ? "已暂停 · P 开始 / 继续" : "← → 移动 · ↑ 旋转 · ↓ 加速 · 空格直落 · P 暂停";
        }
        public override void SetHostPaused(bool value) { hostPaused = value; if (timer != null) ApplyPause(); }
        public override void Dispose() { disposed = true; if (timer != null) { timer.Stop(); timer.Tick -= OnTick; } PreviewKeyDown -= OnKey; }
    }
}
