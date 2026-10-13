using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace NezhaDesktop
{
    public class SpriteFrame
    {
        public BitmapSource Image;
        public byte[] Pixels;
        public bool Solid(double x, double y)
        {
            int px = (int)(x * Image.PixelWidth), py = (int)(y * Image.PixelHeight);
            return px >= 0 && py >= 0 && px < Image.PixelWidth && py < Image.PixelHeight && Pixels[(py * Image.PixelWidth + px) * 4 + 3] >= 22;
        }
    }
    public class SpriteLibrary
    {
        public Dictionary<string, SpriteFrame[]> Animations = new Dictionary<string, SpriteFrame[]>();
        public SpriteLibrary(PetConfig config, string root)
        {
            Dictionary<string, BitmapSource> sheets = new Dictionary<string, BitmapSource>();
            foreach (KeyValuePair<string, AnimationSpec> pair in config.Animations)
            {
                AnimationSpec spec = pair.Value;
                List<SpriteFrame> frames = new List<SpriteFrame>();
                if (spec.Files != null && spec.Files.Length > 0)
                {
                    foreach (string relative in spec.Files) frames.Add(Frame(Load(PetConfig.AssetPath(root, relative))));
                }
                else
                {
                    string path = PetConfig.AssetPath(root, spec.Sheet);
                    if (!sheets.ContainsKey(path)) sheets[path] = Load(path);
                    BitmapSource sheet = sheets[path];
                    int width = spec.FrameWidth > 0 ? spec.FrameWidth : config.FrameWidth;
                    int height = spec.FrameHeight > 0 ? spec.FrameHeight : config.FrameHeight;
                    foreach (int tile in spec.Frames)
                    {
                        int column = spec.Columns > 0 ? tile % spec.Columns : tile;
                        int row = spec.Row + (spec.Columns > 0 ? tile / spec.Columns : 0);
                        if ((column + 1) * (long)width > sheet.PixelWidth || (row + 1) * (long)height > sheet.PixelHeight)
                            throw new InvalidDataException("动作帧超出图集：" + pair.Key);
                        frames.Add(Frame(new CroppedBitmap(sheet, new Int32Rect(column * width, row * height, width, height))));
                    }
                }
                Animations[pair.Key] = frames.ToArray();
            }
        }
        private static BitmapSource Load(string path)
        {
            BitmapImage image = new BitmapImage();
            image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path); image.EndInit(); image.Freeze(); return image;
        }
        private static SpriteFrame Frame(BitmapSource original)
        {
            BitmapSource image = new FormatConvertedBitmap(original, PixelFormats.Bgra32, null, 0);
            image.Freeze(); byte[] pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
            image.CopyPixels(pixels, image.PixelWidth * 4, 0);
            return new SpriteFrame { Image = image, Pixels = pixels };
        }
    }
    public partial class PetWindow : Window
    {
        public readonly GameEngine Engine;
        public readonly SpriteLibrary Sprites;
        public Grid Surface { get; private set; }
        public int FramesPainted { get; private set; }
        public string CurrentAnimation { get { return animation; } }
        public int CurrentFrameIndex { get { return lastFrame; } }
        public double AnimationElapsedMilliseconds { get { return animationPlayer.ElapsedMilliseconds; } }
        public bool ReactionBusy { get { return animationPlayer.IsProtected; } }
        public bool CanFeed { get { return !ReactionBusy && !Engine.IsResting && Engine.State.Work == null; } }
        public bool HaloEffectActive { get { return effects.HaloActive; } }
        public bool WheelEffectActive { get { return effects.WheelsActive; } }
        public string LastNotice { get; private set; }
        public bool HasTray { get { return tray != null && tray.Visible; } }
        private readonly Image petImage;
        private readonly PetEffects effects;
        private readonly AnimationArbiter animationPlayer = new AnimationArbiter();
        private readonly Border bubble;
        private readonly TextBlock bubbleText;
        private readonly DispatcherTimer timer;
        private readonly Random random = new Random();
        private Forms.NotifyIcon tray;
        private Dashboard dashboard;
        private UpdateWindow updateWindow;
        private bool updateSaved;
        private SpriteFrame visibleFrame;
        private string animation = "idle";
        private DateTime lastAnimationTick, bubbleUntil, nextRoam, nextSave, lastTick, nextPanelRefresh;
        private int lastFrame = -1;
        private int moveDirection;
        private DateTime moveUntil;
        private bool pointerDown, dragged, shuttingDown, reportedSaveFailure;
        private Point downScreen;
        private double downLeft, downTop;

        public PetWindow(GameEngine engine, SpriteLibrary sprites, bool useTray)
        {
            Engine = engine; Sprites = sprites;
            Title = engine.Config.Name; WindowStyle = WindowStyle.None; AllowsTransparency = true;
            Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize; ShowActivated = false;
            Surface = new Grid { Background = Brushes.Transparent };
            Surface.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58) });
            Surface.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            bubbleText = new TextBlock { FontFamily = Ui.Font, FontSize = 12, Foreground = Ui.Ink, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
            bubble = new Border { Background = Ui.Cream, BorderBrush = Ui.Gold, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(12, 0, 12, 4), VerticalAlignment = VerticalAlignment.Bottom,
                Visibility = Visibility.Hidden, Child = bubbleText };
            Surface.Children.Add(bubble);
            effects = new PetEffects(); Grid.SetRow(effects, 1); Surface.Children.Add(effects);
            petImage = new Image { Stretch = Stretch.Fill, Cursor = Cursors.Hand };
            Grid.SetRow(petImage, 1); Surface.Children.Add(petImage); Content = Surface;
            SetSize(engine.State.PetSize);
            Rect area = SystemParameters.WorkArea;
            Left = engine.State.HasPosition ? engine.State.Left : area.Right - Width - 32;
            Top = engine.State.HasPosition ? engine.State.Top : area.Bottom - Height - 24;
            ClampPrimaryIfOffscreen();
            petImage.MouseLeftButtonDown += OnDown; petImage.MouseMove += OnMove; petImage.MouseLeftButtonUp += OnUp;
            petImage.LostMouseCapture += delegate { pointerDown = false; };
            petImage.MouseRightButtonUp += delegate { OpenMenu(); };
            SourceInitialized += delegate {
                HwndSource source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
                source.AddHook(HitHook);
                ClampCurrentScreen();
            };
            lastAnimationTick = lastTick = DateTime.UtcNow; nextRoam = lastTick.AddSeconds(15); nextSave = lastTick.AddSeconds(30);
            Play(engine.IsResting ? engine.State.Rest.Animation : "idle", true);
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
            timer.Tick += Tick; timer.Start();
            if (useTray) CreateTray();
            InitializeMiniGames(useTray);
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if (!e.Cancel) Shutdown(); };
        }
        private void CreateTray()
        {
            tray = new Forms.NotifyIcon { Text = "焰轮哪吒 · 右键打开养成面板", Visible = true,
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location) };
            Forms.ContextMenuStrip menu = new Forms.ContextMenuStrip();
            menu.Items.Add("打开养成面板", null, delegate { Dispatcher.Invoke(new Action(OpenDashboard)); });
            menu.Items.Add("小游戏 · 休闲小窗", null, delegate { Dispatcher.Invoke(new Action(OpenMiniGames)); });
            menu.Items.Add("检查更新", null, delegate { Dispatcher.Invoke(new Action(OpenUpdate)); });
            Forms.ToolStripMenuItem restItem = new Forms.ToolStripMenuItem(Engine.IsResting ? "叫醒哪吒" : "去睡一小会儿");
            restItem.Click += delegate { Dispatcher.Invoke(new Action(delegate { if (Engine.IsResting) WakeAction(); else StartRestAction(); })); };
            menu.Opening += delegate { restItem.Text = Engine.IsResting ? "叫醒哪吒" : "去睡一小会儿"; };
            menu.Items.Add(restItem);
            menu.Items.Add("显示 / 隐藏桌宠", null, delegate { Dispatcher.Invoke(new Action(ToggleVisible)); });
            menu.Items.Add("回到主屏幕", null, delegate { Dispatcher.Invoke(new Action(ResetPosition)); });
            menu.Items.Add("退出", null, delegate { Dispatcher.Invoke(new Action(Close)); });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { Dispatcher.Invoke(new Action(OpenDashboard)); };
        }
        private void OpenMenu()
        {
            ContextMenu menu = new ContextMenu { FontFamily = Ui.Font };
            AddMenu(menu, "养成面板 · 喂食 / 打工 / 零食铺", OpenDashboard);
            AddMenu(menu, "小游戏 · 休闲小窗", OpenMiniGames);
            AddMenu(menu, "检查更新 · v" + Engine.Config.Version, OpenUpdate);
            AddMenu(menu, Engine.IsResting ? "叫醒哪吒" : "去睡一小会儿", delegate { if (Engine.IsResting) WakeAction(); else StartRestAction(); });
            AddMenu(menu, "桌面游走：" + (Engine.State.Roaming ? "开" : "关"), delegate { Options(Engine.State.PetSize, !Engine.State.Roaming); });
            AddMenu(menu, "隐藏（托盘可恢复）", ToggleVisible);
            AddMenu(menu, "退出", Close);
            menu.PlacementTarget = petImage; menu.IsOpen = true;
        }
        private static void AddMenu(ContextMenu menu, string title, Action action)
        { MenuItem item = new MenuItem { Header = title }; item.Click += delegate { action(); }; menu.Items.Add(item); }
        public void OpenDashboard()
        {
            if (dashboard == null) { dashboard = new Dashboard(this); dashboard.Closed += delegate { dashboard = null; }; }
            if (!dashboard.IsVisible) dashboard.Show();
            dashboard.Activate();
        }
        public void OpenUpdate()
        {
            if (updateWindow == null)
            {
                updateWindow = new UpdateWindow(this, AppDomain.CurrentDomain.BaseDirectory);
                updateWindow.Closed += delegate { updateWindow = null; };
            }
            if (!updateWindow.IsVisible) updateWindow.Show();
            updateWindow.Activate();
        }
        public void PrepareForUpdate()
        {
            timer.Stop();
            try
            {
                Engine.Advance(DateTime.UtcNow);
                Engine.SetAppearance(Width, Engine.State.Roaming, Left, Top);
                Engine.Save();
                updateSaved = true;
            }
            catch { timer.Start(); throw; }
        }
        public void CancelPreparedUpdate() { updateSaved = false; if (!shuttingDown) timer.Start(); }
        private void ToggleVisible() { if (IsVisible) Hide(); else Show(); }
        private void SetSize(double size) { Width = size; Height = 58 + size * Engine.Config.FrameHeight / Engine.Config.FrameWidth; }
        public void Options(double size, bool roam)
        {
            double oldSize = Width;
            SetSize(size); ClampCurrentScreen();
            try { Engine.SetAppearance(Width, roam, Left, Top); if (!roam) moveDirection = 0; }
            catch (Exception ex) { SetSize(oldSize); Report(ex); }
        }
        public void ResetPosition()
        {
            Rect area = SystemParameters.WorkArea; Left = area.Right - Width - 32; Top = area.Bottom - Height - 24;
            PersistPosition(); Show();
        }
        private void OnDown(object sender, MouseButtonEventArgs e)
        {
            Point p = e.GetPosition(petImage);
            if (visibleFrame == null || !visibleFrame.Solid(p.X / petImage.ActualWidth, p.Y / petImage.ActualHeight)) return;
            pointerDown = true; dragged = false; moveDirection = 0;
            downScreen = PointToScreen(e.GetPosition(this)); downLeft = Left; downTop = Top;
            petImage.CaptureMouse(); e.Handled = true;
        }
        private void OnMove(object sender, MouseEventArgs e)
        {
            if (!pointerDown || e.LeftButton != MouseButtonState.Pressed) return;
            Point current = PointToScreen(e.GetPosition(this));
            Vector offset = current - downScreen;
            if (offset.Length > 6) dragged = true;
            if (dragged)
            {
                Matrix transform = PresentationSource.FromVisual(this).CompositionTarget.TransformFromDevice;
                Vector logical = transform.Transform(offset);
                Left = downLeft + logical.X; Top = downTop + logical.Y;
            }
        }
        private void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (!pointerDown) return;
            pointerDown = false; petImage.ReleaseMouseCapture();
            if (dragged) { ClampCurrentScreen(); PersistPosition(); }
            else
            {
                Point p = e.GetPosition(petImage);
                Interaction part = Engine.Config.Hit(p.X / petImage.ActualWidth, p.Y / petImage.ActualHeight);
                if (Engine.IsResting) TouchPart("head");
                else if (part != null) TouchPart(part.Id);
            }
            e.Handled = true;
        }
        private IntPtr HitHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message != 0x84 || pointerDown || visibleFrame == null) return IntPtr.Zero;
            long packed = lParam.ToInt64();
            Point screen = new Point((short)(packed & 0xffff), (short)((packed >> 16) & 0xffff));
            Point local = PointFromScreen(screen);
            if (bubble.Visibility == Visibility.Visible && local.Y < 58) return IntPtr.Zero;
            double x = local.X / Width, y = (local.Y - 58) / (Height - 58);
            if (!visibleFrame.Solid(x, y)) { handled = true; return new IntPtr(-1); }
            return IntPtr.Zero;
        }
        public ActionResult Execute(Func<ActionResult> action)
        {
            try { ActionResult result = action(); Feedback(result); if (dashboard != null) dashboard.Refresh(true); return result; }
            catch (Exception ex) { Report(ex); return new ActionResult(false, "这次操作没有保存，请检查目录写入权限。", "upset"); }
        }
        private ActionResult WaitForReaction()
        {
            string message = animation.StartsWith("eat", StringComparison.Ordinal) ? "哪吒还在细细品尝，等吃完再陪他玩吧。" : "哪吒正在回应你，稍等一小会儿。";
            ShowNotice(message); return new ActionResult(false, message, animation);
        }
        public ActionResult Feed(string foodId)
        {
            if (ReactionBusy) return WaitForReaction();
            return Execute(delegate { return Engine.Feed(foodId, DateTime.UtcNow); });
        }
        public ActionResult TouchPart(string partId)
        {
            if (Engine.IsResting)
            {
                string message = "嘘，哪吒正在睡觉。想陪他玩，可以在小屋里叫醒他。";
                ShowNotice(message); return new ActionResult(false, message, Engine.State.Rest.Animation);
            }
            if (ReactionBusy) return WaitForReaction();
            ActionResult result = Execute(delegate { return Engine.Touch(partId, DateTime.UtcNow); });
            if (result.Ok) effects.Trigger(partId);
            return result;
        }
        public ActionResult StartWork(string jobId)
        {
            if (ReactionBusy) return WaitForReaction();
            return Execute(delegate { return Engine.StartWork(jobId, DateTime.UtcNow); });
        }
        public ActionResult StartRestAction()
        {
            if (ReactionBusy) return WaitForReaction();
            return Execute(delegate { return Engine.StartRest(DateTime.UtcNow); });
        }
        public ActionResult WakeAction() { return Execute(delegate { return Engine.Wake(DateTime.UtcNow); }); }
        public void ShowNotice(string message)
        {
            LastNotice = message; bubbleText.Text = message;
            bubble.Visibility = Visibility.Visible; bubbleUntil = DateTime.UtcNow.AddSeconds(4);
        }
        public void Feedback(ActionResult result)
        {
            ShowNotice(result.Message + (String.IsNullOrEmpty(result.GrowthMessage) ? "" : "  " + result.GrowthMessage));
            if (!String.IsNullOrEmpty(result.GrowthMessage)) bubbleText.Text = result.GrowthMessage;
            // Shopping and denied actions may show a message while another clip owns the pet.
            if (ReactionBusy) return;
            if (Engine.IsResting) { moveDirection = 0; Play(Engine.State.Rest.Animation, false); return; }
            Play(result.Animation, true);
            AnimationSpec spec = Engine.Config.Animations[animation];
            double cycle = (double)spec.FrameMs * Sprites.Animations[animation].Length;
            animationPlayer.ProtectReaction(Math.Max(cycle, spec.DurationMs > 0 ? spec.DurationMs : 2800));
            moveDirection = 0;
        }
        private void Play(string name, bool restart)
        {
            if (!Sprites.Animations.ContainsKey(name)) name = "idle";
            if (animation == name && !restart) return;
            animation = name; lastAnimationTick = DateTime.UtcNow; lastFrame = -1;
            AnimationSpec spec = Engine.Config.Animations[animation];
            animationPlayer.Start(spec.FrameMs, Sprites.Animations[animation].Length, spec.Loop != false);
            DisplayFrame(0);
        }
        private void DisplayFrame(int index)
        {
            if (index == lastFrame) return;
            visibleFrame = Sprites.Animations[animation][index]; petImage.Source = visibleFrame.Image;
            lastFrame = index; FramesPainted++;
        }
        private void Tick(object sender, EventArgs args)
        {
            DateTime now = DateTime.UtcNow;
            double elapsed = Math.Max(0, Math.Min(0.15, (now - lastTick).TotalSeconds)); lastTick = now;
            // UI stalls slow the clip briefly instead of dropping meaningful intermediate poses.
            animationPlayer.Advance((now - lastAnimationTick).TotalMilliseconds);
            effects.Advance(elapsed * 1000);
            lastAnimationTick = now;
            Engine.Advance(now);
            if (now >= bubbleUntil) bubble.Visibility = Visibility.Hidden;
            if (!animationPlayer.ReactionActive && !pointerDown)
            {
                animationPlayer.ClearReaction();
                if (Engine.IsResting)
                {
                    moveDirection = 0;
                    Play(Engine.State.Rest.Animation, false);
                }
                else if (Engine.State.Work != null)
                {
                    moveDirection = 0;
                    Play(Engine.WorkReady(now) ? "wave" : Engine.State.Work.Animation, false);
                }
                else if (Engine.State.Roaming && IsVisible)
                {
                    if (now >= nextRoam) { moveDirection = random.Next(2) == 0 ? -1 : 1; moveUntil = now.AddSeconds(2.5); nextRoam = now.AddSeconds(random.Next(15, 30)); }
                    if (now >= moveUntil) moveDirection = 0;
                    Play(moveDirection > 0 ? "runRight" : moveDirection < 0 ? "runLeft" : "idle", false);
                    if (moveDirection != 0)
                    {
                        double before = Left; Left += moveDirection * 40 * elapsed;
                        ClampCurrentScreen(); if (Math.Abs(Left - before) < 0.001) moveDirection *= -1;
                    }
                }
                else { moveDirection = 0; Play("idle", false); }
            }
            if (IsVisible || dashboard == null) DisplayFrame(animationPlayer.FrameIndex);
            if (dashboard != null && now >= nextPanelRefresh) { nextPanelRefresh = now.AddMilliseconds(500); dashboard.Refresh(false); }
            if (now >= nextSave)
            {
                nextSave = now.AddSeconds(30);
                try { Engine.SetAppearance(Width, Engine.State.Roaming, Left, Top); reportedSaveFailure = false; }
                catch (Exception ex) { if (!reportedSaveFailure) { reportedSaveFailure = true; Report(ex); } }
            }
        }
        private void PersistPosition()
        { try { Engine.SetAppearance(Width, Engine.State.Roaming, Left, Top); } catch (Exception ex) { Report(ex); } }
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
        public string ProbeNativeHitTest()
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) throw new InvalidOperationException("没有创建原生窗口。");
            Point transparent = PointToScreen(new Point(1, 59));
            int tx = (int)Math.Round(transparent.X), ty = (int)Math.Round(transparent.Y);
            int transparentHit = SendMessage(handle, 0x84, IntPtr.Zero, new IntPtr((ty << 16) | (tx & 0xffff))).ToInt32();
            int solidX = -1, solidY = -1;
            for (int y = 0; y < visibleFrame.Image.PixelHeight && solidX < 0; y++)
                for (int x = 0; x < visibleFrame.Image.PixelWidth; x++)
                    if (visibleFrame.Pixels[(y * visibleFrame.Image.PixelWidth + x) * 4 + 3] > 250) { solidX = x; solidY = y; break; }
            Point solid = PointToScreen(new Point((solidX + 0.5) * Width / visibleFrame.Image.PixelWidth, 58 + (solidY + 0.5) * (Height - 58) / visibleFrame.Image.PixelHeight));
            int sx = (int)Math.Round(solid.X), sy = (int)Math.Round(solid.Y);
            int solidHit = SendMessage(handle, 0x84, IntPtr.Zero, new IntPtr((sy << 16) | (sx & 0xffff))).ToInt32();
            if (transparentHit != -1 || solidHit == -1) throw new InvalidOperationException("原生像素命中检查失败：transparent=" + transparentHit + ", solid=" + solidHit);
            return "transparentHit=" + transparentHit + "\nsolidHit=" + solidHit;
        }
        private void ClampPrimaryIfOffscreen()
        {
            Rect virtualArea = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            if (!virtualArea.IntersectsWith(new Rect(Left, Top, Width, Height)))
            { Rect area = SystemParameters.WorkArea; Left = area.Right - Width - 32; Top = area.Bottom - Height - 24; }
        }
        private void ClampCurrentScreen()
        {
            PresentationSource source = PresentationSource.FromVisual(this);
            if (source == null) return;
            Forms.Screen screen = Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
            Matrix transform = source.CompositionTarget.TransformFromDevice;
            Point start = transform.Transform(new Point(screen.WorkingArea.Left, screen.WorkingArea.Top));
            Point end = transform.Transform(new Point(screen.WorkingArea.Right, screen.WorkingArea.Bottom));
            Left = Math.Max(start.X, Math.Min(end.X - Width, Left));
            Top = Math.Max(start.Y, Math.Min(end.Y - Height, Top));
        }
        private void Report(Exception ex)
        { MessageBox.Show("操作未完成：" + ex.Message + "\n请把完整程序目录放在可写的位置，例如‘文档’。", "焰轮哪吒", MessageBoxButton.OK, MessageBoxImage.Warning); }
        public void Shutdown()
        {
            if (shuttingDown) return; shuttingDown = true; timer.Stop();
            if (dashboard != null) dashboard.Close();
            if (updateWindow != null) updateWindow.Close();
            try { if (!updateSaved) { Engine.Advance(DateTime.UtcNow); Engine.SetAppearance(Width, Engine.State.Roaming, Left, Top); } }
            catch (Exception ex) { Report(ex); }
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
        }
    }
    public static class Ui
    {
        public static readonly FontFamily Font = new FontFamily("Microsoft YaHei UI");
        public static readonly Brush Cream = Color("#FFF9EE"), Red = Color("#A9382E"), Gold = Color("#C49B58"), Ink = Color("#392E29"), Muted = Color("#85776A"), Line = Color("#EADCC8");
        public static Brush Color(string hex) { return (SolidColorBrush)new BrushConverter().ConvertFromString(hex); }
        public static TextBlock Text(string text, double size, Brush color)
        { return new TextBlock { Text = text, FontSize = size, Foreground = color, FontFamily = Font, TextWrapping = TextWrapping.Wrap }; }
        public static Button Button(string text, Action action, bool primary)
        {
            Button button = new Button { Content = text, FontFamily = Font, FontSize = 13, Background = primary ? Red : Cream,
                Foreground = primary ? Brushes.White : Ink, BorderBrush = primary ? Red : Line, BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 8, 14, 8), Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left };
            ControlTemplate template = new ControlTemplate(typeof(Button));
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            FrameworkElementFactory content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content); template.VisualTree = border;
            Trigger disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.4)); template.Triggers.Add(disabled);
            Trigger hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.82)); template.Triggers.Add(hover);
            button.Template = template; button.Click += delegate { action(); }; return button;
        }
        public static Border Card(UIElement child)
        { return new Border { Background = Brushes.White, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 12), Child = child }; }
    }
    public class Dashboard : Window
    {
        private readonly PetWindow pet;
        private readonly GameEngine engine;
        private readonly TextBlock coins, level, satietyText, energyText, moodText, workStatus, footer;
        private readonly ProgressBar satiety, energy, mood;
        private readonly StackPanel page;
        private readonly Dictionary<string, Button> navigation = new Dictionary<string, Button>();
        private readonly Dictionary<Button, Func<bool>> conditions = new Dictionary<Button, Func<bool>>();
        private string selected = "home";
        private Button collect;
        private TextBlock restStatus;
        private ProgressBar restProgress;
        private Button restAction;
        public Border PreviewRoot { get; private set; }
        public Dashboard(PetWindow window)
        {
            pet = window; engine = pet.Engine; Title = "焰轮哪吒 · 养成小屋";
            Width = 610; Height = 720; MinWidth = 560; MinHeight = 620;
            FontFamily = Ui.Font; Background = Ui.Cream; WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Grid layout = new Grid { Margin = new Thickness(24, 20, 24, 18) };
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel heading = new StackPanel(); heading.Children.Add(Ui.Text("焰轮哪吒", 25, Ui.Red));
            TextBlock subtitle = Ui.Text("神环相伴，烟火日常", 12, Ui.Muted); subtitle.Margin = new Thickness(0, 4, 0, 16); heading.Children.Add(subtitle); header.Children.Add(heading);
            StackPanel balance = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
            coins = Ui.Text("", 20, Ui.Ink); level = Ui.Text("", 12, Ui.Muted); level.Margin = new Thickness(0, 5, 0, 0);
            balance.Children.Add(coins); balance.Children.Add(level); Grid.SetColumn(balance, 1); header.Children.Add(balance); layout.Children.Add(header);
            Grid stats = new Grid(); stats.ColumnDefinitions.Add(new ColumnDefinition()); stats.ColumnDefinitions.Add(new ColumnDefinition()); stats.ColumnDefinitions.Add(new ColumnDefinition());
            satietyText = Ui.Text("", 12, Ui.Ink); energyText = Ui.Text("", 12, Ui.Ink); moodText = Ui.Text("", 12, Ui.Ink);
            satiety = Stat(stats, 0, satietyText, "#DCAD57"); energy = Stat(stats, 1, energyText, "#7F9A73"); mood = Stat(stats, 2, moodText, "#D57D67");
            Grid.SetRow(stats, 1); layout.Children.Add(stats);
            UniformGrid nav = new UniformGrid { Columns = 5, Margin = new Thickness(0, 18, 0, 18) };
            AddNav(nav, "home", "小屋"); AddNav(nav, "bag", "背包"); AddNav(nav, "work", "打工"); AddNav(nav, "shop", "零食铺"); AddNav(nav, "growth", "成长");
            Grid.SetRow(nav, 2); layout.Children.Add(nav);
            page = new StackPanel(); ScrollViewer scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = page };
            Grid.SetRow(scroll, 3); layout.Children.Add(scroll);
            footer = Ui.Text("右键桌宠打开菜单  ·  关闭小屋后，哪吒会继续陪你", 11, Ui.Muted);
            footer.Margin = new Thickness(0, 14, 0, 0); Grid.SetRow(footer, 4); layout.Children.Add(footer);
            workStatus = Ui.Text("", 14, Ui.Red);
            PreviewRoot = new Border { Background = Ui.Cream, Child = layout }; Content = PreviewRoot;
            Refresh(true);
        }
        private static ProgressBar Stat(Grid parent, int column, TextBlock label, string color)
        {
            StackPanel panel = new StackPanel { Margin = new Thickness(column == 0 ? 0 : 8, 0, column == 2 ? 0 : 8, 0) };
            panel.Children.Add(label);
            ProgressBar bar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 7, Foreground = Ui.Color(color), Background = Ui.Line, BorderThickness = new Thickness(0), Margin = new Thickness(0, 8, 0, 0) };
            panel.Children.Add(bar); Grid.SetColumn(panel, column); parent.Children.Add(panel); return bar;
        }
        private void AddNav(Panel nav, string id, string text)
        {
            Button button = Ui.Button(text, delegate { SelectPage(id); }, false);
            button.HorizontalAlignment = HorizontalAlignment.Stretch; button.Margin = new Thickness(0, 0, 6, 0);
            navigation[id] = button; nav.Children.Add(button);
        }
        public void SelectPage(string id) { selected = id; Refresh(true); }
        public void Refresh(bool rebuild)
        {
            SaveData state = engine.State;
            coins.Text = state.Coins + " 金币";
            level.Text = "Lv." + engine.Level + " " + engine.CurrentLevel.Title + (engine.NextLevel == null ? " · 成长路线已完成" : "\n" + engine.LevelProgress + " / " + engine.LevelGoal + " 经验");
            if (!String.IsNullOrEmpty(pet.LastNotice)) footer.Text = pet.LastNotice;
            SetStat(satiety, satietyText, "饱腹", state.Satiety); SetStat(energy, energyText, "体力", state.Energy); SetStat(mood, moodText, "心情", state.Mood);
            DateTime now = DateTime.UtcNow;
            workStatus.Text = state.Work == null ? "今天想做点什么？" : engine.WorkReady(now) ? state.Work.Name + "已完成，快来领取报酬。" : state.Work.Name + "进行中 · 剩余 " + FormatTime(engine.RemainingSeconds(now));
            if (collect != null) collect.IsEnabled = engine.WorkReady(now) && !pet.ReactionBusy;
            UpdateRest(now);
            foreach (KeyValuePair<Button, Func<bool>> condition in conditions) condition.Key.IsEnabled = condition.Value();
            if (!rebuild) return;
            foreach (KeyValuePair<string, Button> n in navigation) { n.Value.Background = n.Key == selected ? Ui.Red : Ui.Cream; n.Value.Foreground = n.Key == selected ? Brushes.White : Ui.Ink; }
            page.Children.Clear(); conditions.Clear(); collect = null; restStatus = null; restProgress = null; restAction = null;
            if (selected == "shop") Shop(); else if (selected == "bag") Bag(); else if (selected == "work") Work(); else if (selected == "growth") Growth(); else Home();
        }
        private static void SetStat(ProgressBar bar, TextBlock text, string label, double value)
        { bar.Value = value; text.Text = label + "  " + Math.Round(value) + " / 100"; }
        private void Home()
        {
            Grid hero = new Grid(); hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) }); hero.ColumnDefinitions.Add(new ColumnDefinition());
            SpriteFrame[] portrait = pet.Sprites.Animations[engine.IsResting ? engine.State.Rest.Animation : "idle"];
            Image image = new Image { Source = portrait[Math.Min(2, portrait.Length - 1)].Image, Width = 120, Height = 130, Stretch = Stretch.Uniform };
            hero.Children.Add(image);
            StackPanel welcome = new StackPanel { Margin = new Thickness(14, 16, 0, 0) }; welcome.Children.Add(Ui.Text(engine.IsResting ? "睡个小觉，补充元气" : "今天也一起元气满满", 18, Ui.Ink));
            TextBlock status = Ui.Text(engine.IsResting ? "哪吒正在安静休息。醒来以后，再吃点心、陪你一起玩。" : "摸摸头、戳戳身体，或点一下风火轮，看看哪吒的回应。", 13, Ui.Muted); status.Margin = new Thickness(0, 9, 0, 12); welcome.Children.Add(status);
            welcome.Children.Add(Ui.Button(engine.IsResting ? "去背包看看" : "去背包喂一口", delegate { SelectPage("bag"); }, true)); Grid.SetColumn(welcome, 1); hero.Children.Add(welcome); page.Children.Add(Ui.Card(hero));
            RestCard();
            StackPanel goal = new StackPanel(); goal.Children.Add(Ui.Text("下一站的惊喜", 16, Ui.Ink));
            string next = engine.NextLevel == null ? "已经成为" + engine.CurrentLevel.Title + "，当前成长路线已完成。" : "再积累 " + (engine.NextLevel.Experience - engine.State.Experience) + " 经验 → Lv." + engine.NextLevel.Level + " " + engine.NextLevel.Title + "\n" + engine.NextLevel.Benefit;
            TextBlock nextText = Ui.Text(next, 12, Ui.Muted); nextText.Margin = new Thickness(0, 8, 0, 12); goal.Children.Add(nextText);
            goal.Children.Add(Ui.Button("看看成长路线", delegate { SelectPage("growth"); }, false)); page.Children.Add(Ui.Card(goal));
            StackPanel routine = new StackPanel(); routine.Children.Add(Ui.Text("烟火日常", 16, Ui.Ink));
            TextBlock flow = Ui.Text("打工赚金币 → 零食铺买点心 → 背包里喂食", 13, Ui.Muted); flow.Margin = new Thickness(0, 8, 0, 8); routine.Children.Add(flow);
            TextBlock tips = Ui.Text("闲下来会慢慢恢复体力，也可以睡一小会儿补充元气。久未打开也只按最多一小时计算日常状态变化，回来依然能轻松照顾。", 12, Ui.Muted); routine.Children.Add(tips); page.Children.Add(Ui.Card(routine));
            StackPanel settings = new StackPanel(); settings.Children.Add(Ui.Text("陪伴方式", 16, Ui.Ink));
            CheckBox roam = new CheckBox { Content = "偶尔在桌面上走一走", IsChecked = engine.State.Roaming, Foreground = Ui.Ink, Margin = new Thickness(0, 12, 0, 14) };
            roam.Checked += delegate { pet.Options(engine.State.PetSize, true); }; roam.Unchecked += delegate { pet.Options(engine.State.PetSize, false); }; settings.Children.Add(roam);
            settings.Children.Add(Ui.Text("桌宠大小", 12, Ui.Muted));
            Slider size = new Slider { Minimum = 180, Maximum = 360, TickFrequency = 30, IsSnapToTickEnabled = true, Value = engine.State.PetSize, Margin = new Thickness(0, 9, 0, 2) };
            size.ValueChanged += delegate { pet.Options(size.Value, engine.State.Roaming); }; settings.Children.Add(size); page.Children.Add(Ui.Card(settings));
            StackPanel updates = new StackPanel(); updates.Children.Add(Ui.Text("版本更新", 16, Ui.Ink));
            TextBlock version = Ui.Text("当前版本 v" + engine.Config.Version + " · 更新会保留金币、等级和背包", 12, Ui.Muted);
            version.Margin = new Thickness(0, 8, 0, 12); updates.Children.Add(version);
            updates.Children.Add(Ui.Button("检查更新", pet.OpenUpdate, false)); page.Children.Add(Ui.Card(updates));
        }
        private void RestCard()
        {
            StackPanel rest = new StackPanel(); rest.Children.Add(Ui.Text("午睡补元气", 16, Ui.Ink));
            restStatus = Ui.Text("", 12, Ui.Muted); restStatus.Margin = new Thickness(0, 8, 0, 10); rest.Children.Add(restStatus);
            restProgress = new ProgressBar { Minimum = 0, Maximum = 1, Height = 7, Foreground = Ui.Color("#9D91B0"), Background = Ui.Line, BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 0, 12) };
            rest.Children.Add(restProgress);
            restAction = Ui.Button("", delegate { if (engine.IsResting) pet.WakeAction(); else pet.StartRestAction(); }, true);
            rest.Children.Add(restAction); UpdateRest(DateTime.UtcNow); page.Children.Add(Ui.Card(rest));
        }
        private void UpdateRest(DateTime now)
        {
            if (restStatus == null) return;
            if (engine.IsResting)
            {
                int remaining = engine.RestRemainingSeconds(now);
                restStatus.Text = remaining > 0 ? "哪吒正睡得香 · 剩余 " + FormatTime(remaining) + "\n提前叫醒也会按已睡的时间补充元气。" : "已经睡饱了，叫醒哪吒一起玩吧。";
                restProgress.Visibility = Visibility.Visible; restProgress.Value = engine.RestProgress(now);
                restAction.Content = remaining > 0 ? "提前叫醒" : "叫醒哪吒"; restAction.IsEnabled = true;
            }
            else
            {
                restStatus.Text = "睡 " + FormatTime(engine.Config.Rest.Seconds) + "，体力 +" + engine.Config.Rest.EnergyReward + "、心情 +" + engine.Config.Rest.MoodReward + "。\n睡觉时不打工、不喂食，醒来再继续。";
                restProgress.Visibility = Visibility.Collapsed; restProgress.Value = 0;
                restAction.Content = "去睡一小会儿"; restAction.IsEnabled = engine.State.Work == null && !pet.ReactionBusy && engine.State.Energy < 99.99;
            }
        }
        private void Shop()
        {
            Intro("陈塘关零食铺", "买到的点心会放进背包，随时拿出来喂食。");
            foreach (Food food in engine.Config.Foods)
            {
                Food chosen = food;
                StackPanel text = new StackPanel(); text.Children.Add(Ui.Text(food.Name, 17, Ui.Ink));
                TextBlock description = Ui.Text(food.Description, 12, Ui.Muted); description.Margin = new Thickness(0, 5, 0, 8); text.Children.Add(description);
                text.Children.Add(Ui.Text(FoodEffect(food) + "\n喂食经验 +" + food.Experience, 12, Ui.Muted));
                Button buy = Ui.Button(engine.Level < food.MinimumLevel ? "Lv." + food.MinimumLevel + " 解锁" : food.Price + " 金币 · 买一份", delegate { pet.Execute(delegate { return engine.Buy(chosen.Id, DateTime.UtcNow); }); }, true);
                EnableWhen(buy, delegate { return engine.Level >= chosen.MinimumLevel && engine.State.Coins >= chosen.Price && engine.State.Inventory[chosen.Id] < 999; });
                page.Children.Add(FoodCard(food, text, buy));
            }
        }
        private void Bag()
        {
            Intro("我的小背包", "把喜欢的点心喂给哪吒，补充元气。");
            foreach (Food food in engine.Config.Foods)
            {
                Food chosen = food; int count = engine.State.Inventory[food.Id];
                StackPanel text = new StackPanel(); text.Children.Add(Ui.Text(food.Name + "  × " + count, 17, Ui.Ink));
                TextBlock effect = Ui.Text(FoodEffect(food), 12, Ui.Muted); effect.Margin = new Thickness(0, 8, 0, 0); text.Children.Add(effect);
                Button feed = Ui.Button("喂一份", delegate { pet.Feed(chosen.Id); }, true);
                EnableWhen(feed, delegate { return engine.State.Inventory[chosen.Id] > 0 && pet.CanFeed && engine.State.Satiety < 99.99; });
                page.Children.Add(FoodCard(food, text, feed));
            }
            page.Children.Add(Ui.Button("去零食铺补货", delegate { SelectPage("shop"); }, false));
        }
        private void Work()
        {
            Intro("今日打工", "一次接一份工作，完成后领取金币。关闭程序也会继续计时。");
            StackPanel active = new StackPanel(); active.Children.Add(workStatus);
            collect = Ui.Button("领取打工报酬", delegate { if (!pet.ReactionBusy) pet.Execute(delegate { return engine.Collect(DateTime.UtcNow); }); }, true);
            collect.Margin = new Thickness(0, 12, 0, 0); collect.IsEnabled = engine.WorkReady(DateTime.UtcNow) && !pet.ReactionBusy; active.Children.Add(collect); page.Children.Add(Ui.Card(active));
            foreach (Job job in engine.Config.Jobs)
            {
                Job chosen = job; StackPanel text = new StackPanel(); text.Children.Add(Ui.Text(job.Name, 16, Ui.Ink));
                TextBlock reward = Ui.Text(FormatTime(job.Seconds) + " · 收入 " + engine.WorkReward(job) + " 金币 · 经验 +" + job.Experience, 12, Ui.Muted);
                reward.Margin = new Thickness(0, 7, 0, 5); text.Children.Add(reward);
                text.Children.Add(Ui.Text("消耗：体力 " + job.EnergyCost + "，饱腹 " + job.SatietyCost, 12, Ui.Muted));
                Button start = Ui.Button(engine.Level < job.MinimumLevel ? "Lv." + job.MinimumLevel + " 解锁" : "出发", delegate { pet.StartWork(chosen.Id); }, true);
                EnableWhen(start, delegate { return engine.Level >= chosen.MinimumLevel && engine.State.Work == null && !engine.IsResting && !pet.ReactionBusy && engine.State.Energy >= chosen.EnergyCost && engine.State.Satiety >= chosen.SatietyCost + 10; });
                page.Children.Add(RowCard(text, start));
            }
        }
        private void Intro(string title, string description)
        {
            page.Children.Add(Ui.Text(title, 19, Ui.Ink)); TextBlock text = Ui.Text(description, 12, Ui.Muted);
            text.Margin = new Thickness(0, 7, 0, 16); page.Children.Add(text);
        }
        private void Growth()
        {
            Intro("成长之路", "每一次照顾、每一份努力，都让小小哪吒更有力量。");
            StackPanel summary = new StackPanel(); summary.Children.Add(Ui.Text("Lv." + engine.Level + " · " + engine.CurrentLevel.Title, 20, Ui.Red));
            if (engine.NextLevel != null)
            {
                ProgressBar progress = new ProgressBar { Minimum = 0, Maximum = engine.LevelGoal, Value = engine.LevelProgress, Height = 10, Foreground = Ui.Gold, Background = Ui.Line, BorderThickness = new Thickness(0), Margin = new Thickness(0, 14, 0, 9) };
                summary.Children.Add(progress);
                summary.Children.Add(Ui.Text(engine.LevelProgress + " / " + engine.LevelGoal + " 本级经验 · 再 " + (engine.NextLevel.Experience - engine.State.Experience) + " 经验升级", 12, Ui.Muted));
            }
            else summary.Children.Add(Ui.Text("已完成当前成长路线，总经验 " + engine.State.Experience, 12, Ui.Muted));
            TextBlock method = Ui.Text("喂食、完成打工和部位互动获得经验；购物本身不加经验。\n每日喂食经验 " + engine.State.FeedExperienceToday + " / " + engine.Config.Growth.FeedDailyCap + "，互动经验 " + engine.State.TouchExperienceToday + " / " + engine.Config.Growth.TouchDailyCap + "。达到上限后仍然可以照顾哪吒。", 12, Ui.Muted);
            method.Margin = new Thickness(0, 12, 0, 0); summary.Children.Add(method); page.Children.Add(Ui.Card(summary));
            foreach (LevelSpec step in engine.Config.Growth.Levels)
            {
                StackPanel info = new StackPanel();
                string tag = step.Level == engine.Level ? " · 当前" : step.Level < engine.Level ? " · 已达成" : "";
                info.Children.Add(Ui.Text("Lv." + step.Level + " " + step.Title + tag, 16, step.Level <= engine.Level ? Ui.Red : Ui.Ink));
                TextBlock benefit = Ui.Text(step.Benefit, 12, Ui.Muted); benefit.Margin = new Thickness(0, 7, 0, 7); info.Children.Add(benefit);
                info.Children.Add(Ui.Text("累计经验 " + step.Experience + (step.GiftCoins > 0 ? " · 成长礼 " + step.GiftCoins + " 金币" + (engine.State.RewardedLevels.Contains(step.Level) ? "（已领取）" : "") : ""), 11, Ui.Muted));
                page.Children.Add(Ui.Card(info));
            }
        }
        private Border FoodCard(Food food, StackPanel text, Button button)
        {
            Grid card = new Grid(); card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) }); card.ColumnDefinitions.Add(new ColumnDefinition()); card.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            SpriteFrame[] frames = pet.Sprites.Animations[food.Animation];
            Image thumbnail = new Image { Source = frames[Math.Min(2, frames.Length - 1)].Image, Width = 66, Height = 76, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 8, 0) };
            card.Children.Add(thumbnail); text.Margin = new Thickness(0, 0, 10, 0); Grid.SetColumn(text, 1); card.Children.Add(text);
            button.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(button, 2); card.Children.Add(button); return Ui.Card(card);
        }
        private void EnableWhen(Button button, Func<bool> condition) { conditions[button] = condition; button.IsEnabled = condition(); }
        private static Border RowCard(StackPanel content, Button button)
        {
            Grid row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.Margin = new Thickness(0, 0, 14, 0); row.Children.Add(content);
            button.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(button, 1); row.Children.Add(button); return Ui.Card(row);
        }
        private static string FoodEffect(Food food)
        { return "饱腹 +" + food.Satiety + "   体力 +" + food.Energy + "   心情 +" + food.Mood; }
        private static string FormatTime(int seconds)
        { return seconds >= 60 ? (seconds / 60) + "分" + (seconds % 60 > 0 ? (seconds % 60) + "秒" : "钟") : seconds + "秒"; }
    }
}
