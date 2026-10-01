using System;
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
    public sealed class SudokuBoard
    {
        public readonly int[] Cells = new int[81], Solution = new int[81];
        public readonly bool[] Given = new bool[81];
        public int Clues { get { return Given.Count(v => v); } }
        public bool Won { get { return Cells.SequenceEqual(Solution); } }
        public SudokuBoard(int clues, Random random)
        {
            if (clues < 26 || clues > 60) throw new ArgumentOutOfRangeException("clues");
            int[] rows = Order(random), columns = Order(random), digits = Shuffle(Enumerable.Range(1, 9).ToArray(), random);
            for (int r = 0; r < 9; r++) for (int c = 0; c < 9; c++) Cells[r * 9 + c] = Solution[r * 9 + c] = digits[(rows[r] * 3 + rows[r] / 3 + columns[c]) % 9];
            int remaining = 81;
            foreach (int i in Shuffle(Enumerable.Range(0, 81).ToArray(), random)) {
                if (remaining <= clues) break; int old = Cells[i]; Cells[i] = 0;
                if (CountSolutions(Cells, 2) == 1) remaining--; else Cells[i] = old;
            }
            for (int i = 0; i < 81; i++) Given[i] = Cells[i] != 0;
        }
        private static int[] Shuffle(int[] items, Random random) { for (int i = items.Length - 1; i > 0; i--) { int j = random.Next(i + 1), old = items[i]; items[i] = items[j]; items[j] = old; } return items; }
        private static int[] Order(Random random) { return Shuffle(new[] { 0, 1, 2 }, random).SelectMany(group => Shuffle(new[] { 0, 1, 2 }, random).Select(i => group * 3 + i)).ToArray(); }
        private static int Box(int i) { return i / 27 * 3 + i % 9 / 3; }
        public static int CountSolutions(int[] source, int limit)
        {
            if (source == null || source.Length != 81 || limit < 1) throw new ArgumentException("无效题目。");
            int[] cells = (int[])source.Clone(), rows = new int[9], columns = new int[9], boxes = new int[9];
            for (int i = 0; i < 81; i++) {
                int value = cells[i]; if (value < 0 || value > 9) return 0; if (value == 0) continue; int bit = 1 << value;
                if (((rows[i / 9] | columns[i % 9] | boxes[Box(i)]) & bit) != 0) return 0;
                rows[i / 9] |= bit; columns[i % 9] |= bit; boxes[Box(i)] |= bit;
            }
            return Search(cells, rows, columns, boxes, limit);
        }
        private static int Search(int[] cells, int[] rows, int[] columns, int[] boxes, int limit)
        {
            int chosen = -1, candidates = 0, fewest = 10;
            for (int i = 0; i < 81; i++) if (cells[i] == 0) {
                int mask = 1022 & ~(rows[i / 9] | columns[i % 9] | boxes[Box(i)]), count = 0;
                for (int bits = mask; bits != 0; bits &= bits - 1) count++;
                if (count == 0) return 0; if (count < fewest) { fewest = count; candidates = mask; chosen = i; if (count == 1) break; }
            }
            if (chosen < 0) return 1;
            int r = chosen / 9, c = chosen % 9, b = Box(chosen), found = 0;
            for (int value = 1; value <= 9 && found < limit; value++) {
                int bit = 1 << value; if ((candidates & bit) == 0) continue;
                cells[chosen] = value; rows[r] |= bit; columns[c] |= bit; boxes[b] |= bit;
                found += Search(cells, rows, columns, boxes, limit - found);
                cells[chosen] = 0; rows[r] &= ~bit; columns[c] &= ~bit; boxes[b] &= ~bit;
            }
            return found;
        }
        public bool Set(int index, int value) { if (Won || index < 0 || index >= 81 || Given[index] || value < 0 || value > 9 || Cells[index] == value) return false; Cells[index] = value; return true; }
        public bool Conflict(int index)
        {
            if (index < 0 || index >= 81 || Cells[index] == 0) return false;
            return Enumerable.Range(0, 81).Any(i => i != index && Cells[i] == Cells[index] && (i / 9 == index / 9 || i % 9 == index % 9 || Box(i) == Box(index)));
        }
    }
    public sealed class SudokuGame : MiniGameControl
    {
        public SudokuBoard Board { get; private set; }
        public int ElapsedSeconds { get; private set; }
        public int Hints { get; private set; }
        private ComboBox difficulty;
        private UniformGrid cells;
        private TextBlock score, message;
        private DispatcherTimer timer;
        private int selected, tier;
        private bool paused, started, recorded, disposed;
        protected override UIElement Build()
        {
            Grid layout = new Grid(); foreach (GridLength row in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto }) layout.RowDefinitions.Add(new RowDefinition { Height = row });
            WrapPanel top = new WrapPanel(); difficulty = new ComboBox { ItemsSource = new[] { "入门", "简单", "标准", "挑战" }, SelectedIndex = 1, Width = 72, Margin = new Thickness(0, 0, 6, 6) };
            top.Children.Add(difficulty); top.Children.Add(Ui.Button("新一局", NewGame, false)); Button hint = Ui.Button("提示", Hint, false); hint.Margin = new Thickness(6, 0, 0, 6); top.Children.Add(hint); layout.Children.Add(top);
            score = Ui.Text("", 12, Ui.Muted); Grid.SetRow(score, 1); layout.Children.Add(score);
            cells = new UniformGrid { Rows = 9, Columns = 9, Width = 324, Height = 324 }; Viewbox box = new Viewbox { Child = cells, Stretch = Stretch.Uniform, Margin = new Thickness(0, 8, 0, 8) }; Grid.SetRow(box, 2); layout.Children.Add(box);
            UniformGrid numbers = new UniformGrid { Columns = 5 }; for (int n = 1; n <= 10; n++) {
                int value = n == 10 ? 0 : n; Button number = Ui.Button(value == 0 ? "清除" : value.ToString(), delegate { Input(value, false); }, false);
                number.Tag = "number-" + value; number.Padding = new Thickness(4); number.Margin = new Thickness(2); number.HorizontalAlignment = HorizontalAlignment.Stretch; numbers.Children.Add(number);
            }
            Grid.SetRow(numbers, 3); layout.Children.Add(numbers); message = Ui.Text("", 11, Ui.Muted); message.Margin = new Thickness(0, 6, 0, 0); Grid.SetRow(message, 4); layout.Children.Add(message);
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; timer.Tick += OnTick; PreviewKeyDown += OnKey; NewGame(); return layout;
        }
        private void NewGame() { timer.Stop(); tier = Math.Max(0, difficulty.SelectedIndex); Board = new SudokuBoard(new[] { 46, 40, 34, 30 }[tier], new Random()); selected = Array.FindIndex(Board.Given, v => !v); started = recorded = false; Hints = ElapsedSeconds = 0; Paint(); }
        private void OnTick(object sender, EventArgs e) { if (!paused && !disposed && !Board.Won) { ElapsedSeconds++; PaintScore(); } }
        private void Input(int value, bool hint)
        {
            if (paused || disposed || !Board.Set(selected, value)) return;
            started = true; if (hint) Hints++;
            if (Board.Won) {
                timer.Stop(); if (!recorded && Hints == 0) { recorded = true; try { Host.Record("seconds-" + tier, Math.Max(1, ElapsedSeconds), true); } catch (Exception ex) { Host.Notify("成绩未保存：" + ex.Message); } }
            } else timer.Start();
            Paint(); Focus();
        }
        private void Hint()
        {
            if (paused || Board.Won) return;
            if (Board.Given[selected] || Board.Cells[selected] == Board.Solution[selected]) selected = Enumerable.Range(0, 81).First(i => !Board.Given[i] && Board.Cells[i] != Board.Solution[i]);
            Input(Board.Solution[selected], true);
        }
        private void OnKey(object sender, KeyEventArgs e)
        {
            if (paused) return; int key = (int)e.Key;
            if (key >= (int)Key.D1 && key <= (int)Key.D9) Input(key - (int)Key.D0, false);
            else if (key >= (int)Key.NumPad1 && key <= (int)Key.NumPad9) Input(key - (int)Key.NumPad0, false);
            else if (e.Key == Key.Back || e.Key == Key.Delete || e.Key == Key.D0) Input(0, false);
            else if (e.Key == Key.Left) selected = Math.Max(0, selected - 1); else if (e.Key == Key.Right) selected = Math.Min(80, selected + 1);
            else if (e.Key == Key.Up) selected = Math.Max(0, selected - 9); else if (e.Key == Key.Down) selected = Math.Min(80, selected + 9); else return;
            Paint(); e.Handled = true;
        }
        private void PaintScore() { int best = Host.Best("seconds-" + tier, 0); score.Text = "用时 " + ElapsedSeconds + " 秒 · 提示 " + Hints + " · 最佳 " + (best == 0 ? "—" : best + " 秒"); }
        private void Paint()
        {
            cells.Children.Clear(); for (int i = 0; i < 81; i++) {
                int index = i; Button cell = Ui.Button(Board.Cells[i] == 0 ? "" : Board.Cells[i].ToString(), delegate { if (!paused) { selected = index; Paint(); Focus(); } }, false);
                cell.Tag = index; cell.FontSize = 22; cell.FontWeight = Board.Given[i] ? FontWeights.Bold : FontWeights.Normal; cell.Padding = new Thickness(0);
                cell.HorizontalAlignment = HorizontalAlignment.Stretch; cell.VerticalAlignment = VerticalAlignment.Stretch;
                cell.Margin = new Thickness(i % 9 % 3 == 0 ? 2 : 0.5, i / 9 % 3 == 0 ? 2 : 0.5, 0.5, 0.5);
                cell.Foreground = Board.Conflict(i) ? Ui.Red : Board.Given[i] ? Ui.Ink : Ui.Color("#427A8B");
                cell.Background = selected == i ? Ui.Color("#ECD8B2") : Board.Given[i] ? Ui.Color("#EEE8DD") : Brushes.White; cells.Children.Add(cell);
            }
            PaintScore(); message.Text = Board.Won ? Hints == 0 ? "完成啦！这一局已计入最佳成绩。" : "练习完成！使用提示的本局不计最佳。" : paused ? "已暂停" : "每行、列、九宫格填 1～9；提示仅计练习。";
        }
        public override void SetHostPaused(bool value) { paused = value; if (timer == null) return; timer.Stop(); if (!paused && started && !Board.Won && !disposed) timer.Start(); Paint(); }
        public override void Dispose() { disposed = true; if (timer != null) { timer.Stop(); timer.Tick -= OnTick; } PreviewKeyDown -= OnKey; }
    }
}
