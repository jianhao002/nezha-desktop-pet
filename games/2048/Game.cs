using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using NezhaDesktop;
using NezhaDesktop.MiniGames;

namespace NezhaGames
{
    public sealed class MergeBoard
    {
        public readonly int[] Cells = new int[16];
        public int Score { get; private set; }
        public bool Won { get { return Cells.Any(v => v >= 2048); } }
        public bool Ended { get { return !CanMove(); } }
        private readonly Random random;
        public MergeBoard(Random random) { this.random = random; Spawn(); Spawn(); }
        // 0 left, 1 right, 2 up, 3 down. A tile merges at most once per move.
        public bool Move(int direction)
        {
            if (direction < 0 || direction > 3) throw new ArgumentOutOfRangeException("direction");
            bool changed = false;
            for (int line = 0; line < 4; line++)
            {
                int[] indices = Enumerable.Range(0, 4).Select(i => Index(direction, line, i)).ToArray();
                List<int> values = indices.Select(i => Cells[i]).Where(v => v > 0).ToList(), merged = new List<int>();
                for (int i = 0; i < values.Count; i++)
                {
                    int value = values[i];
                    if (i + 1 < values.Count && value == values[i + 1]) { value = checked(value * 2); Score = checked(Score + value); i++; }
                    merged.Add(value);
                }
                while (merged.Count < 4) merged.Add(0);
                for (int i = 0; i < 4; i++) { changed |= Cells[indices[i]] != merged[i]; Cells[indices[i]] = merged[i]; }
            }
            if (changed) Spawn(); return changed;
        }
        public bool CanMove()
        {
            for (int i = 0; i < 16; i++)
                if (Cells[i] == 0 || (i % 4 < 3 && Cells[i] == Cells[i + 1]) || (i / 4 < 3 && Cells[i] == Cells[i + 4])) return true;
            return false;
        }
        private void Spawn()
        {
            int[] empty = Enumerable.Range(0, 16).Where(i => Cells[i] == 0).ToArray();
            if (empty.Length > 0) Cells[empty[random.Next(empty.Length)]] = random.Next(10) == 0 ? 4 : 2;
        }
        private static int Index(int direction, int line, int cell)
        { return direction == 0 ? line * 4 + cell : direction == 1 ? line * 4 + 3 - cell : direction == 2 ? cell * 4 + line : (3 - cell) * 4 + line; }
    }
    public sealed class MergeGame : MiniGameControl
    {
        public MergeBoard Board { get; private set; }
        private TextBlock score, message;
        private UniformGrid tiles;
        private bool paused, announcedWin;
        protected override UIElement Build()
        {
            Grid layout = new Grid();
            foreach (GridLength height in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
                layout.RowDefinitions.Add(new RowDefinition { Height = height });
            DockPanel top = new DockPanel();
            Button restart = Ui.Button("新一局", NewGame, false); DockPanel.SetDock(restart, Dock.Right); top.Children.Add(restart);
            score = Ui.Text("", 13, Ui.Muted); score.VerticalAlignment = VerticalAlignment.Center; top.Children.Add(score); layout.Children.Add(top);
            tiles = new UniformGrid { Rows = 4, Columns = 4, Width = 288, Height = 288 };
            Viewbox box = new Viewbox { Child = tiles, Stretch = Stretch.Uniform, Margin = new Thickness(0, 10, 0, 8) };
            Grid.SetRow(box, 1); layout.Children.Add(box);
            StackPanel bottom = new StackPanel(); message = Ui.Text("", 12, Ui.Muted); message.MinHeight = 30; bottom.Children.Add(message);
            UniformGrid arrows = new UniformGrid { Columns = 4 };
            string[] labels = { "←", "→", "↑", "↓" };
            for (int i = 0; i < 4; i++) { int chosen = i; Button button = Ui.Button(labels[i], delegate { Move(chosen); }, false); button.HorizontalAlignment = HorizontalAlignment.Stretch; button.Margin = new Thickness(2); arrows.Children.Add(button); }
            bottom.Children.Add(arrows); Grid.SetRow(bottom, 2); layout.Children.Add(bottom);
            PreviewKeyDown += OnKey; NewGame(); return layout;
        }
        private void NewGame() { Board = new MergeBoard(new Random()); announcedWin = false; message.Text = "方向键 / WASD · 也可以点击下方箭头"; Paint(); Focus(); }
        private void OnKey(object sender, KeyEventArgs e)
        {
            int direction;
            if (e.Key == Key.Left || e.Key == Key.A) direction = 0;
            else if (e.Key == Key.Right || e.Key == Key.D) direction = 1;
            else if (e.Key == Key.Up || e.Key == Key.W) direction = 2;
            else if (e.Key == Key.Down || e.Key == Key.S) direction = 3;
            else return;
            Move(direction); e.Handled = true;
        }
        private void Move(int direction)
        {
            if (paused || !Board.Move(direction)) return;
            try { Host.Record("best", Board.Score, false); } catch (Exception ex) { message.Text = "成绩未保存：" + ex.Message; }
            if (Board.Won && !announcedWin) { announcedWin = true; message.Text = "合出 2048！还可以继续挑战更大的数字。"; }
            else if (Board.Ended) message.Text = "棋盘满啦，再开一局试试。";
            Paint(); Focus();
        }
        private void Paint()
        {
            tiles.Children.Clear();
            foreach (int value in Board.Cells)
            {
                string color = value == 0 ? "#E8E1D5" : value <= 4 ? "#EEE3CE" : value <= 16 ? "#E8BB82" : value <= 64 ? "#D68B66" : value <= 512 ? "#C86645" : "#A9382E";
                TextBlock number = Ui.Text(value == 0 ? "" : value.ToString(), value >= 1024 ? 23 : 29, value > 4 ? Brushes.White : Ui.Ink);
                number.FontWeight = FontWeights.Bold; number.TextAlignment = TextAlignment.Center; number.VerticalAlignment = VerticalAlignment.Center;
                tiles.Children.Add(new Border { Background = Ui.Color(color), CornerRadius = new CornerRadius(9), Margin = new Thickness(3), Child = number });
            }
            score.Text = "本局 " + Board.Score + " · 最佳 " + Host.Best("best", 0);
        }
        public override void SetHostPaused(bool value) { paused = value; }
        public override void Dispose() { PreviewKeyDown -= OnKey; }
    }
}
