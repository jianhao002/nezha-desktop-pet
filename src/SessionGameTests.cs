using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NezhaDesktop.MiniGames;
namespace NezhaDesktop
{
    internal static class SessionGameTests
    {
        private static void Check(bool valid,string message){if(!valid)throw new InvalidOperationException(message);}
        private static object Get(object item,string name){return item.GetType().GetProperty(name).GetValue(item,null);}
        private static object Call(object item,string name,params object[] args){try{return item.GetType().GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).Invoke(item,args);}catch(TargetInvocationException ex){throw ex.InnerException;}}
        private static IEnumerable<Button> Buttons(DependencyObject root){Button button=root as Button;if(button!=null)yield return button;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(Button child in Buttons(VisualTreeHelper.GetChild(root,i)))yield return child;}
        private static void Click(IMiniGame game,string text){game.View.Measure(new Size(340,400));game.View.Arrange(new Rect(0,0,340,400));game.View.UpdateLayout();Buttons(game.View).Single(b=>Convert.ToString(b.Content)==text).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
        private static string Snapshot(IMiniGame game){MethodInfo method=game.GetType().GetMethod("CaptureSession",BindingFlags.Instance|BindingFlags.NonPublic);return new JavaScriptSerializer().Serialize(method.Invoke(game,null));}
        private static void Mutate(string id,IMiniGame game)
        {
            game.SetHostPaused(false);object board=Get(game,"Board");
            switch(id){
                case "2048":Call(game,"Move",0);break;
                case "minesweeper":Call(game,"Reveal",40,false);break;
                case "snake":Call(board,"Turn",0,-1);Call(board,"Step");break;
                case "tetris":Call(board,"Move",1,0);Call(board,"Rotate");Call(board,"HardDrop");break;
                case "breakout":Call(board,"Launch");Call(board,"Step",0.03,0);break;
                case "memory":Call(board,"Flip",0);int[] cards=(int[])board.GetType().GetField("Cards").GetValue(board);int other=Enumerable.Range(1,cards.Length-1).First(i=>cards[i]!=cards[0]);Call(board,"Flip",other);break;
                case "sudoku":int[] solution=(int[])board.GetType().GetField("Solution").GetValue(board);int selected=(int)game.GetType().GetField("selected",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(game);Call(game,"Input",solution[selected],false);break;
                case "gomoku":Call(game,"Play",40);break;
                case "sliding-puzzle":int blank=(int)Get(board,"Blank"),size=(int)board.GetType().GetField("Size").GetValue(board);Call(game,"Move",blank%size>0?blank-1:blank+1);break;
                case "link-match":int[] pair=(int[])Call(board,"FindPair");Call(board,"Match",pair[0],pair[1]);break;
                case "same-game":int[] cells=(int[])board.GetType().GetField("Cells").GetValue(board);int index=Enumerable.Range(0,cells.Length).First(i=>((int[])Call(board,"Region",i)).Length>=2);Call(board,"Remove",index);break;
                case "net-lights":Call(board,"Rotate",0);Call(board,"ToggleLock",1);break;
                case "solitaire":Call(board,"Draw");Call(board,"Draw");break;
            }
            game.SetHostPaused(true);
        }
        public static void Run(Action<string,Action> test,string root,string output,List<MiniGameEntry> entries)
        {
            foreach(MiniGameEntry entry in entries){MiniGameEntry chosen=entry;test("resume-board-score-options-and-pending-state-"+chosen.Manifest.Id,delegate{
                string data=Path.Combine(output,"roundtrip-"+chosen.Manifest.Id);MiniGameCatalog catalog=new MiniGameCatalog(root,data);Type type=catalog.Compile(chosen);string before;
                using(IMiniGame first=catalog.Create(chosen,type,null,null)){Check(((IMiniGameSession)first).CanResume,"missing resume support");Mutate(chosen.Manifest.Id,first);before=Snapshot(first);Check(before!="null","fixture unexpectedly finished");((IMiniGameSession)first).SaveSession();}
                List<string> notices=new List<string>();using(IMiniGame resumed=catalog.Create(chosen,type,null,notices.Add)){Check(notices.Any(t=>t.StartsWith("已继续")),String.Join(";",notices));Check(Snapshot(resumed)==before,"restored state differs");if(new[]{"snake","tetris","breakout"}.Contains(chosen.Manifest.Id)){resumed.SetHostPaused(false);DispatcherTimer timer=(DispatcherTimer)resumed.GetType().GetField("timer",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(resumed);Check(!timer.IsEnabled,"restored dynamic game auto-started");}if(chosen.Manifest.Id=="solitaire")Check((bool)Get(Get(resumed,"Board"),"CanUndo"),"undo history not restored");}
            });}
            test("session-backup-recovery-and-explicit-finish-prevent-resurrection",delegate{
                string folder=Path.Combine(output,"session-backup");MiniGameContext host=new MiniGameContext(root,folder,null,null);host.StoreSession(new{value=1});host.StoreSession(new{value=2});File.WriteAllText(Path.Combine(folder,"session.json"),"corrupt");Check(host.LoadSession().Contains("1"),"backup not recovered");host.StoreSession(null);Check(host.LoadSession()==null,"finished game resurrected from backup");
            });
            test("session-invalid-values-preserved-until-user-new-game",delegate{
                MiniGameEntry entry=entries.Single(e=>e.Manifest.Id=="2048");string data=Path.Combine(output,"corrupt-session");MiniGameCatalog catalog=new MiniGameCatalog(root,data);Type type=catalog.Compile(entry);MiniGameContext host=new MiniGameContext(entry.Directory,Path.Combine(data,"scores","2048"),null,null);host.StoreSession(new{Cells=new[]{-10},Score=1});string file=Path.Combine(data,"scores","2048","session.json"),hash=UpdateSafety.FileHash(file);List<string> notices=new List<string>();
                using(IMiniGame game=catalog.Create(entry,type,null,notices.Add)){Check(notices.Any(t=>t.StartsWith("续局未读取")),"invalid state not reported");((IMiniGameSession)game).SaveSession();Check(UpdateSafety.FileHash(file)==hash,"invalid original silently overwritten");Click(game,"新一局");((IMiniGameSession)game).SaveSession();Check(UpdateSafety.FileHash(file)!=hash,"explicit new game did not start fresh");}
            });
            test("session-save-failure-keeps-window-game-and-blocks-close",delegate{
                string data=Path.Combine(output,"failed-close");MiniGameWindow window=new MiniGameWindow(root,data,null,false){Opacity=0,ShowActivated=false};window.Show();window.StartForTest(entries.Single(e=>e.Manifest.Id=="2048"));string folder=Path.Combine(data,"scores","2048");Directory.CreateDirectory(folder);string sessionFile=Path.Combine(folder,"session.json");Directory.CreateDirectory(sessionFile);IMiniGame original=window.ActiveGame;
                Check(!window.SaveActiveSession(),"save failure ignored");window.ShowCatalog();Check(Object.ReferenceEquals(original,window.ActiveGame),"failed save discarded board");window.Close();Check(window.IsVisible && Object.ReferenceEquals(original,window.ActiveGame),"failed save closed game");Directory.Delete(sessionFile);window.Close();Check(window.ActiveGame==null && File.Exists(sessionFile),"retry close did not persist game");
            });
            test("normal-window-close-and-reopen-resumes-active-board",delegate{
                string data=Path.Combine(output,"window-restart");MiniGameEntry entry=entries.Single(e=>e.Manifest.Id=="solitaire");MiniGameWindow first=new MiniGameWindow(root,data,null,false){Opacity=0,ShowActivated=false};first.Show();first.StartForTest(entry);Mutate("solitaire",first.ActiveGame);string before=Snapshot(first.ActiveGame);first.Close();MiniGameWindow second=new MiniGameWindow(root,data,null,false){Opacity=0,ShowActivated=false};second.Show();second.StartForTest(entry);Check(Snapshot(second.ActiveGame)==before,"closing window lost session");second.Close();
            });
            test("session-thirteen-games-survive-real-process-exit-and-restart",delegate{
                string fixture=Path.Combine(output,"process-restart");foreach(string phase in new[]{"write","read"}){ProcessStartInfo start=new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"--session-fixture \""+fixture+"\" "+phase){UseShellExecute=false,CreateNoWindow=true};using(Process process=Process.Start(start)){if(!process.WaitForExit(30000)){process.Kill();throw new IOException("cross-process fixture timed out");}Check(process.ExitCode==0,"cross-process fixture failed "+phase);}}Check(File.ReadAllText(Path.Combine(fixture,"read.txt")).Contains("PASS 13"),"restart proof missing");
            });
        }
        public static int Fixture(string folder,string phase)
        {
            if(phase!="write" && phase!="read")throw new ArgumentException("unknown fixture phase");if(phase=="write" && Directory.Exists(folder))throw new IOException("fixture already exists");Directory.CreateDirectory(folder);
            string root=Path.Combine(folder,"runtime"),data=Path.Combine(folder,"data");Directory.CreateDirectory(root);MiniGameCatalog catalog=new MiniGameCatalog(root,data);List<MiniGameEntry> entries=catalog.Scan();Check(entries.Count==13,"fixture missing games");
            foreach(MiniGameEntry entry in entries){List<string> notices=new List<string>();using(IMiniGame game=catalog.Create(entry,null,notices.Add)){string expected=Path.Combine(folder,entry.Manifest.Id+".expected.json");if(phase=="write"){Mutate(entry.Manifest.Id,game);File.WriteAllText(expected,Snapshot(game));((IMiniGameSession)game).SaveSession();}else{Check(notices.Any(t=>t.StartsWith("已继续")),entry.Manifest.Id+": "+String.Join(";",notices));Check(Snapshot(game)==File.ReadAllText(expected),"process restart changed "+entry.Manifest.Id);}}}
            File.WriteAllText(Path.Combine(folder,phase+".txt"),"PASS 13 games "+phase);return 0;
        }
    }
}
