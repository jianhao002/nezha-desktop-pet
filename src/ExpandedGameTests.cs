using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NezhaDesktop.MiniGames;

namespace NezhaDesktop
{
    internal static class ExpandedGameTests
    {
        private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private static object Get(object item, string name) { return item.GetType().GetProperty(name).GetValue(item, null); }
        private static object Field(object item, string name) { return item.GetType().GetField(name).GetValue(item); }
        private static object Call(object item, string name, params object[] args)
        { try { return item.GetType().GetMethod(name).Invoke(item, args); } catch (TargetInvocationException ex) { throw ex.InnerException; } }
        private static object New(IMiniGame game, string name, params object[] args) { return Activator.CreateInstance(game.GetType().Assembly.GetType("NezhaGames." + name, true), args); }
        private static IEnumerable<T> Controls<T>(DependencyObject root) where T : DependencyObject
        { T control = root as T; if (control != null) yield return control; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (T child in Controls<T>(VisualTreeHelper.GetChild(root, i))) yield return child; }
        private static void Click(IMiniGame game, string label) { Controls<Button>(game.View).Single(b => Convert.ToString(b.Content) == label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        private static void Cell(IMiniGame game, int index) { Controls<Button>(game.View).Single(b => b.Tag is int && (int)b.Tag == index).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        private static void Pump(int milliseconds)
        {
            DispatcherFrame frame = new DispatcherFrame(); DispatcherTimer deadline = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            deadline.Tick += delegate { deadline.Stop(); frame.Continue = false; }; deadline.Start(); Dispatcher.PushFrame(frame);
        }
        private static DispatcherTimer Timer(IMiniGame game) { return (DispatcherTimer)game.GetType().GetField("timer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(game); }
        public static void Run(Action<string, Action> test, Dictionary<string, IMiniGame> games)
        {
            if (games.ContainsKey("sliding-puzzle")) {
                IMiniGame game = games["sliding-puzzle"];
                test("sliding-all-sizes-have-valid-solvable-unsolved-permutations", delegate {
                    foreach (int size in new[] { 3, 4, 5 }) for (int seed = 0; seed < 12; seed++) {
                        object board = New(game, "SlidingBoard", size, new Random(seed)); int[] cells = (int[])Field(board, "Cells");
                        Check(cells.OrderBy(i => i).SequenceEqual(Enumerable.Range(0, size * size)) && !(bool)Get(board, "Won") && (int)Get(board, "Moves") == 0, "invalid shuffled puzzle");
                        int inversions = 0; for (int i = 0; i < cells.Length; i++) for (int j = i + 1; j < cells.Length; j++) if (cells[i] != 0 && cells[j] != 0 && cells[i] > cells[j]) inversions++;
                        int fromBottom = size - (int)Get(board, "Blank") / size;
                        Check(size % 2 == 1 ? inversions % 2 == 0 : (inversions + fromBottom) % 2 == 1, "unsolvable permutation");
                    }
                });
                test("sliding-nonneighbor-rejected-and-win-locks-board", delegate {
                    object board = New(game, "SlidingBoard", 3, new Random(1)); int[] cells = (int[])Field(board, "Cells"); int blank = (int)Get(board, "Blank");
                    int remote = Enumerable.Range(0, 9).First(i => Math.Abs(i % 3 - blank % 3) + Math.Abs(i / 3 - blank / 3) > 1);
                    Check(!(bool)Call(board, "Move", remote) && (int)Get(board, "Moves") == 0, "distant tile moved");
                    for (int i = 0; i < 9; i++) cells[i] = i == blank ? 0 : i + 1;
                    // Place one missing number next to the existing blank, then move it home.
                    int neighbor = blank % 3 > 0 ? blank - 1 : blank + 1;
                    Check((bool)Call(board, "Move", neighbor) && (int)Get(board, "Moves") == 1, "neighbor not accepted");
                    for (int i = 0; i < 9; i++) cells[i] = i == 8 ? 0 : i + 1;
                    Check((bool)Get(board, "Won") && !(bool)Call(board, "Move", 7), "finished puzzle changed");
                });
                test("sliding-host-pause-rejects-clicks-and-resumes", delegate {
                    object board = Get(game, "Board"); int size = (int)Field(board, "Size"), blank = (int)Get(board, "Blank"), neighbor = blank % size > 0 ? blank - 1 : blank + 1;
                    game.SetHostPaused(true); Cell(game, neighbor); Check((int)Get(board, "Moves") == 0, "paused puzzle moved");
                    game.SetHostPaused(false); Cell(game, neighbor); Check((int)Get(board, "Moves") == 1, "puzzle did not resume");
                });
            }
            if (games.ContainsKey("memory")) {
                IMiniGame game = games["memory"];
                test("memory-decks-contain-exact-pairs-at-all-tiers", delegate {
                    foreach (int pairs in new[] { 6, 8, 12 }) for (int seed = 0; seed < 8; seed++) {
                        object board = New(game, "MemoryBoard", pairs, new Random(seed)); int[] deck = (int[])Field(board, "Cards");
                        Check(deck.Length == pairs * 2 && deck.GroupBy(v => v).Count() == pairs && deck.GroupBy(v => v).All(g => g.Count() == 2), "bad pair deck");
                    }
                });
                test("memory-mismatch-locks-third-flip-and-match-finishes", delegate {
                    object board = New(game, "MemoryBoard", 6, new Random(9)); int[] deck = (int[])Field(board, "Cards"); int other = Array.FindIndex(deck, v => v != deck[0]);
                    Call(board, "Flip", 0); Check(!(bool)Call(board, "Flip", 0), "same card counted twice"); Call(board, "Flip", other);
                    Check((bool)Get(board, "Pending") && !(bool)Call(board, "Flip", (other + 1) % deck.Length), "third card accepted"); Call(board, "Resolve");
                    Check(((bool[])Field(board, "Open")).All(v => !v) && (int)Get(board, "Turns") == 1, "mismatch failed to close");
                    foreach (IGrouping<int, int> pair in Enumerable.Range(0, deck.Length).GroupBy(i => deck[i])) { int[] indices = pair.ToArray(); Call(board, "Flip", indices[0]); Call(board, "Flip", indices[1]); }
                    Check((bool)Get(board, "Won") && (int)Get(board, "Turns") == 7 && !(bool)Call(board, "Flip", 0), "pair victory failed");
                });
                test("memory-delay-freezes-on-host-pause-and-disposes", delegate {
                    object board = Get(game, "Board"); int[] deck = (int[])Field(board, "Cards"); int other = Array.FindIndex(deck, v => v != deck[0]);
                    Cell(game, 0); Cell(game, other); game.SetHostPaused(true); Pump(850); Check((bool)Get(board, "Pending"), "hidden mismatch closed");
                    game.SetHostPaused(false); Pump(850); Check(!(bool)Get(board, "Pending") && ((bool[])Field(board, "Open")).All(v => !v), "mismatch did not resume");
                    game.Dispose(); Check(!Timer(game).IsEnabled, "memory timer survived dispose"); game.SetHostPaused(false); Check(!Timer(game).IsEnabled, "disposed timer restarted");
                });
            }
            if (games.ContainsKey("gomoku")) {
                IMiniGame game = games["gomoku"];
                test("gomoku-four-directions-and-finished-board-lock", delegate {
                    foreach (int delta in new[] { 1, 9, 10, 8 }) {
                        object board = New(game, "GomokuBoard", 9); int start = delta == 8 ? 4 : 0;
                        for (int n = 0; n < 5; n++) { Check((bool)Call(board, "Play", start + n * delta, 1), "human move rejected"); if (n < 4) Check((bool)Call(board, "Play", 72 + n, 2), "opponent move rejected"); }
                        Check((int)Get(board, "Winner") == 1 && !(bool)Call(board, "Play", 80, 2), "five-in-row not detected or win unlocked");
                    }
                });
                test("gomoku-rejects-wrong-turn-and-row-wrap", delegate {
                    object board = New(game, "GomokuBoard", 9); Check(!(bool)Call(board, "Play", 0, 2), "wrong turn accepted");
                    int[] cells = (int[])Field(board, "Cells"); for (int i = 6; i < 10; i++) cells[i] = 1;
                    Check((bool)Call(board, "Play", 10, 1) && (int)Get(board, "Winner") == 0, "line incorrectly wrapped across rows"); Check(!(bool)Call(board, "Play", 10, 2), "occupied cell accepted");
                });
                test("gomoku-computer-takes-win-and-blocks-threat", delegate {
                    object board = New(game, "GomokuBoard", 9); Call(board, "Play", 80, 1); int[] cells = (int[])Field(board, "Cells");
                    for (int i = 0; i < 4; i++) cells[i] = 1; Check((int)Call(board, "ChooseMove", new Random(2), 2) == 4, "computer missed immediate block");
                    for (int i = 9; i < 13; i++) cells[i] = 2; Check((int)Call(board, "ChooseMove", new Random(2), 2) == 13, "computer ignored own win");
                    Check(cells[4] == 0 && cells[13] == 0, "move selection mutated board");
                });
                test("gomoku-paused-click-and-ai-turn-pair", delegate {
                    object board = Get(game, "Board"); game.SetHostPaused(true); Cell(game, 40); Check((int)Get(board, "Moves") == 0, "paused opponent advanced");
                    game.SetHostPaused(false); Cell(game, 40); Check((int)Get(board, "Moves") == 2 && (int)Get(board, "Turn") == 1, "computer did not reply exactly once");
                });
            }
            if (games.ContainsKey("sudoku")) {
                IMiniGame game = games["sudoku"];
                test("sudoku-all-tiers-unique-solutions-and-valid-givens", delegate {
                    Type type = game.GetType().Assembly.GetType("NezhaGames.SudokuBoard");
                    foreach (int clues in new[] { 46, 40, 34, 30 }) for (int seed = 0; seed < 3; seed++) {
                        object board = New(game, "SudokuBoard", clues, new Random(seed)); int[] cells = (int[])Field(board, "Cells"), solution = (int[])Field(board, "Solution");
                        Check((int)type.GetMethod("CountSolutions").Invoke(null, new object[] { cells, 2 }) == 1, "puzzle not unique");
                        Check((int)Get(board, "Clues") >= clues && (int)Get(board, "Clues") <= clues + 6, "clue tier out of range");
                        for (int row = 0; row < 9; row++) Check(solution.Skip(row * 9).Take(9).OrderBy(v => v).SequenceEqual(Enumerable.Range(1, 9)), "invalid solution row");
                        Check(Enumerable.Range(0, 81).All(i => cells[i] == 0 || cells[i] == solution[i]), "givens mismatch solution");
                    }
                });
                test("sudoku-givens-locked-conflicts-detected-and-solve", delegate {
                    object board = New(game, "SudokuBoard", 40, new Random(7)); int[] cells = (int[])Field(board, "Cells"), solution = (int[])Field(board, "Solution"); bool[] given = (bool[])Field(board, "Given");
                    int fixedCell = Array.FindIndex(given, v => v), empty = Array.FindIndex(given, v => !v);
                    Check(!(bool)Call(board, "Set", fixedCell, 0) && !(bool)Call(board, "Set", empty, 10), "invalid edit accepted");
                    int peer = Enumerable.Range(0, 81).First(i => i != empty && i / 9 == empty / 9 && cells[i] != 0); Call(board, "Set", empty, cells[peer]); Check((bool)Call(board, "Conflict", empty), "duplicate not marked");
                    for (int i = 0; i < 81; i++) if (!given[i]) Call(board, "Set", i, solution[i]);
                    Check((bool)Get(board, "Won") && !(bool)Call(board, "Set", empty, 0), "completion not locked");
                });
                test("sudoku-hint-and-clock-obey-pause-and-dispose", delegate {
                    int[] initial = (int[])((int[])Field(Get(game, "Board"), "Cells")).Clone(); game.SetHostPaused(true); Click(game, "提示");
                    Check(((int[])Field(Get(game, "Board"), "Cells")).SequenceEqual(initial) && (int)Get(game, "Hints") == 0, "paused hint changed grid");
                    game.SetHostPaused(false); Click(game, "提示"); Pump(1100); Check((int)Get(game, "Hints") == 1 && (int)Get(game, "ElapsedSeconds") >= 1, "hint or clock not started");
                    game.SetHostPaused(true); int frozen = (int)Get(game, "ElapsedSeconds"); Pump(1100); Check((int)Get(game, "ElapsedSeconds") == frozen, "hidden clock advanced");
                    game.SetHostPaused(false); Pump(1100); Check((int)Get(game, "ElapsedSeconds") > frozen, "clock not resumed");
                    game.Dispose(); Check(!Timer(game).IsEnabled, "sudoku timer survived dispose");
                });
            }
            if (games.ContainsKey("tetris")) {
                IMiniGame game = games["tetris"];
                test("blocks-seven-bag-and-legal-spawn", delegate {
                    object board = New(game, "BlockBoard", new Random(11)); HashSet<int> kinds = new HashSet<int>();
                    for (int i = 0; i < 7; i++) { kinds.Add((int)Get(board, "Kind")); int[] active = (int[])Call(board, "ActiveCells"); Check(active.Distinct().Count() == 4 && active.All(v => v >= 0 && v < 200), "bad spawn"); Array.Clear((int[])Field(board, "Cells"), 0, 200); Call(board, "HardDrop"); }
                    Check(kinds.Count == 7, "bag missing or repeating shapes");
                });
                test("blocks-wall-rotation-and-hard-drop-stop-overlap", delegate {
                    object board = New(game, "BlockBoard", new Random(4)); for (int i = 0; i < 20; i++) Call(board, "Move", -1, 0); for (int i = 0; i < 4; i++) Call(board, "Rotate");
                    Check(((int[])Call(board, "ActiveCells")).All(i => i >= 0 && i < 200), "rotation left board"); Call(board, "HardDrop");
                    Check(((int[])Field(board, "Cells")).Count(v => v != 0) == 4 && (int)Get(board, "Score") > 0, "drop did not lock shape");
                });
                test("blocks-clears-adjacent-full-lines-and-preserves-upper-rows", delegate {
                    object board = New(game, "BlockBoard", new Random(1)); int[] cells = (int[])Field(board, "Cells"); for (int i = 180; i < 200; i++) cells[i] = 1; cells[175] = 2;
                    Check((int)Call(board, "ClearLines") == 2 && cells[195] == 2 && cells.Count(v => v != 0) == 1, "line collapse failed");
                });
                test("blocks-top-out-locks-further-input", delegate {
                    object board = New(game, "BlockBoard", new Random(1)); int[] cells = (int[])Field(board, "Cells"); for (int i = 0; i < 10; i++) cells[i] = i == 0 ? 0 : 1; Call(board, "HardDrop");
                    Check((bool)Get(board, "Ended"), "blocked spawn did not end"); int before = (int)Get(board, "Score"); Call(board, "HardDrop"); Check((int)Get(board, "Score") == before && !(bool)Call(board, "Move", 1, 0), "ended board moved");
                });
                test("blocks-gravity-freezes-resumes-and-disposes", delegate {
                    Controls<ComboBox>(game.View).Single().SelectedIndex = 3; Click(game, "新一局"); Controls<Button>(game.View).Single(b => Convert.ToString(b.Content).StartsWith("开始", StringComparison.Ordinal)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    game.SetHostPaused(false); Pump(310); object board = Get(game, "Board"); Check((int)Get(board, "Y") > 0, "gravity not running"); game.SetHostPaused(true); int frozen = (int)Get(board, "Y");
                    Click(game, "直落"); Pump(310); Check((int)Get(board, "Y") == frozen && ((int[])Field(board, "Cells")).All(v => v == 0), "paused blocks advanced");
                    game.SetHostPaused(false); Pump(310); Check((int)Get(board, "Y") > frozen, "gravity not resumed"); game.Dispose(); Check(!Timer(game).IsEnabled, "block timer survived dispose");
                });
            }
            if (games.ContainsKey("breakout")) {
                IMiniGame game = games["breakout"];
                test("breakout-tiers-paddle-clamp-and-walls", delegate {
                    foreach (int tier in new[] { 0, 1, 2 }) {
                        object board = New(game, "BreakoutBoard", 3 + tier, new[] { 190.0, 240.0, 290.0 }[tier]); Call(board, "SetPaddle", -500.0); Check((double)Get(board, "Paddle") == 32, "left clamp failed"); Call(board, "SetPaddle", 1000.0); Check((double)Get(board, "Paddle") == 268, "right clamp failed"); Call(board, "Launch");
                        for (int i = 0; i < 40; i++) { Call(board, "Step", 0.02, 0); Check((double)Get(board, "BallX") >= 4 && (double)Get(board, "BallX") <= 296, "ball escaped walls"); }
                        Check(((bool[])Field(board, "Bricks")).Length == (3 + tier) * 8, "wrong brick tier");
                    }
                });
                test("breakout-brick-hit-scores-once-and-paddle-bounces", delegate {
                    object board = New(game, "BreakoutBoard", 1, 190.0); Call(board, "Launch"); for (int i = 0; i < 100 && (int)Get(board, "Score") == 0; i++) Call(board, "Step", 0.02, 0);
                    Check((int)Get(board, "Score") == 10 && ((bool[])Field(board, "Bricks")).Count(v => !v) == 1, "brick not removed or scored twice");
                    // Track the descending ball with the paddle; at least one return must occur.
                    bool bounced = false; for (int i = 0; i < 220; i++) { double previous = (double)Get(board, "VY"); Call(board, "SetPaddle", (double)Get(board, "BallX")); Call(board, "Step", 0.02, 0); if (previous > 0 && (double)Get(board, "VY") < 0 && (double)Get(board, "BallY") > 250) bounced = true; }
                    Check(bounced && (int)Get(board, "Lives") == 3, "paddle did not bounce safely");
                });
                test("breakout-lost-ball-costs-one-life-and-wait-for-launch", delegate {
                    object board = New(game, "BreakoutBoard", 1, 190.0); Call(board, "Launch");
                    for (int i = 0; i < 1200 && (int)Get(board, "Lives") == 3; i++) { double x = (double)Get(board, "BallX"); Call(board, "SetPaddle", x < 150 ? 268.0 : 32.0); Call(board, "Step", 0.02, 0); }
                    Check((int)Get(board, "Lives") == 2 && (bool)Get(board, "Serving"), "life did not reset to serve"); for (int i = 0; i < 50; i++) Call(board, "Step", 0.02, 0);
                    Check((int)Get(board, "Lives") == 2, "serve lost more lives");
                });
                test("breakout-native-timer-pauses-resumes-and-disposes", delegate {
                    Click(game, "发球"); game.SetHostPaused(false); double first = (double)Get(Get(game, "Board"), "BallY"); Pump(100); Check((double)Get(Get(game, "Board"), "BallY") != first, "ball timer not running");
                    game.SetHostPaused(true); double frozen = (double)Get(Get(game, "Board"), "BallY"); Pump(100); Check((double)Get(Get(game, "Board"), "BallY") == frozen, "hidden ball moved");
                    game.SetHostPaused(false); Pump(100); Check((double)Get(Get(game, "Board"), "BallY") != frozen, "ball not resumed"); game.Dispose(); Check(!Timer(game).IsEnabled, "ball timer survived dispose");
                });
            }
        }
    }
}
