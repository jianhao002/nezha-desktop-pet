using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using NezhaDesktop;
using NezhaDesktop.MiniGames;

namespace NezhaGames
{
    public sealed class MemoryBoard
    {
        public readonly int[] Cards;
        public readonly bool[] Open, Matched;
        public int First { get; private set; }
        public int Second { get; private set; }
        public int Turns { get; private set; }
        public int Pairs { get; private set; }
        public bool Pending { get { return Second >= 0; } }
        public bool Won { get { return Pairs * 2 == Cards.Length; } }
        public MemoryBoard(int pairs, Random random)
        {
            if (pairs < 2 || pairs > 12) throw new ArgumentOutOfRangeException("pairs");
            Cards = Enumerable.Range(0, pairs * 2).Select(i => i / 2).ToArray(); Open = new bool[Cards.Length]; Matched = new bool[Cards.Length]; First = Second = -1;
            for (int i = Cards.Length - 1; i > 0; i--) { int j = random.Next(i + 1), value = Cards[i]; Cards[i] = Cards[j]; Cards[j] = value; }
        }
        public bool Flip(int index)
        {
            if (Won || Pending || index < 0 || index >= Cards.Length || Open[index] || Matched[index]) return false;
            Open[index] = true;
            if (First < 0) First = index;
            else { Turns++; if (Cards[First] == Cards[index]) { Matched[First] = Matched[index] = true; Pairs++; First = -1; } else Second = index; }
            return true;
        }
        public void Resolve() { if (!Pending) return; Open[First] = Open[Second] = false; First = Second = -1; }
    }
    public sealed class MemoryGame : MiniGameControl
    {
        public MemoryBoard Board { get; private set; }
        private static readonly string[] Symbols = { "莲", "火", "风", "轮", "绫", "枪", "海", "云", "星", "山", "雷", "月" };
        private ComboBox difficulty;
        private UniformGrid cards;
        private TextBlock score, message;
        private DispatcherTimer timer;
        private bool paused, recorded, disposed;
        protected override UIElement Build()
        {
            Grid layout = new Grid(); foreach (GridLength row in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto }) layout.RowDefinitions.Add(new RowDefinition { Height = row });
            WrapPanel top = new WrapPanel(); difficulty = new ComboBox { ItemsSource = new[] { "入门 · 6 对", "标准 · 8 对", "挑战 · 12 对" }, SelectedIndex = 0, Width = 110, Margin = new Thickness(0, 0, 8, 6) };
            top.Children.Add(difficulty); top.Children.Add(Ui.Button("新一局", NewGame, false)); layout.Children.Add(top);
            score = Ui.Text("", 12, Ui.Muted); Grid.SetRow(score, 1); layout.Children.Add(score);
            cards = new UniformGrid { Width = 300, Height = 300 }; Viewbox box = new Viewbox { Child = cards, Stretch = Stretch.Uniform, Margin = new Thickness(0, 10, 0, 10) }; Grid.SetRow(box, 2); layout.Children.Add(box);
            message = Ui.Text("", 12, Ui.Muted); Grid.SetRow(message, 3); layout.Children.Add(message);
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) }; timer.Tick += OnResolve; NewGame(); return layout;
        }
        private void NewGame() { timer.Stop(); Board = new MemoryBoard(new[] { 6, 8, 12 }[Math.Max(0, difficulty.SelectedIndex)], new Random()); recorded = false; Paint(); }
        private void Flip(int index)
        {
            if (paused || disposed || !Board.Flip(index)) return;
            if (Board.Pending) timer.Start();
            if (Board.Won && !recorded) { recorded = true; try { Host.Record("turns-" + Board.Cards.Length / 2, Board.Turns, true); } catch (Exception ex) { Host.Notify("成绩未保存：" + ex.Message); } }
            Paint();
        }
        private void OnResolve(object sender, EventArgs e) { timer.Stop(); if (!paused && !disposed) { Board.Resolve(); Paint(); } }
        private void Paint()
        {
            cards.Columns = Board.Cards.Length == 24 ? 6 : 4; cards.Rows = Board.Cards.Length / cards.Columns; cards.Children.Clear();
            for (int i = 0; i < Board.Cards.Length; i++) {
                int index = i; bool visible = Board.Open[i] || Board.Matched[i]; Button card = Ui.Button(visible ? Symbols[Board.Cards[i]] : "·", delegate { Flip(index); }, false);
                card.Tag = index; card.Padding = new Thickness(0); card.FontSize = cards.Columns == 6 ? 27 : 36; card.Margin = new Thickness(3); card.HorizontalAlignment = HorizontalAlignment.Stretch; card.VerticalAlignment = VerticalAlignment.Stretch;
                card.Background = Board.Matched[i] ? Ui.Color("#DCE7DB") : visible ? Ui.Cream : Ui.Color("#E9DCC7"); card.Foreground = Ui.Red;
                card.IsEnabled = !paused && !Board.Pending && !Board.Matched[i] && !Board.Open[i]; cards.Children.Add(card);
            }
            int best = Host.Best("turns-" + Board.Cards.Length / 2, 0); score.Text = "配对 " + Board.Pairs + "/" + Board.Cards.Length / 2 + " · 翻牌 " + Board.Turns + " 次 · 最佳 " + (best == 0 ? "—" : best.ToString());
            message.Text = Board.Won ? "全部配对成功！哪吒为你记住了这一局。" : paused ? "已暂停，当前翻开的牌会保留。" : "每次翻两张，找到相同的汉字。";
        }
        public override void SetHostPaused(bool value) { paused = value; if (timer == null) return; timer.Stop(); if (!paused && !disposed && Board.Pending) timer.Start(); Paint(); }
        public override void Dispose() { disposed = true; if (timer != null) { timer.Stop(); timer.Tick -= OnResolve; } }
    }
}
