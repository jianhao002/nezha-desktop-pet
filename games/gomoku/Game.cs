using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using NezhaDesktop;
using NezhaDesktop.MiniGames;

namespace NezhaGames
{
    public sealed class GomokuState { public int Size, Level, Last; public int[] Cells; }
    public sealed class GomokuBoard
    {
        public readonly int Size;
        public readonly int[] Cells;
        public int Turn { get; private set; }
        public int Winner { get; private set; }
        public int Moves { get; private set; }
        public int Last { get; private set; }
        public bool Ended { get { return Winner != 0; } }
        public GomokuBoard(int size) { if (size < 5 || size > 19) throw new ArgumentOutOfRangeException("size"); Size = size; Cells = new int[size * size]; Turn = 1; Last = -1; }
        public bool Play(int index, int player)
        {
            if (Ended || player != Turn || index < 0 || index >= Cells.Length || Cells[index] != 0) return false;
            Cells[index] = player; Moves++; Last = index;
            if (Wins(index, player)) Winner = player; else if (Moves == Cells.Length) Winner = 3; else Turn = 3 - player;
            return true;
        }
        private int Run(int x, int y, int dx, int dy, int player)
        { int count = 0; for (x += dx, y += dy; x >= 0 && y >= 0 && x < Size && y < Size && Cells[y * Size + x] == player; x += dx, y += dy) count++; return count; }
        private bool Wins(int index, int player)
        {
            int x = index % Size, y = index / Size;
            foreach (int[] d in new[] { new[] { 1, 0 }, new[] { 0, 1 }, new[] { 1, 1 }, new[] { 1, -1 } })
                if (1 + Run(x, y, d[0], d[1], player) + Run(x, y, -d[0], -d[1], player) >= 5) return true;
            return false;
        }
        private bool WouldWin(int index, int player) { Cells[index] = player; bool win = Wins(index, player); Cells[index] = 0; return win; }
        private int Potential(int index, int player)
        {
            int result = 0, x = index % Size, y = index / Size;
            foreach (int[] d in new[] { new[] { 1, 0 }, new[] { 0, 1 }, new[] { 1, 1 }, new[] { 1, -1 } }) {
                int left = Run(x, y, d[0], d[1], player), right = Run(x, y, -d[0], -d[1], player), length = left + right + 1, open = 0;
                foreach (int sign in new[] { -1, 1 }) {
                    int reach = (sign < 0 ? right : left) + 1, nx = x + d[0] * sign * reach, ny = y + d[1] * sign * reach;
                    if (nx >= 0 && ny >= 0 && nx < Size && ny < Size && Cells[ny * Size + nx] == 0) open++;
                }
                int weight = length >= 4 ? 2500 : length == 3 ? 180 : length == 2 ? 18 : 1; result += weight * (open + 1);
            }
            return result;
        }
        public int ChooseMove(Random random, int level)
        {
            if (Ended || Turn != 2) return -1;
            int[] available = Enumerable.Range(0, Cells.Length).Where(i => Cells[i] == 0).ToArray();
            foreach (int i in available) if (WouldWin(i, 2)) return i;
            if (level > 0) foreach (int i in available) if (WouldWin(i, 1)) return i;
            int[] near = available.Where(i => Enumerable.Range(0, Cells.Length).Any(j => Cells[j] != 0 && Math.Abs(i % Size - j % Size) <= 2 && Math.Abs(i / Size - j / Size) <= 2)).ToArray();
            if (near.Length == 0) return Size / 2 * Size + Size / 2;
            if (level == 0) return near[random.Next(near.Length)];
            int best = Int32.MinValue; List<int> choices = new List<int>();
            foreach (int i in near) {
                int score = Potential(i, 2) * 10 + Potential(i, 1) * (level >= 2 ? 11 : 5) - Math.Abs(i % Size - Size / 2) - Math.Abs(i / Size - Size / 2);
                if (score > best) { best = score; choices.Clear(); } if (score == best) choices.Add(i);
            }
            return choices[random.Next(choices.Count)];
        }
        public static GomokuBoard Restore(GomokuState state)
        {
            MiniGameSession.Require(new[]{9,13,15}.Contains(state.Size) && MiniGameSession.Range(state.Cells,state.Size*state.Size,0,2) && state.Cells.Count(v=>v==1)==state.Cells.Count(v=>v==2) && state.Last>=-1 && state.Last<state.Cells.Length);
            GomokuBoard board=new GomokuBoard(state.Size); Array.Copy(state.Cells,board.Cells,state.Cells.Length); board.Moves=state.Cells.Count(v=>v!=0); board.Last=state.Last;
            MiniGameSession.Require((board.Moves==0?state.Last==-1:state.Last>=0 && state.Cells[state.Last]==2) && !Enumerable.Range(0,state.Cells.Length).Any(i=>state.Cells[i]!=0 && board.Wins(i,state.Cells[i]))); return board;
        }
    }
    public sealed class GomokuGame : MiniGameControl
    {
        public GomokuBoard Board { get; private set; }
        private ComboBox size, opponent;
        private UniformGrid board;
        private TextBlock score, message;
        private bool paused, recorded;
        private int level;
        private readonly Random random = new Random();
        protected override UIElement Build()
        {
            Grid layout = new Grid(); foreach (GridLength row in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto }) layout.RowDefinitions.Add(new RowDefinition { Height = row });
            WrapPanel top = new WrapPanel(); size = new ComboBox { ItemsSource = new[] { "9×9", "13×13", "15×15" }, SelectedIndex = 0, Width = 72, Margin = new Thickness(0, 0, 6, 6) };
            opponent = new ComboBox { ItemsSource = new[] { "轻松", "标准", "认真" }, SelectedIndex = 1, Width = 66, Margin = new Thickness(0, 0, 6, 6) };
            top.Children.Add(size); top.Children.Add(opponent); top.Children.Add(Ui.Button("新一局", NewGame, false)); layout.Children.Add(top);
            score = Ui.Text("", 12, Ui.Muted); Grid.SetRow(score, 1); layout.Children.Add(score);
            board = new UniformGrid { Width = 330, Height = 330 }; Viewbox box = new Viewbox { Child = board, Stretch = Stretch.Uniform, Margin = new Thickness(0, 10, 0, 10) }; Grid.SetRow(box, 2); layout.Children.Add(box);
            message = Ui.Text("", 12, Ui.Muted); Grid.SetRow(message, 3); layout.Children.Add(message); NewGame(); return layout;
        }
        private string ScoreKey { get { return "moves-" + Board.Size + "-" + level; } }
        private void NewGame() { NewSession(); Board = new GomokuBoard(new[] { 9, 13, 15 }[Math.Max(0, size.SelectedIndex)]); level = Math.Max(0, opponent.SelectedIndex); recorded = false; Paint(); }
        private void Play(int index)
        {
            if (paused || !Board.Play(index, 1)) return;
            if (!Board.Ended) { int move = Board.ChooseMove(random, level); if (move >= 0) Board.Play(move, 2); }
            if (Board.Winner == 1 && !recorded) { recorded = true; try { Host.Record(ScoreKey, (Board.Moves + 1) / 2, true); } catch (Exception ex) { Host.Notify("成绩未保存：" + ex.Message); } }
            Paint();
        }
        private void Paint()
        {
            board.Rows = board.Columns = Board.Size; board.Children.Clear();
            for (int i = 0; i < Board.Cells.Length; i++) {
                int index = i; Button cell = Ui.Button("", delegate { Play(index); }, false); cell.Tag = index;
                cell.Padding = new Thickness(0); cell.Margin = new Thickness(0.5); cell.HorizontalAlignment = HorizontalAlignment.Stretch; cell.VerticalAlignment = VerticalAlignment.Stretch;
                cell.Background = i == Board.Last ? Ui.Color("#D6AD75") : Ui.Color("#ECD3AC");
                if (Board.Cells[i] != 0) cell.Content = new Ellipse { Width = 240.0 / Board.Size, Height = 240.0 / Board.Size, Fill = Board.Cells[i] == 1 ? Ui.Ink : Brushes.White, Stroke = Ui.Muted, StrokeThickness = 1 };
                cell.IsEnabled = !paused && !Board.Ended && Board.Cells[i] == 0; board.Children.Add(cell);
            }
            int best = Host.Best(ScoreKey, 0); score.Text = "你的落子 " + (Board.Moves + 1) / 2 + " · 最快获胜 " + (best == 0 ? "—" : best + " 手");
            message.Text = Board.Winner == 1 ? "你连成五子啦！" : Board.Winner == 2 ? "电脑先连成五子，再来一局吧。" : Board.Winner == 3 ? "棋盘满了，平局。" : paused ? "已暂停" : "你执黑先行 · 横、竖、斜连五子获胜 · 无禁手";
        }
        public override void SetHostPaused(bool value) { paused = value; if (Board != null) Paint(); }
        public override bool CanResume { get { return true; } }
        protected override object CaptureSession() { return Board.Ended ? null : new GomokuState {Size=Board.Size,Level=level,Last=Board.Last,Cells=Board.Cells}; }
        protected override void RestoreSession(string json) { GomokuState state=MiniGameSession.Read<GomokuState>(json); MiniGameSession.Require(state.Level>=0 && state.Level<3); Board=GomokuBoard.Restore(state); level=state.Level; size.SelectedIndex=Array.IndexOf(new[]{9,13,15},Board.Size); opponent.SelectedIndex=level; recorded=false; Paint(); }
        public override void Dispose() { }
    }
}
