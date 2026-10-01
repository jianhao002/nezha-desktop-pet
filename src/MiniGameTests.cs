using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NezhaDesktop.MiniGames;

namespace NezhaDesktop
{
    public static class MiniGameTests
    {
        private sealed class LastRandom : Random { public override int Next(int max) { return max - 1; } }
        private sealed class FoodRandom : Random { public override int Next(int max) { return Math.Min(10, max - 1); } }
        private sealed class ZeroRandom : Random { public override int Next(int max) { return 0; } }
        private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private static object Field(object item, string name) { return item.GetType().GetField(name).GetValue(item); }
        private static object Property(object item, string name) { return item.GetType().GetProperty(name).GetValue(item, null); }
        private static object Call(object item, string name, params object[] args)
        { try { return item.GetType().GetMethod(name).Invoke(item, args); } catch (TargetInvocationException ex) { throw ex.InnerException; } }
        private static void Reject(Action action)
        { bool rejected = false; try { action(); } catch { rejected = true; } Check(rejected, "expected rejection"); }
        private static object NewBoard(Assembly plugin, string name, params object[] args)
        { return Activator.CreateInstance(plugin.GetType("NezhaGames." + name, true), args); }
        private static void Capture(FrameworkElement view, int width, int height, string path)
        {
            if (Window.GetWindow(view) == null) {
                view.Measure(new Size(width, height)); view.Arrange(new Rect(0, 0, width, height)); view.UpdateLayout();
            } else {
                view.UpdateLayout();
                Check(Math.Abs(view.ActualWidth - width) < 1 && Math.Abs(view.ActualHeight - height) < 1, "preview does not match native window dimensions");
            }
            RenderTargetBitmap image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(view);
            PngBitmapEncoder png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
            using (FileStream stream = new FileStream(path, FileMode.Create)) png.Save(stream);
        }
        private static IEnumerable<Button> Buttons(DependencyObject root)
        {
            Button button = root as Button; if (button != null) yield return button;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (Button child in Buttons(VisualTreeHelper.GetChild(root, i))) yield return child;
        }
        public static int Run(Application app, string output, string exampleFolder)
        {
            if (Directory.Exists(output)) throw new IOException("小游戏测试目录必须是新目录。");
            Directory.CreateDirectory(output);
            string root = Path.Combine(output, "runtime"), data = Path.Combine(output, "data");
            Directory.CreateDirectory(root);
            List<string> results = new List<string>();
            Action<string, Action> test = delegate(string name, Action body) {
                try { body(); results.Add("PASS " + name); } catch (Exception ex) { results.Add("FAIL " + name + ": " + ex.Message); }
            };
            MiniGameCatalog catalog = new MiniGameCatalog(root, data); List<MiniGameEntry> entries = catalog.Scan();
            test("three-folder-discovery-without-registration", delegate {
                Check(entries.Select(e => e.Manifest.Id).OrderBy(s => s).SequenceEqual(new[] { "2048", "minesweeper", "snake" }), "missing bundled games");
                Check(catalog.Errors.Count == 0, String.Join(";", catalog.Errors));
            });
            Dictionary<string, IMiniGame> games = new Dictionary<string, IMiniGame>();
            foreach (MiniGameEntry entry in entries) {
                MiniGameEntry chosen = entry;
                test("compile-and-create-" + entry.Manifest.Id, delegate {
                    IMiniGame game = catalog.Create(chosen, delegate { return new PetSnapshot("哪吒", 2, 60, 70); }, null);
                    Check(game.View != null, "no view"); games[chosen.Manifest.Id] = game;
                    Capture(game.View, 340, 400, Path.Combine(output, chosen.Manifest.Id + ".png"));
                });
            }
            if (games.ContainsKey("minesweeper")) {
                Assembly plugin = games["minesweeper"].GetType().Assembly;
                test("mines-first-click-zero-and-neighbors-safe", delegate {
                    for (int seed = 0; seed < 30; seed++) {
                        object board = NewBoard(plugin, "MinesBoard", 9, 9, 10, new Random(seed)); Call(board, "Reveal", 40);
                        bool[] mines = (bool[])Field(board, "Mines"); int[] numbers = (int[])Field(board, "Adjacent");
                        Check(mines.Count(m => m) == 10 && numbers[40] == 0 && !mines[40], "unsafe first click");
                        foreach (int neighbor in (IEnumerable<int>)Call(board, "Neighbors", 40)) Check(!mines[neighbor], "mine beside first click");
                    }
                });
                test("mines-flags-do-not-generate-or-open", delegate {
                    object board = NewBoard(plugin, "MinesBoard", 9, 9, 10, new Random(7)); Call(board, "ToggleFlag", 40); Call(board, "Reveal", 40);
                    Check(!(bool)Property(board, "Generated") && (int)Property(board, "OpenCount") == 0, "flagged cell opened");
                    for (int i = 0; i < 20; i++) Call(board, "ToggleFlag", i);
                    Check((int)Property(board, "FlagCount") == 10, "flag count exceeded mine count");
                });
                test("mines-victory-and-repeat-actions", delegate {
                    object board = NewBoard(plugin, "MinesBoard", 9, 9, 10, new Random(3)); Call(board, "Reveal", 40);
                    bool[] mines = (bool[])Field(board, "Mines");
                    for (int i = 0; i < mines.Length; i++) if (!mines[i]) Call(board, "Reveal", i);
                    Check((bool)Property(board, "Won") && (int)Property(board, "OpenCount") == 71, "no win");
                    Call(board, "Reveal", Array.FindIndex(mines, m => m));
                    Check(!(bool)Property(board, "Lost") && (int)Property(board, "OpenCount") == 71, "finished board changed");
                });
                test("mines-loss-locks-further-operations", delegate {
                    object board = NewBoard(plugin, "MinesBoard", 9, 9, 10, new Random(5)); Call(board, "Reveal", 40);
                    Call(board, "Reveal", Array.FindIndex((bool[])Field(board, "Mines"), m => m));
                    int count = (int)Property(board, "OpenCount"); Call(board, "Reveal", 0);
                    Check((bool)Property(board, "Lost") && (int)Property(board, "OpenCount") == count, "loss not locked");
                });
            }
            if (games.ContainsKey("snake")) {
                Assembly plugin = games["snake"].GetType().Assembly;
                test("snake-reversal-and-two-turn-burst-rejected", delegate {
                    object board = NewBoard(plugin, "SnakeBoard", 5, false, new ZeroRandom());
                    Check(!(bool)Call(board, "Turn", -1, 0), "reverse accepted");
                    Check((bool)Call(board, "Turn", 0, -1) && !(bool)Call(board, "Turn", -1, 0), "multiple turns accepted");
                    Call(board, "Step"); Check(((List<int>)Field(board, "Body"))[0] == 7, "wrong queued direction");
                });
                test("snake-food-grows-once-and-awards-score", delegate {
                    object board = NewBoard(plugin, "SnakeBoard", 5, false, new FoodRandom()); Call(board, "Step");
                    Check(((List<int>)Field(board, "Body")).Count == 4 && (int)Property(board, "Score") == 10, "food not consumed");
                    Check(!((List<int>)Field(board, "Body")).Contains((int)Property(board, "Food")), "food inside body");
                });
                test("snake-classic-wall-and-wrap-mode", delegate {
                    object classic = NewBoard(plugin, "SnakeBoard", 5, false, new ZeroRandom());
                    object wrap = NewBoard(plugin, "SnakeBoard", 5, true, new ZeroRandom());
                    for (int i = 0; i < 3; i++) { Call(classic, "Step"); Call(wrap, "Step"); }
                    Check((bool)Property(classic, "Ended") && !(bool)Property(wrap, "Ended") && ((List<int>)Field(wrap, "Body"))[0] == 10, "wrong wall behavior");
                });
                test("snake-moving-tail-is-legal-but-body-collision-is-not", delegate {
                    object board = NewBoard(plugin, "SnakeBoard", 5, false, new ZeroRandom());
                    List<int> body = (List<int>)Field(board, "Body"); body.Clear(); body.AddRange(new[] { 6, 5, 10, 11 });
                    Call(board, "Turn", 0, 1); Call(board, "Step"); Check(!(bool)Property(board, "Ended") && body[0] == 11, "moving tail collision");
                    object blocked = NewBoard(plugin, "SnakeBoard", 5, false, new ZeroRandom());
                    body = (List<int>)Field(blocked, "Body"); body.Clear(); body.AddRange(new[] { 6, 7, 12, 11 }); Call(blocked, "Step");
                    Check((bool)Property(blocked, "Ended"), "body collision ignored");
                });
            }
            if (games.ContainsKey("2048")) {
                Assembly plugin = games["2048"].GetType().Assembly;
                test("2048-one-merge-per-tile", delegate {
                    object board = NewBoard(plugin, "MergeBoard", new LastRandom()); int[] cells = (int[])Field(board, "Cells"); Array.Clear(cells, 0, 16);
                    cells[0] = cells[1] = cells[2] = cells[3] = 2; Call(board, "Move", 0);
                    Check(cells[0] == 4 && cells[1] == 4 && cells[2] == 0 && (int)Property(board, "Score") == 8 && cells.Sum() == 10, "incorrect chain merge");
                });
                test("2048-three-equal-tiles-leave-one-unmerged", delegate {
                    object board = NewBoard(plugin, "MergeBoard", new LastRandom()); int[] cells = (int[])Field(board, "Cells"); Array.Clear(cells, 0, 16);
                    cells[0] = cells[1] = cells[2] = 2; Call(board, "Move", 0); Check(cells[0] == 4 && cells[1] == 2, "triple merge error");
                });
                test("2048-all-directions", delegate {
                    for (int direction = 1; direction <= 3; direction++) {
                        object board = NewBoard(plugin, "MergeBoard", new LastRandom()); int[] cells = (int[])Field(board, "Cells"); Array.Clear(cells, 0, 16);
                        cells[0] = 2; cells[direction == 1 ? 1 : 4] = 2; Call(board, "Move", direction);
                        Check(cells[direction == 1 ? 3 : direction == 2 ? 0 : 12] == 4, "wrong direction");
                    }
                });
                test("2048-no-op-does-not-spawn-or-score", delegate {
                    object board = NewBoard(plugin, "MergeBoard", new LastRandom()); int[] cells = (int[])Field(board, "Cells"); Array.Clear(cells, 0, 16); cells[0] = 2;
                    Check(!(bool)Call(board, "Move", 0) && cells.Count(v => v > 0) == 1 && (int)Property(board, "Score") == 0, "no-op changed board");
                });
                test("2048-full-board-can-still-merge", delegate {
                    object board = NewBoard(plugin, "MergeBoard", new LastRandom()); int[] cells = (int[])Field(board, "Cells");
                    for (int i = 0; i < 16; i++) cells[i] = (i % 4 + i / 4) % 2 == 0 ? 2 : 4;
                    Check((bool)Property(board, "Ended"), "blocked board playable"); cells[15] = cells[14];
                    Check(!(bool)Property(board, "Ended"), "mergeable full board ended"); cells[0] = 2048; Check((bool)Property(board, "Won"), "2048 not recognized");
                });
            }
            test("score-improvement-and-reload", delegate {
                string scores = Path.Combine(output, "record-test");
                MiniGameContext context = new MiniGameContext(root, scores, null, null);
                Check(context.Record("high", 10, false) && !context.Record("high", 9, false), "high score regression");
                Check(context.Record("low", 100, true) && context.Record("low", 80, true) && !context.Record("low", 90, true), "low score regression");
                MiniGameContext loaded = new MiniGameContext(root, scores, null, null);
                Check(loaded.Best("high", 0) == 10 && loaded.Best("low", 0) == 80, "record not persisted");
                Reject(delegate { context.Record("../save", 1, false); });
            });
            test("invalid-folder-isolated-and-traversal-rejected", delegate {
                string bad = Path.Combine(root, "games", "bad"); Directory.CreateDirectory(bad);
                File.WriteAllText(Path.Combine(bad, "game.json"), "{\"apiVersion\":1,\"id\":\"bad\",\"name\":\"bad\",\"entryType\":\"Bad.Game\",\"sources\":[\"../Game.cs\"]}");
                Check(catalog.Scan().Count == 3 && catalog.Errors.Count == 1, "bad folder broke catalog");
            });
            test("add-a-folder-without-host-rebuild", delegate {
                string folder = Path.Combine(root, "games", "example"); Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "game.json"), "{\"apiVersion\":1,\"id\":\"example\",\"name\":\"示例\",\"entryType\":\"Example.Game\",\"sources\":[\"Game.cs\"]}");
                File.WriteAllText(Path.Combine(folder, "Game.cs"), "using System.Windows; using System.Windows.Controls; using NezhaDesktop.MiniGames; namespace Example { public class Game : MiniGameControl { protected override UIElement Build() { return new TextBlock { Text = Host.Pet.Name }; } public override void SetHostPaused(bool p) {} public override void Dispose() {} } }");
                MiniGameEntry entry = catalog.Scan().Single(e => e.Manifest.Id == "example");
                using (IMiniGame game = catalog.Create(entry, delegate { return new PetSnapshot("哪吒", 1, 50, 50); }, null))
                    Check(((TextBlock)((ContentControl)game.View).Content).Text == "哪吒", "extension or read-only pet context failed");
            });
            test("edited-bundled-source-preserved", delegate {
                string path = Path.Combine(root, "games", "2048", "Game.cs"); File.AppendAllText(path, "\n// local customization\n");
                string before = UpdateSafety.FileHash(path); catalog.Scan(); Check(before == UpdateSafety.FileHash(path), "custom source overwritten");
            });
            test("bad-plugin-compilation-does-not-break-other-game", delegate {
                string source = Path.Combine(root, "games", "example", "Game.cs"); File.WriteAllText(source, "this is not C#");
                Reject(delegate { catalog.Create(catalog.Scan().Single(e => e.Manifest.Id == "example"), null, null); });
                using (IMiniGame valid = catalog.Create(entries.Single(e => e.Manifest.Id == "2048"), null, null)) Check(valid.View != null, "healthy game failed");
            });
            if (exampleFolder != null) test("shipped-template-compiles-and-obeys-host-pause", delegate {
                string folder = Path.Combine(root, "games", "my-game"); Directory.CreateDirectory(folder);
                foreach (string file in new[] { "game.json", "Game.cs" }) File.Copy(Path.Combine(exampleFolder, file), Path.Combine(folder, file));
                MiniGameEntry entry = catalog.Scan().Single(e => e.Manifest.Id == "my-game");
                using (IMiniGame game = catalog.Create(entry, delegate { return new PetSnapshot("测试哪吒", 2, 60, 70); }, null)) {
                    Check((string)Property(game, "PetName") == "测试哪吒", "template did not read pet context");
                    Capture(game.View, 280, 300, Path.Combine(output, "template.png"));
                    Button tap = Buttons(game.View).Single(b => Convert.ToString(b.Content) == "点一下");
                    tap.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check((int)Property(game, "Count") == 1, "template did not accept input");
                    game.SetHostPaused(true); tap.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(!tap.IsEnabled && (int)Property(game, "Count") == 1, "template advanced while paused");
                    game.SetHostPaused(false); tap.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check((int)Property(game, "Count") == 2, "template did not resume");
                }
            });
            foreach (IMiniGame game in games.Values) game.Dispose();
            MiniGameWindow window = new MiniGameWindow(root, Path.Combine(output, "window-data"), null, false) { Opacity = 0, ShowActivated = false };
            app.MainWindow = window; window.Show(); window.UpdateLayout();
            test("native-window-and-three-size-previews", delegate {
                Check(new System.Windows.Interop.WindowInteropHelper(window).Handle != IntPtr.Zero, "no native handle");
                foreach (MiniGameEntry entry in entries) {
                    window.StartForTest(entry);
                    foreach (int width in new[] { 310, 380, 470 }) {
                        int height = width == 310 ? 465 : width == 380 ? 550 : 650;
                        window.Width = width; window.Height = height; window.UpdateLayout();
                        Capture(window.PreviewRoot, width, height, Path.Combine(output, entry.Manifest.Id + "-" + width + ".png"));
                    }
                }
            });
            if (!entries.Any(e => e.Manifest.Id == "snake")) { window.Close(); return Finish(results, output); }
            window.StartForTest(entries.Single(e => e.Manifest.Id == "snake")); window.UpdateLayout();
            IMiniGame snake = window.ActiveGame; object snakeBoard = Property(snake, "Board");
            Button start = Buttons(snake.View).First(b => Convert.ToString(b.Content).StartsWith("开始", StringComparison.Ordinal));
            start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); snake.SetHostPaused(false);
            int initial = ((List<int>)Field(snakeBoard, "Body"))[0], frozen = 0, stage = 0;
            DispatcherTimer tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            tick.Tick += delegate {
                if (stage == 0) {
                    test("snake-native-timer-runs", delegate { Check(((List<int>)Field(snakeBoard, "Body"))[0] != initial, "timer did not advance"); });
                    window.QuickHide(); frozen = ((List<int>)Field(snakeBoard, "Body"))[0]; stage++;
                } else if (stage == 1) {
                    test("quick-hide-freezes-active-game", delegate { Check(!window.IsVisible && window.HostPaused && ((List<int>)Field(snakeBoard, "Body"))[0] == frozen, "hidden game kept moving"); });
                    snake.SetHostPaused(false); stage++;
                } else {
                    tick.Stop();
                    test("same-game-can-resume-after-hide", delegate { Check(((List<int>)Field(snakeBoard, "Body"))[0] != frozen, "resume failed"); });
                    window.Close(); int code = Finish(results, output); app.Shutdown(code);
                }
            };
            tick.Start(); return app.Run();
        }
        private static int Finish(List<string> results, string output)
        {
            int failures = results.Count(line => line.StartsWith("FAIL", StringComparison.Ordinal));
            File.WriteAllText(Path.Combine(output, "mini-game-results.txt"), String.Join("\n", results) + "\nresult=" + (failures == 0 ? "PASS" : "FAIL") + "\n");
            return failures == 0 ? 0 : 1;
        }
    }
}
