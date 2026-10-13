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
    public sealed class NetState{public int Size,Moves;public int[] Pipes;public bool[] Locked;}
    public sealed class NetBoard
    {
        public readonly int Size;public readonly int[] Pipes;public readonly bool[] Locked;public int Moves{get;private set;}
        private static readonly int[] DX={0,1,0,-1},DY={-1,0,1,0};
        public NetBoard(int size,Random random)
        {
            if(size<2 || size>12)throw new ArgumentOutOfRangeException("size");Size=size;Pipes=new int[size*size];Locked=new bool[Pipes.Length];
            bool[] visited=new bool[Pipes.Length];Stack<int> stack=new Stack<int>();stack.Push(Pipes.Length/2);visited[stack.Peek()]=true;
            while(stack.Count>0){int p=stack.Peek();int[] choices=Enumerable.Range(0,4).Where(d=>Neighbor(p,d)>=0 && !visited[Neighbor(p,d)]).ToArray();if(choices.Length==0){stack.Pop();continue;}int dir=choices[random.Next(choices.Length)],q=Neighbor(p,dir);Pipes[p]|=1<<dir;Pipes[q]|=1<<((dir+2)%4);visited[q]=true;stack.Push(q);}
            for(int i=0;i<Pipes.Length;i++)for(int rotation=random.Next(4);rotation>0;rotation--)Pipes[i]=Rotated(Pipes[i]);
            if(Won){int i=Array.FindIndex(Pipes,p=>p!=15);Pipes[i]=Rotated(Pipes[i]);}
        }
        private static int Rotated(int value){return ((value<<1)&15)|(value>>3);}
        private int Neighbor(int p,int d){int x=p%Size+DX[d],y=p/Size+DY[d];return x<0 || y<0 || x>=Size || y>=Size?-1:y*Size+x;}
        public bool[] Connected(){bool[] found=new bool[Pipes.Length];Queue<int> queue=new Queue<int>();int source=Pipes.Length/2;found[source]=true;queue.Enqueue(source);while(queue.Count>0){int p=queue.Dequeue();for(int d=0;d<4;d++){int q=Neighbor(p,d);if(q>=0 && (Pipes[p]&(1<<d))!=0 && (Pipes[q]&(1<<((d+2)%4)))!=0 && !found[q]){found[q]=true;queue.Enqueue(q);}}}return found;}
        public bool Won{get{return Connected().All(v=>v) && !Enumerable.Range(0,Pipes.Length).Any(p=>Enumerable.Range(0,4).Any(d=>(Pipes[p]&(1<<d))!=0 && (Neighbor(p,d)<0 || (Pipes[Neighbor(p,d)]&(1<<((d+2)%4)))==0)));}}
        public bool Rotate(int index){if(index<0 || index>=Pipes.Length || Locked[index] || Won)return false;Pipes[index]=Rotated(Pipes[index]);Moves++;return true;}
        public void ToggleLock(int index){if(index>=0 && index<Locked.Length && !Won)Locked[index]=!Locked[index];}
        public void Restore(NetState state){MiniGameSession.Require(state.Size==Size && MiniGameSession.Range(state.Pipes,Pipes.Length,1,15) && state.Locked!=null && state.Locked.Length==Pipes.Length && state.Moves>=0 && state.Pipes.Sum(p=>Enumerable.Range(0,4).Count(d=>(p&(1<<d))!=0))==2*(Pipes.Length-1));Array.Copy(state.Pipes,Pipes,Pipes.Length);Array.Copy(state.Locked,Locked,Locked.Length);Moves=state.Moves;}
    }
    public sealed class NetLightsGame:MiniGameControl
    {
        public NetBoard Board{get;private set;}private ComboBox difficulty;private UniformGrid tiles;private TextBlock score,message;private bool paused,recorded;
        protected override UIElement Build(){Grid layout=new Grid();foreach(GridLength row in new[]{GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto})layout.RowDefinitions.Add(new RowDefinition{Height=row});WrapPanel bar=new WrapPanel();difficulty=new ComboBox{ItemsSource=new[]{"轻松 5×5","标准 7×7","挑战 9×9"},SelectedIndex=0,Width=105,Margin=new Thickness(0,0,6,5)};bar.Children.Add(difficulty);bar.Children.Add(Ui.Button("新一局",NewGame,false));layout.Children.Add(bar);score=Ui.Text("",12,Ui.Muted);Grid.SetRow(score,1);layout.Children.Add(score);tiles=new UniformGrid{Width=360,Height=360};Viewbox box=new Viewbox{Child=tiles,Stretch=Stretch.Uniform,Margin=new Thickness(0,8,0,8)};Grid.SetRow(box,2);layout.Children.Add(box);message=Ui.Text("",11,Ui.Muted);Grid.SetRow(message,3);layout.Children.Add(message);NewGame();return layout;}
        private void NewGame(){NewSession();Board=new NetBoard(new[]{5,7,9}[Math.Max(0,difficulty.SelectedIndex)],new Random());recorded=false;Paint();}
        private void Rotate(int index){if(paused || !Board.Rotate(index))return;if(Board.Won && !recorded){recorded=true;try{Host.Record("moves-"+Board.Size,Board.Moves,true);}catch(Exception ex){Host.Notify("成绩未保存："+ex.Message);}}Paint();}
        private void Paint(){bool[] lit=Board.Connected();tiles.Rows=tiles.Columns=Board.Size;tiles.Children.Clear();for(int i=0;i<Board.Pipes.Length;i++){int index=i;Canvas pipe=new Canvas{Width=36,Height=36};Brush color=lit[i]?Ui.Gold:Ui.Color("#A3B0A8");for(int d=0;d<4;d++)if((Board.Pipes[i]&(1<<d))!=0){Rectangle arm=new Rectangle{Width=d%2==0?4:18,Height=d%2==0?18:4,Fill=color};Canvas.SetLeft(arm,d==3?0:d==1?18:16);Canvas.SetTop(arm,d==0?0:d==2?18:16);pipe.Children.Add(arm);}Ellipse bulb=new Ellipse{Width=i==Board.Pipes.Length/2?14:9,Height=i==Board.Pipes.Length/2?14:9,Fill=lit[i]?Ui.Red:color};Canvas.SetLeft(bulb,18-bulb.Width/2);Canvas.SetTop(bulb,18-bulb.Height/2);pipe.Children.Add(bulb);Button tile=Ui.Button("",delegate{Rotate(index);},false);tile.Tag=index;tile.Content=new Viewbox{Child=pipe,Stretch=Stretch.Fill};tile.Padding=new Thickness(0);tile.Margin=new Thickness(1);tile.HorizontalAlignment=HorizontalAlignment.Stretch;tile.VerticalAlignment=VerticalAlignment.Stretch;tile.HorizontalContentAlignment=HorizontalAlignment.Stretch;tile.VerticalContentAlignment=VerticalAlignment.Stretch;tile.Background=Board.Locked[i]?Ui.Color("#D9E4D9"):lit[i]?Ui.Color("#F2E6C8"):Ui.Cream;tile.ToolTip=Board.Locked[i]?"已锁定，右键解锁":"左键旋转，右键锁定";tile.IsEnabled=!paused && !Board.Won;tile.MouseRightButtonUp+=delegate(object sender,System.Windows.Input.MouseButtonEventArgs e){if(!paused){Board.ToggleLock(index);Paint();}e.Handled=true;};tiles.Children.Add(tile);}int best=Host.Best("moves-"+Board.Size,0);score.Text="点亮 "+lit.Count(v=>v)+"/"+lit.Length+" · 旋转 "+Board.Moves+" · 最佳 "+(best==0?"—":best.ToString());message.Text=Board.Won?"所有莲灯都亮了，风火相连！":paused?"已暂停":"左键旋转线路，接通中心莲火与全部灯；右键锁定已确定的格子。";}
        public override bool CanResume{get{return true;}}protected override object CaptureSession(){return Board.Won?null:new NetState{Size=Board.Size,Moves=Board.Moves,Pipes=Board.Pipes,Locked=Board.Locked};}protected override void RestoreSession(string json){NetState state=MiniGameSession.Read<NetState>(json);MiniGameSession.Require(new[]{5,7,9}.Contains(state.Size));NetBoard restored=new NetBoard(state.Size,new Random());restored.Restore(state);Board=restored;difficulty.SelectedIndex=Array.IndexOf(new[]{5,7,9},state.Size);Paint();}public override void SetHostPaused(bool value){paused=value;if(Board!=null)Paint();}public override void Dispose(){}
    }
}
