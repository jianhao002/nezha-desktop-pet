using System;
using System.Windows;
using System.Windows.Controls;
using NezhaDesktop.MiniGames;

namespace NezhaExample
{
    // Copy this folder into the runtime's games folder, then refresh the list.
    public sealed class TapGame : MiniGameControl
    {
        public int Count { get; private set; }
        public string PetName { get; private set; }
        private Button tap;
        private TextBlock score;
        private bool paused;

        protected override UIElement Build()
        {
            PetSnapshot pet = Host.Pet;
            PetName = pet == null ? "哪吒" : pet.Name;
            StackPanel panel = new StackPanel { Margin = new Thickness(8) };
            panel.Children.Add(new TextBlock {
                Text = PetName + "陪你试试新游戏。", FontSize = 17,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16)
            });
            score = new TextBlock { FontSize = 15, Margin = new Thickness(0, 0, 0, 12) };
            panel.Children.Add(score);
            tap = new Button { Content = "点一下", Padding = new Thickness(12, 8, 12, 8) };
            tap.Click += delegate {
                if (paused) return;
                Count++;
                try { Host.Record("best", Count, false); }
                catch (Exception ex) { Host.Notify("成绩未保存：" + ex.Message); }
                Paint();
            };
            panel.Children.Add(tap);
            Button reset = new Button { Content = "重置本局", Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(12, 8, 12, 8) };
            reset.Click += delegate { Count = 0; Paint(); };
            panel.Children.Add(reset); Paint(); return panel;
        }
        private void Paint()
        { score.Text = "本局 " + Count + " · 最佳 " + Host.Best("best", 0) + (paused ? " · 已暂停" : ""); }
        public override void SetHostPaused(bool value)
        { paused = value; if (tap != null) { tap.IsEnabled = !paused; Paint(); } }
        public override void Dispose()
        {
            // Stop timers and unsubscribe external events here if the game uses them.
        }
    }
}
