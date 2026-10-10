using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NezhaDesktop.MiniGames;

namespace NezhaDesktop
{
    public sealed class MiniGamePreferences
    {
        public int Mode { get; set; }
        public int OpacityPercent { get; set; }
        public bool PauseOnBlur { get; set; }
        public bool KeepOnTop { get; set; }
        public MiniGamePreferences() { Mode = 1; OpacityPercent = 92; PauseOnBlur = true; }
        public void Validate() { Mode = Math.Max(0, Math.Min(2, Mode)); OpacityPercent = Math.Max(25, Math.Min(100, OpacityPercent)); }
    }
    public sealed class MiniGameWindow : Window
    {
        private static readonly List<MiniGameWindow> windows = new List<MiniGameWindow>();
        private readonly MiniGameCatalog catalog;
        private readonly Func<PetSnapshot> snapshot;
        private readonly string preferencesFile;
        private readonly MiniGamePreferences preferences;
        private readonly Grid gameArea;
        private readonly TextBlock status, heading;
        private readonly ComboBox mode;
        private readonly Slider opacity;
        private readonly Button back;
        private IMiniGame active;
        private int generation;
        private bool closed, initializing = true;
        public FrameworkElement PreviewRoot { get; private set; }
        public IMiniGame ActiveGame { get { return active; } }
        public bool HostPaused { get { return !IsVisible || (preferences.PauseOnBlur && !IsActive); } }
        public MiniGameWindow(string root, string data, Func<PetSnapshot> snapshot, bool globalHotkey)
        {
            this.snapshot = snapshot; catalog = new MiniGameCatalog(root, data); preferencesFile = Path.Combine(data, "window.json");
            preferences = MiniGameFiles.ReadJson<MiniGamePreferences>(preferencesFile) ?? new MiniGamePreferences(); preferences.Validate();
            Title = "哪吒 · 休闲小窗"; FontFamily = Ui.Font; WindowStyle = WindowStyle.None; AllowsTransparency = true;
            Background = Brushes.Transparent; ResizeMode = ResizeMode.CanResizeWithGrip; MinWidth = 290; MinHeight = 380;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Border frame = new Border { Background = Ui.Cream, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(12) };
            Grid layout = new Grid();
            foreach (GridLength height in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
                layout.RowDefinitions.Add(new RowDefinition { Height = height });
            DockPanel title = new DockPanel { Margin = new Thickness(0, 0, 0, 10), Background = Brushes.Transparent };
            Button close = Ui.Button("×", Close, false); close.Padding = new Thickness(8, 2, 8, 2); DockPanel.SetDock(close, Dock.Right); title.Children.Add(close);
            Button hide = Ui.Button("收起", QuickHide, false); hide.Padding = new Thickness(9, 3, 9, 3); hide.Margin = new Thickness(0, 0, 6, 0); DockPanel.SetDock(hide, Dock.Right); title.Children.Add(hide);
            heading = Ui.Text("哪吒 · 休闲小窗", 16, Ui.Ink); heading.VerticalAlignment = VerticalAlignment.Center; title.Children.Add(heading);
            heading.MouseLeftButtonDown += delegate { if (Mouse.LeftButton == MouseButtonState.Pressed) DragMove(); };
            layout.Children.Add(title);
            WrapPanel settings = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            back = Ui.Button("游戏列表", ShowCatalog, false); back.Padding = new Thickness(8, 5, 8, 5); back.Margin = new Thickness(0, 0, 7, 5); settings.Children.Add(back);
            mode = new ComboBox { ItemsSource = new[] { "普通", "低调", "极简" }, SelectedIndex = preferences.Mode, Width = 64, Margin = new Thickness(0, 0, 7, 5) };
            settings.Children.Add(mode); settings.Children.Add(Ui.Text("透明度", 11, Ui.Muted));
            opacity = new Slider { Minimum = 25, Maximum = 100, Value = preferences.OpacityPercent, Width = 88, Margin = new Thickness(5, 2, 7, 5), TickFrequency = 5, IsSnapToTickEnabled = false };
            settings.Children.Add(opacity);
            CheckBox onTop = new CheckBox { Content = "置顶", IsChecked = preferences.KeepOnTop, FontSize = 11, Margin = new Thickness(0, 4, 8, 5) };
            CheckBox pause = new CheckBox { Content = "切走暂停", IsChecked = preferences.PauseOnBlur, FontSize = 11, Margin = new Thickness(0, 4, 0, 5) };
            settings.Children.Add(onTop); settings.Children.Add(pause); Grid.SetRow(settings, 1); layout.Children.Add(settings);
            gameArea = new Grid(); Grid.SetRow(gameArea, 2); layout.Children.Add(gameArea);
            status = Ui.Text(globalHotkey ? "Esc 收起 · Ctrl+Alt+H 收起 / 恢复" : "Esc 收起 · 从宠物右键或托盘恢复", 11, Ui.Muted);
            status.Margin = new Thickness(0, 8, 0, 0); Grid.SetRow(status, 3); layout.Children.Add(status);
            frame.Child = layout; PreviewRoot = frame; Content = frame;
            mode.SelectionChanged += delegate {
                if (initializing) return; preferences.Mode = mode.SelectedIndex; ApplyMode(true); SavePreferences();
            };
            opacity.ValueChanged += delegate { if (!initializing) { preferences.OpacityPercent = (int)Math.Round(opacity.Value); Opacity = preferences.OpacityPercent / 100.0; } };
            opacity.PreviewMouseLeftButtonUp += delegate { SavePreferences(); };
            onTop.Checked += delegate { preferences.KeepOnTop = true; Topmost = true; SavePreferences(); };
            onTop.Unchecked += delegate { preferences.KeepOnTop = false; Topmost = false; SavePreferences(); };
            pause.Checked += delegate { preferences.PauseOnBlur = true; PauseActive(); SavePreferences(); };
            pause.Unchecked += delegate { preferences.PauseOnBlur = false; PauseActive(); SavePreferences(); };
            Activated += delegate { PauseActive(); if (active != null) active.View.Focus(); };
            Deactivated += delegate { PauseActive(); };
            IsVisibleChanged += delegate { PauseActive(); };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { QuickHide(); e.Handled = true; } };
            Closed += delegate { closed = true; generation++; DisposeActive(); windows.Remove(this); SavePreferences(); };
            ApplyMode(false); initializing = false; windows.Add(this); ShowCatalog();
        }
        private void ApplyMode(bool preset)
        {
            Width = preferences.Mode == 0 ? 470 : preferences.Mode == 1 ? 380 : 310;
            Height = preferences.Mode == 0 ? 650 : preferences.Mode == 1 ? 550 : 465;
            ShowInTaskbar = preferences.Mode == 0; Topmost = preferences.KeepOnTop;
            if (preset) { preferences.OpacityPercent = preferences.Mode == 0 ? 100 : preferences.Mode == 1 ? 90 : 70; opacity.Value = preferences.OpacityPercent; }
            Opacity = preferences.OpacityPercent / 100.0;
            if (IsLoaded) { Rect area = SystemParameters.WorkArea; Left = Math.Max(area.Left, Math.Min(area.Right - Width, Left)); Top = Math.Max(area.Top, Math.Min(area.Bottom - Height, Top)); }
        }
        private void SavePreferences()
        {
            if (initializing) return;
            try { MiniGameFiles.WriteJson(preferencesFile, preferences); }
            catch (Exception ex) { status.Text = "窗口设置未保存：" + ex.Message; }
        }
        public void QuickHide() { if (active != null) SafePause(true); Hide(); SavePreferences(); }
        public void Restore() { if (!IsVisible) Show(); Activate(); PauseActive(); }
        private void PauseActive() { if (active != null) SafePause(HostPaused); }
        private void SafePause(bool paused)
        {
            try { active.SetHostPaused(paused); }
            catch (Exception ex) { GameFailed(ex); }
        }
        private void DisposeActive()
        {
            IMiniGame previous = active; active = null; gameArea.Children.Clear();
            if (previous != null) { try { previous.Dispose(); } catch { } }
        }
        public void ShowCatalog()
        {
            generation++; DisposeActive(); heading.Text = "哪吒 · 休闲小窗"; back.IsEnabled = false;
            StackPanel list = new StackPanel();
            try {
                foreach (MiniGameEntry entry in catalog.Scan())
                {
                    MiniGameEntry chosen = entry; StackPanel details = new StackPanel();
                    details.Children.Add(Ui.Text(entry.Manifest.Name, 18, Ui.Ink));
                    TextBlock description = Ui.Text(entry.Manifest.Description, 12, Ui.Muted); description.Margin = new Thickness(0, 6, 0, 9); details.Children.Add(description);
                    details.Children.Add(Ui.Button("打开", delegate { StartGame(chosen); }, true)); list.Children.Add(Ui.Card(details));
                }
                foreach (string error in catalog.Errors) { TextBlock warning = Ui.Text(error, 11, Ui.Red); warning.Margin = new Thickness(0, 0, 0, 8); list.Children.Add(warning); }
                WrapPanel controls = new WrapPanel(); controls.Children.Add(Ui.Button("刷新列表", ShowCatalog, false));
                Button folder = Ui.Button("添加游戏", delegate { Process.Start(new ProcessStartInfo(catalog.GamesDirectory) { UseShellExecute = true }); }, false);
                folder.Margin = new Thickness(7, 0, 0, 0); controls.Children.Add(folder); list.Children.Add(controls);
                TextBlock help = Ui.Text("把新游戏文件夹放入打开的目录，再刷新列表。", 11, Ui.Muted); help.Margin = new Thickness(0, 9, 0, 0); list.Children.Add(help);
            } catch (Exception ex) { list.Children.Add(Ui.Text("游戏目录无法读取：" + ex.Message, 12, Ui.Red)); }
            gameArea.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        }
        public async void StartGame(MiniGameEntry entry)
        {
            int ticket = ++generation; DisposeActive(); back.IsEnabled = true; heading.Text = entry.Manifest.Name;
            gameArea.Children.Add(Ui.Text("准备游戏…", 14, Ui.Muted));
            try {
                Type type = await Task.Run(delegate { return catalog.Compile(entry); });
                if (closed || ticket != generation) return;
                active = catalog.Create(entry, type, snapshot, delegate(string text) { status.Text = text; });
                gameArea.Children.Clear(); gameArea.Children.Add(active.View); SafePause(HostPaused); active.View.Focus();
            } catch (Exception ex) { if (!closed && ticket == generation) GameFailed(ex); }
        }
        public void StartForTest(MiniGameEntry entry)
        {
            generation++; DisposeActive(); active = catalog.Create(entry, snapshot, delegate(string text) { status.Text = text; });
            heading.Text = entry.Manifest.Name; back.IsEnabled = true; gameArea.Children.Add(active.View); SafePause(HostPaused);
        }
        private void GameFailed(Exception ex)
        {
            generation++; DisposeActive(); back.IsEnabled = true;
            gameArea.Children.Add(Ui.Text("这个游戏暂时无法运行：" + ex.Message + "\n可以返回列表打开其他游戏。", 13, Ui.Red));
        }
        public static bool HandlePluginFailure(Exception exception)
        {
            System.Diagnostics.StackTrace trace = new System.Diagnostics.StackTrace(exception);
            foreach (MiniGameWindow window in windows.ToArray())
            {
                if (window.active == null) continue;
                System.Reflection.Assembly plugin = window.active.GetType().Assembly;
                if ((trace.GetFrames() ?? new System.Diagnostics.StackFrame[0]).Any(frame => frame.GetMethod() != null && frame.GetMethod().DeclaringType != null && frame.GetMethod().DeclaringType.Assembly == plugin))
                { window.GameFailed(exception); return true; }
            }
            return false;
        }
    }
}
