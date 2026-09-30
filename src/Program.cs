using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace NezhaDesktop
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length != 0) return Run(args);
            using (Mutex mutex = new Mutex(false, UpdateSafety.InstallMutexName(AppDomain.CurrentDomain.BaseDirectory)))
            {
                bool held;
                try { held = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { held = true; }
                if (!held) { MessageBox.Show("这个目录下的哪吒已在运行或正在更新。请稍候，从托盘打开养成面板。", "焰轮哪吒"); return 0; }
                try { return Run(args); }
                finally { mutex.ReleaseMutex(); }
            }
        }
        private static int Run(string[] args)
        {
            string root = AppDomain.CurrentDomain.BaseDirectory;
            string reportDirectory = null;
            try
            {
                if (args.Length > 0)
                {
                    string command = args[0];
                    bool reports = command == "--self-test" || command == "--preview" || command == "--native-smoke" || command == "--food-preview" || command == "--update-test" || command == "--update-online-test";
                    if (reports && args.Length == 2) reportDirectory = Path.GetFullPath(args[1]);
                    else if (command != "--update-health" || args.Length != 2) throw new ArgumentException("启动参数无效。");
                }
                PetConfig config = PetConfig.Load(root);
                if (args.Length > 0 && args[0] == "--update-online-test")
                {
                    Directory.CreateDirectory(reportDirectory);
                    UpdateClient client = new UpdateClient(root);
                    UpdateCheck check = client.Check("0.2.0");
                    if (!check.Available) throw new InvalidDataException("线上发布版本没有高于 0.2.0。");
                    PreparedUpdate prepared = client.Download(check, null);
                    File.WriteAllText(Path.Combine(reportDirectory, "online-update.txt"), "PASS anonymous-public-check-and-download\nversion=" + check.LatestVersion + "\nsha256=" + check.Manifest.Sha256 + "\nsize=" + check.Manifest.Size);
                    UpdateSafety.DeleteStage(prepared.StageDirectory, root);
                    return 0;
                }
                if (args.Length > 0 && args[0] == "--update-test")
                {
                    if (reportDirectory == null) throw new ArgumentException("需要指定新的更新测试目录。");
                    return UpdateTests.Run(root, reportDirectory);
                }
                if (args.Length > 0 && args[0] == "--self-test")
                {
                    if (reportDirectory == null) throw new ArgumentException("需要指定新的测试目录。");
                    return Tests.Run(config, reportDirectory);
                }
                Application app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e) {
                    MessageBox.Show("桌宠遇到了问题：" + e.Exception.Message + "\n进度保存在 data 目录，请保留这个目录。", "焰轮哪吒");
                    e.Handled = true; app.Shutdown(1);
                };
                SpriteLibrary sprites = new SpriteLibrary(config, root);
                if (args.Length > 0 && args[0] == "--update-health")
                {
                    string marker = Path.GetFullPath(args[1]);
                    string stageRoot = Path.Combine(Path.GetFullPath(root), ".update") + Path.DirectorySeparatorChar;
                    if (!marker.StartsWith(stageRoot, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("更新验证文件必须位于本程序的暂存目录。");
                    new SaveStore(Path.Combine(root, "data", "save.json")).Load(config, DateTime.UtcNow);
                    File.WriteAllText(marker, config.Version); return 0;
                }
                if (args.Length > 0 && args[0] == "--preview")
                {
                    if (reportDirectory == null) throw new ArgumentException("需要指定预览输出目录。");
                    return Preview(app, config, sprites, reportDirectory);
                }
                if (args.Length > 0 && args[0] == "--native-smoke")
                {
                    if (reportDirectory == null) throw new ArgumentException("需要指定原生窗口验证目录。");
                    return NativeSmoke(app, config, sprites, reportDirectory);
                }
                if (args.Length > 0 && args[0] == "--food-preview")
                {
                    if (reportDirectory == null) throw new ArgumentException("需要指定食物动画验证目录。");
                    return FoodPreview(app, config, sprites, reportDirectory);
                }
                    SaveStore store = new SaveStore(Path.Combine(root, "data", "save.json"));
                    GameEngine engine = new GameEngine(config, store, DateTime.UtcNow);
                    PetWindow pet = new PetWindow(engine, sprites, true);
                    pet.Closed += delegate { app.Shutdown(); };
                    app.MainWindow = pet; pet.Show();
                    pet.Feedback(new ActionResult(true, String.IsNullOrEmpty(engine.InitialGrowthMessage) ? "右键打开养成小屋，陪我一起玩吧！" : engine.InitialGrowthMessage, "wave"));
                    if (store.RecoveredDuringLoad) MessageBox.Show("已从备份恢复进度，最后一次操作可能需要重新完成。", "焰轮哪吒");
                    return app.Run();
            }
            catch (Exception ex)
            {
                string detail = ex.ToString();
                if (reportDirectory != null) { Directory.CreateDirectory(reportDirectory); File.WriteAllText(Path.Combine(reportDirectory, "error.txt"), detail); }
                else if (args.Length == 0 || args[0] != "--update-health") MessageBox.Show("无法启动：" + ex.Message + "\n请先完整解压程序，并放到可写的目录，例如‘文档’。", "焰轮哪吒", MessageBoxButton.OK, MessageBoxImage.Error);
                Console.Error.WriteLine(detail); return 1;
            }
        }
        private static int Preview(Application app, PetConfig config, SpriteLibrary sprites, string output)
        {
            Directory.CreateDirectory(output);
            GameEngine engine = new GameEngine(config, new SaveStore(Path.Combine(output, "preview-save.json")), DateTime.UtcNow);
            PetWindow pet = new PetWindow(engine, sprites, false);
            Dashboard panel = new Dashboard(pet);
            foreach (System.Collections.Generic.KeyValuePair<string, SpriteFrame[]> animation in sprites.Animations)
                foreach (SpriteFrame frame in animation.Value)
                {
                    bool visible = false;
                    for (int i = 3; i < frame.Pixels.Length; i += 4) if (frame.Pixels[i] > 22) { visible = true; break; }
                    if (!visible) throw new InvalidDataException("动作中存在空帧：" + animation.Key);
                }
            Render(panel.PreviewRoot, 610, 690, Path.Combine(output, "desktop-home.png"));
            UpdateWindow updatePreview = new UpdateWindow(pet, AppDomain.CurrentDomain.BaseDirectory);
            Render((FrameworkElement)updatePreview.Content, 430, 270, Path.Combine(output, "desktop-update.png")); updatePreview.Close();
            panel.SelectPage("shop"); Render(panel.PreviewRoot, 610, 690, Path.Combine(output, "desktop-shop.png"));
            panel.SelectPage("growth"); Render(panel.PreviewRoot, 610, 690, Path.Combine(output, "desktop-growth.png"));
            pet.Execute(delegate { return engine.Buy(config.Foods[0].Id, DateTime.UtcNow); });
            pet.Execute(delegate { return engine.Feed(config.Foods[0].Id, DateTime.UtcNow); });
            panel.SelectPage("bag"); Render(panel.PreviewRoot, 610, 690, Path.Combine(output, "desktop-bag.png"));
            pet.Execute(delegate { return engine.StartWork(config.Jobs[0].Id, DateTime.UtcNow); });
            panel.SelectPage("work"); Render(panel.PreviewRoot, 610, 690, Path.Combine(output, "desktop-work.png"));
            pet.Execute(delegate { return engine.Touch("head", DateTime.UtcNow); });
            Render(pet.Surface, (int)pet.Width, (int)pet.Height, Path.Combine(output, "desktop-pet.png"));
            DispatcherTimer finish = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.3) };
            finish.Tick += delegate {
                finish.Stop();
                bool pass = pet.FramesPainted >= 6;
                File.WriteAllText(Path.Combine(output, "ui-smoke.txt"), "sprites=" + sprites.Animations.Count + "\nframesPainted=" + pet.FramesPainted + "\nshopFeedWorkFlow=" + (engine.State.Work != null && engine.State.Coins == 50 - config.Foods[0].Price) + "\nresult=" + (pass ? "PASS" : "FAIL"));
                pet.Shutdown(); app.Shutdown(pass ? 0 : 1);
            };
            finish.Start(); return app.Run();
        }
        private static void Render(FrameworkElement element, int width, int height, string path)
        {
            element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
            RenderTargetBitmap bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(element);
            PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream stream = new FileStream(path, FileMode.Create)) encoder.Save(stream);
        }
        private static int NativeSmoke(Application app, PetConfig config, SpriteLibrary sprites, string output)
        {
            Directory.CreateDirectory(output);
            GameEngine engine = new GameEngine(config, new SaveStore(Path.Combine(output, "native-save.json")), DateTime.UtcNow);
            PetWindow pet = new PetWindow(engine, sprites, true) { Opacity = 0 };
            app.MainWindow = pet; pet.Show(); pet.UpdateLayout();
            string hitReport = pet.ProbeNativeHitTest();
            Dashboard panel = new Dashboard(pet) { Opacity = 0, ShowActivated = false };
            panel.Show(); panel.UpdateLayout();
            bool panelHandle = new System.Windows.Interop.WindowInteropHelper(panel).Handle != IntPtr.Zero;
            panel.Hide(); pet.Hide();
            DispatcherTimer finish = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            finish.Tick += delegate {
                finish.Stop();
                bool pass = pet.HasTray && panelHandle && pet.FramesPainted > 5;
                File.WriteAllText(Path.Combine(output, "native-smoke.txt"), hitReport + "\ntrayRegistered=" + pet.HasTray + "\npanelHandle=" + panelHandle + "\nframesPainted=" + pet.FramesPainted + "\nresult=" + (pass ? "PASS" : "FAIL"));
                panel.Close(); pet.Shutdown(); pet.Close(); app.Shutdown(pass ? 0 : 1);
            };
            finish.Start(); return app.Run();
        }
        private static int FoodPreview(Application app, PetConfig config, SpriteLibrary sprites, string output)
        {
            Directory.CreateDirectory(output); DateTime now = DateTime.UtcNow;
            SaveStore store = new SaveStore(Path.Combine(output, "food-preview-save.json"));
            SaveData seed = SaveData.Fresh(config, now); seed.Experience = config.Growth.Levels[2].Experience;
            seed.RewardedLevels = new System.Collections.Generic.List<int> { 1, 2, 3 }; seed.Satiety = 0;
            foreach (Food food in config.Foods) seed.Inventory[food.Id] = 3;
            store.Write(seed); GameEngine engine = new GameEngine(config, store, now);
            PetWindow pet = new PetWindow(engine, sprites, false);
            int foodIndex = 0; DateTime began = DateTime.UtcNow;
            System.Collections.Generic.List<int> seen = new System.Collections.Generic.List<int>();
            System.Collections.Generic.List<string> results = new System.Collections.Generic.List<string>();
            bool heldLast = false;
            pet.Execute(delegate { return engine.Feed(config.Foods[foodIndex].Id, DateTime.UtcNow); });
            DispatcherTimer observe = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            observe.Tick += delegate {
                Food food = config.Foods[foodIndex]; AnimationSpec spec = config.Animations[food.Animation];
                int count = sprites.Animations[food.Animation].Length;
                if (pet.CurrentAnimation == food.Animation)
                {
                    int frame = pet.CurrentFrameIndex;
                    if (!seen.Contains(frame))
                    {
                        seen.Add(frame);
                        Render(pet.Surface, (int)pet.Width, (int)pet.Height, Path.Combine(output, food.Id + "-" + frame.ToString("00") + ".png"));
                    }
                    if (pet.AnimationElapsedMilliseconds > spec.FrameMs * count)
                        heldLast = frame == count - 1;
                }
                if (pet.CurrentAnimation == food.Animation && (DateTime.UtcNow - began).TotalMilliseconds < Math.Max(spec.DurationMs, spec.FrameMs * count) + 5000) return;
                bool ordered = seen.Count == count;
                for (int index = 0; index < seen.Count; index++) if (seen[index] != index) ordered = false;
                bool pass = ordered && heldLast && pet.CurrentAnimation == "idle";
                results.Add((pass ? "PASS " : "FAIL ") + food.Id + " frames=" + String.Join(",", seen) + " heldLast=" + heldLast + " returnedToIdle=" + (pet.CurrentAnimation == "idle"));
                foodIndex++;
                if (foodIndex >= config.Foods.Length)
                {
                    observe.Stop();
                    bool allPassed = !results.Exists(result => result.StartsWith("FAIL"));
                    File.WriteAllLines(Path.Combine(output, "food-playback.txt"), results);
                    pet.Shutdown(); app.Shutdown(allPassed ? 0 : 1); return;
                }
                seen.Clear(); heldLast = false;
                pet.Execute(delegate { return engine.Feed(config.Foods[foodIndex].Id, DateTime.UtcNow); }); began = DateTime.UtcNow;
            };
            observe.Start(); return app.Run();
        }
    }
}
