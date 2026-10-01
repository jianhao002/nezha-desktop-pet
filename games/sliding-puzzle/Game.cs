using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using NezhaDesktop;
using NezhaDesktop.MiniGames;

namespace NezhaGames
{
    public sealed class SlidingBoard
    {
        public readonly int Size;
        public readonly int[] Cells;
        public int Blank { get; private set; }
        public int Moves { get; private set; }
        public bool Won { get { return Cells.Take(Cells.Length - 1).Select((v, i) => v == i + 1).All(v => v) && Cells[Cells.Length - 1] == 0; } }
        public SlidingBoard(int size, Random random)
        {
            if (size < 3 || size > 5) throw new ArgumentOutOfRangeException("size");
            Size = size; Cells = Enumerable.Range(1, size * size).ToArray(); Blank = Cells.Length - 1; Cells[Blank] = 0;
            int previous = -1;
            for (int turn = 0; turn < size * size * 30; turn++) {
                int[] choices = Enumerable.Range(0, Cells.Length).Where(i => Adjacent(i) && i != previous).ToArray();
                int next = choices[random.Next(choices.Length)], old = Blank; Swap(next); previous = old;
            }
            if (Won) Swap(Blank - 1);
        }
        private bool Adjacent(int index)
        { return index >= 0 && index < Cells.Length && Math.Abs(index % Size - Blank % Size) + Math.Abs(index / Size - Blank / Size) == 1; }
        private void Swap(int index) { Cells[Blank] = Cells[index]; Cells[index] = 0; Blank = index; }
        public bool Move(int index)
        { if (Won || !Adjacent(index)) return false; Swap(index); Moves++; return true; }
    }
    public sealed class SlidingGame : MiniGameControl
    {
        public SlidingBoard Board { get; private set; }
        private ComboBox size;
        private UniformGrid tiles;
        private TextBlock score, message;
        private bool paused, recorded;
        protected override UIElement Build()
        {
            Grid layout = new Grid();
            foreach (GridLength row in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto }) layout.RowDefinitions.Add(new RowDefinition { Height = row });
            WrapPanel top = new WrapPanel(); size = new ComboBox { ItemsSource = new[] { "入门 3×3", "标准 4×4", "挑战 5×5" }, SelectedIndex = 0, Width = 105, Margin = new Thickness(0, 0, 8, 6) };
            top.Children.Add(size); top.Children.Add(Ui.Button("新一局", NewGame, false)); layout.Children.Add(top);
            score = Ui.Text("", 12, Ui.Muted); Grid.SetRow(score, 1); layout.Children.Add(score);
            tiles = new UniformGrid { Width = 300, Height = 300 };
            Viewbox box = new Viewbox { Child = tiles, Stretch = System.Windows.Media.Stretch.Uniform, Margin = new Thickness(0, 10, 0, 10) }; Grid.SetRow(box, 2); layout.Children.Add(box);
            message = Ui.Text("", 12, Ui.Muted); Grid.SetRow(message, 3); layout.Children.Add(message);
            PreviewKeyDown += OnKey; NewGame(); return layout;
        }
        private void NewGame() { Board = new SlidingBoard(size.SelectedIndex + 3, new Random()); recorded = false; Paint(); Focus(); }
        private void Move(int index)
        {
            if (paused || !Board.Move(index)) return;
            if (Board.Won && !recorded) { recorded = true; try { Host.Record("moves-" + Board.Size, Board.Moves, true); } catch (Exception ex) { Host.Notify("成绩未保存：" + ex.Message); } }
            Paint(); Focus();
        }
        private void OnKey(object sender, KeyEventArgs e)
        {
            int x = Board.Blank % Board.Size, y = Board.Blank / Board.Size;
            if (e.Key == Key.Left || e.Key == Key.A) x--; else if (e.Key == Key.Right || e.Key == Key.D) x++;
            else if (e.Key == Key.Up || e.Key == Key.W) y--; else if (e.Key == Key.Down || e.Key == Key.S) y++; else return;
            if (x >= 0 && y >= 0 && x < Board.Size && y < Board.Size) Move(y * Board.Size + x); e.Handled = true;
        }
        private void Paint()
        {
            tiles.Rows = tiles.Columns = Board.Size; tiles.Children.Clear();
            for (int i = 0; i < Board.Cells.Length; i++) {
                int index = i, value = Board.Cells[i]; Button tile = Ui.Button(value == 0 ? "" : value.ToString(), delegate { Move(index); }, false);
                tile.Tag = index; tile.FontSize = Board.Size == 5 ? 22 : 30; tile.FontWeight = FontWeights.Bold; tile.Padding = new Thickness(0); tile.Margin = new Thickness(3);
                tile.HorizontalAlignment = HorizontalAlignment.Stretch; tile.VerticalAlignment = VerticalAlignment.Stretch;
                tile.Background = value == 0 ? Ui.Color("#E8E1D5") : Ui.Cream; tile.IsEnabled = !paused && value != 0 && !Board.Won; tiles.Children.Add(tile);
            }
            int best = Host.Best("moves-" + Board.Size, 0); score.Text = "移动 " + Board.Moves + " 步 · 最佳 " + (best == 0 ? "—" : best + " 步");
            message.Text = Board.Won ? "数字归位啦！可以换一个棋盘挑战。" : paused ? "已暂停" : "按顺序排列数字，把空格留在右下角。";
        }
        public override void SetHostPaused(bool value) { paused = value; if (Board != null) Paint(); }
        public override void Dispose() { PreviewKeyDown -= OnKey; }
    }
}
