using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NezhaDesktop;
using NezhaDesktop.MiniGames;

namespace NezhaGames
{
    public sealed class MinesBoard
    {
        public readonly int Width, Height, MineCount;
        public readonly bool[] Mines, Open, Flags;
        public readonly int[] Adjacent;
        public bool Generated { get; private set; }
        public bool Lost { get; private set; }
        public bool Won { get; private set; }
        public int FlagCount { get; private set; }
        public int OpenCount { get; private set; }
        private readonly Random random;
        public MinesBoard(int width, int height, int mineCount, Random random)
        {
            if (width < 5 || height < 5 || width > 24 || height > 24 || mineCount < 1 || mineCount > width * height - 9)
                throw new ArgumentException("棋盘尺寸或地雷数无效。");
            Width = width; Height = height; MineCount = mineCount; this.random = random;
            Mines = new bool[width * height]; Open = new bool[Mines.Length]; Flags = new bool[Mines.Length]; Adjacent = new int[Mines.Length];
        }
        public IEnumerable<int> Neighbors(int index)
        {
            int x = index % Width, y = index / Width;
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                if ((dx != 0 || dy != 0) && x + dx >= 0 && x + dx < Width && y + dy >= 0 && y + dy < Height)
                    yield return (y + dy) * Width + x + dx;
        }
        public bool ToggleFlag(int index)
        {
            Check(index); if (Won || Lost || Open[index] || (!Flags[index] && FlagCount >= MineCount)) return false;
            Flags[index] = !Flags[index]; FlagCount += Flags[index] ? 1 : -1; return true;
        }
        public bool Reveal(int index)
        {
            Check(index); if (Won || Lost || Flags[index] || Open[index]) return false;
            if (!Generated) Generate(index);
            if (Mines[index]) { Open[index] = true; Lost = true; return true; }
            Queue<int> pending = new Queue<int>(); pending.Enqueue(index);
            while (pending.Count > 0)
            {
                int current = pending.Dequeue();
                if (Open[current] || Flags[current] || Mines[current]) continue;
                Open[current] = true; OpenCount++;
                if (Adjacent[current] == 0) foreach (int neighbor in Neighbors(current)) if (!Open[neighbor]) pending.Enqueue(neighbor);
            }
            Won = OpenCount == Mines.Length - MineCount; return true;
        }
        public bool Chord(int index)
        {
            Check(index); if (!Open[index] || Won || Lost || Adjacent[index] == 0 || Neighbors(index).Count(n => Flags[n]) != Adjacent[index]) return false;
            bool changed = false;
            foreach (int neighbor in Neighbors(index).ToArray()) { changed |= Reveal(neighbor); if (Lost) break; }
            return changed;
        }
        private void Generate(int first)
        {
            HashSet<int> safe = new HashSet<int>(Neighbors(first)); safe.Add(first);
            List<int> possible = Enumerable.Range(0, Mines.Length).Where(i => !safe.Contains(i)).ToList();
            for (int i = 0; i < MineCount; i++) { int pick = random.Next(possible.Count); Mines[possible[pick]] = true; possible.RemoveAt(pick); }
            for (int i = 0; i < Mines.Length; i++) Adjacent[i] = Neighbors(i).Count(n => Mines[n]);
            Generated = true;
        }
        private void Check(int index) { if (index < 0 || index >= Mines.Length) throw new ArgumentOutOfRangeException("index"); }
    }

    public sealed class MinesweeperGame : MiniGameControl
    {
        public MinesBoard Board { get; private set; }
        private readonly Stopwatch clock = new Stopwatch();
        private DispatcherTimer timer;
        private UniformGrid grid;
        private Button[] cells;
        private TextBlock counter, message;
        private Button pause;
        private ComboBox difficulty;
        private bool hostPaused, manualPaused, scored;
        protected override UIElement Build()
        {
            Grid layout = new Grid();
            foreach (GridLength height in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
                layout.RowDefinitions.Add(new RowDefinition { Height = height });
            DockPanel bar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            difficulty = new ComboBox { Width = 95, Margin = new Thickness(0, 0, 8, 0), ItemsSource = new[] { "9 × 9 · 10 雷", "12 × 12 · 24 雷" }, SelectedIndex = 0 };
            bar.Children.Add(difficulty); bar.Children.Add(Ui.Button("新一局", NewGame, false));
            pause = Ui.Button("暂停", delegate { manualPaused = !manualPaused; ApplyPause(); }, false); pause.Margin = new Thickness(8, 0, 0, 0); bar.Children.Add(pause);
            layout.Children.Add(bar);
            counter = Ui.Text("", 12, Ui.Muted); counter.Margin = new Thickness(0, 0, 0, 7); Grid.SetRow(counter, 1); layout.Children.Add(counter);
            grid = new UniformGrid(); Viewbox boardBox = new Viewbox { Stretch = Stretch.Uniform, Child = grid, Margin = new Thickness(0, 0, 0, 8) };
            Grid.SetRow(boardBox, 2); layout.Children.Add(boardBox);
            message = Ui.Text("", 12, Ui.Muted); message.MinHeight = 32; Grid.SetRow(message, 3); layout.Children.Add(message);
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) }; timer.Tick += delegate { UpdateCounter(); };
            NewGame(); return layout;
        }
        private void NewGame()
        {
            int width = difficulty.SelectedIndex == 1 ? 12 : 9;
            Board = new MinesBoard(width, width, width == 9 ? 10 : 24, new Random());
            scored = false; manualPaused = false; clock.Reset(); grid.Rows = grid.Columns = width;
            grid.Width = grid.Height = width * 34; grid.Children.Clear(); cells = new Button[width * width];
            for (int i = 0; i < cells.Length; i++)
            {
                int index = i;
                Button cell = new Button { Width = 32, Height = 32, Margin = new Thickness(1), Padding = new Thickness(0), FontFamily = Ui.Font,
                    FontSize = 16, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand, BorderBrush = Ui.Line, BorderThickness = new Thickness(1) };
                cell.Click += delegate { Reveal(index, false); };
                cell.MouseRightButtonUp += delegate(object sender, MouseButtonEventArgs e) { if (!Paused) { Board.ToggleFlag(index); Paint(); } e.Handled = true; };
                cell.MouseDoubleClick += delegate(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) Reveal(index, true); };
                cells[i] = cell; grid.Children.Add(cell);
            }
            message.Text = "左键点开 · 右键插旗 · 双击数字快速展开"; Paint(); ApplyPause();
        }
        private bool Paused { get { return hostPaused || manualPaused; } }
        private void Reveal(int index, bool chord)
        {
            if (Paused) return;
            bool changed = chord ? Board.Chord(index) : Board.Reveal(index);
            if (!changed) return;
            if (!clock.IsRunning && !Board.Won && !Board.Lost) clock.Start();
            if (Board.Lost) { clock.Stop(); message.Text = "踩到雷啦，休息一下再试一局。"; }
            else if (Board.Won)
            {
                clock.Stop(); message.Text = "全部排除！哪吒为你鼓掌。";
                if (!scored) {
                    scored = true;
                    try { if (Host.Record("best-" + Board.Width, Math.Max(1, (int)Math.Min(Int32.MaxValue, clock.ElapsedMilliseconds)), true)) message.Text += " 新纪录！"; }
                    catch (Exception ex) { message.Text += " 成绩未保存：" + ex.Message; }
                }
            }
            ApplyPause(); Paint();
        }
        private void Paint()
        {
            Brush[] numbers = { Ui.Ink, Ui.Color("#4269AE"), Ui.Color("#438466"), Ui.Red, Ui.Color("#7660A4"), Ui.Color("#986B31"), Ui.Ink, Ui.Ink, Ui.Ink };
            for (int i = 0; i < cells.Length; i++)
            {
                Button cell = cells[i];
                bool mineShown = Board.Mines[i] && (Board.Lost || Board.Won);
                cell.Content = mineShown ? "●" : Board.Flags[i] ? "⚑" : Board.Open[i] && Board.Adjacent[i] > 0 ? Board.Adjacent[i].ToString() : "";
                cell.Background = mineShown && Board.Open[i] ? Ui.Color("#EBC1B4") : Board.Open[i] ? Ui.Cream : Ui.Color("#E6ECDD");
                cell.Foreground = mineShown || Board.Flags[i] ? Ui.Red : numbers[Board.Adjacent[i]];
            }
            grid.IsEnabled = !Paused; UpdateCounter();
        }
        private void UpdateCounter()
        {
            int best = Host.Best("best-" + Board.Width, 0);
            counter.Text = "余雷 " + (Board.MineCount - Board.FlagCount) + "   ·   " + (int)clock.Elapsed.TotalSeconds + " 秒" + (best > 0 ? "   ·   最快 " + (best / 1000.0).ToString("0.0") + " 秒" : "");
        }
        private void ApplyPause()
        {
            if (Paused || !Board.Generated || Board.Won || Board.Lost) { clock.Stop(); timer.Stop(); }
            else { clock.Start(); timer.Start(); }
            grid.IsEnabled = !Paused; pause.Content = manualPaused ? "继续" : "暂停";
        }
        public override void SetHostPaused(bool paused) { hostPaused = paused; if (grid != null) ApplyPause(); }
        public override void Dispose() { if (timer != null) timer.Stop(); clock.Stop(); }
    }
}
