using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using NezhaDesktop;
using NezhaDesktop.MiniGames;
namespace NezhaGames
{
    public sealed class SamePosition{public int[] Cells;public int Score,Moves;}
    public sealed class SameState{public int Tier;public SamePosition Current;public SamePosition[] Undo;}
    public sealed class SameBoard
    {
        public readonly int Width,Height,Colors;public readonly int[] Cells;public int Score{get;private set;}public int Moves{get;private set;}
        private readonly List<SamePosition> history=new List<SamePosition>();
        public bool Won{get{return Cells.All(v=>v==0);}}public bool Ended{get{return !Enumerable.Range(0,Cells.Length).Any(i=>Cells[i]>0 && ((i%Width<Width-1 && Cells[i]==Cells[i+1]) || (i/Width<Height-1 && Cells[i]==Cells[i+Width])));}}
        public bool CanUndo{get{return history.Count>0;}}
        public SameBoard(int width,int height,int colors,Random random){Width=width;Height=height;Colors=colors;Cells=Enumerable.Range(0,width*height).Select(i=>1+random.Next(colors)).ToArray();Cells[1]=Cells[0];}
        public int[] Region(int index){if(index<0 || index>=Cells.Length || Cells[index]==0)return new int[0];HashSet<int> found=new HashSet<int>();Queue<int> queue=new Queue<int>();queue.Enqueue(index);found.Add(index);while(queue.Count>0){int p=queue.Dequeue();foreach(int q in new[]{p%Width>0?p-1:-1,p%Width<Width-1?p+1:-1,p>=Width?p-Width:-1,p+Width<Cells.Length?p+Width:-1})if(q>=0 && Cells[q]==Cells[index] && found.Add(q))queue.Enqueue(q);}return found.ToArray();}
        private SamePosition Position(){return new SamePosition{Cells=(int[])Cells.Clone(),Score=Score,Moves=Moves};}
        public bool Remove(int index)
        {
            int[] region=Region(index);if(region.Length<2)return false;history.Add(Position());if(history.Count>20)history.RemoveAt(0);foreach(int i in region)Cells[i]=0;Score+= (region.Length-1)*(region.Length-1);Moves++;
            for(int x=0;x<Width;x++){int[] remaining=Enumerable.Range(0,Height).Select(y=>Cells[y*Width+x]).Where(v=>v>0).ToArray();for(int y=0;y<Height;y++)Cells[y*Width+x]=y>=Height-remaining.Length?remaining[y-(Height-remaining.Length)]:0;}
            int target=0;for(int x=0;x<Width;x++){if(Cells[(Height-1)*Width+x]==0)continue;if(x!=target)for(int y=0;y<Height;y++){Cells[y*Width+target]=Cells[y*Width+x];Cells[y*Width+x]=0;}target++;}if(Won)Score+=1000;return true;
        }
        public bool Undo(){if(!CanUndo)return false;SamePosition p=history[history.Count-1];history.RemoveAt(history.Count-1);Array.Copy(p.Cells,Cells,Cells.Length);Score=p.Score;Moves=p.Moves;return true;}
        private void Validate(SamePosition p){MiniGameSession.Require(p!=null && MiniGameSession.Range(p.Cells,Cells.Length,0,Colors) && p.Score>=0 && p.Score<1000000 && p.Moves>=0 && p.Moves<=Cells.Length/2);}
        public SameState Capture(int tier){return new SameState{Tier=tier,Current=Position(),Undo=history.ToArray()};}
        public void Restore(SameState state){Validate(state.Current);MiniGameSession.Require(state.Undo!=null && state.Undo.Length<=20);foreach(SamePosition p in state.Undo)Validate(p);Array.Copy(state.Current.Cells,Cells,Cells.Length);Score=state.Current.Score;Moves=state.Current.Moves;history.Clear();history.AddRange(state.Undo);}
    }
    public sealed class SameGame:MiniGameControl
    {
        public SameBoard Board{get;private set;}private ComboBox difficulty;private UniformGrid tiles;private TextBlock score,message;private int tier;private bool paused;
        protected override UIElement Build(){Grid layout=new Grid();foreach(GridLength row in new[]{GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto})layout.RowDefinitions.Add(new RowDefinition{Height=row});WrapPanel bar=new WrapPanel();difficulty=new ComboBox{ItemsSource=new[]{"轻松 · 三色","标准 · 四色","挑战 · 五色"},SelectedIndex=0,Width=105,Margin=new Thickness(0,0,6,5)};bar.Children.Add(difficulty);bar.Children.Add(Ui.Button("新一局",NewGame,false));bar.Children.Add(Ui.Button("撤销",Undo,false));layout.Children.Add(bar);score=Ui.Text("",12,Ui.Muted);Grid.SetRow(score,1);layout.Children.Add(score);tiles=new UniformGrid();Viewbox box=new Viewbox{Child=tiles,Stretch=Stretch.Uniform,Margin=new Thickness(0,8,0,8)};Grid.SetRow(box,2);layout.Children.Add(box);message=Ui.Text("",11,Ui.Muted);Grid.SetRow(message,3);layout.Children.Add(message);NewGame();return layout;}
        private SameBoard Create(int t){return new SameBoard(t==0?8:10,t==2?12:t==0?8:10,3+t,new Random());}
        private void NewGame(){NewSession();tier=Math.Max(0,difficulty.SelectedIndex);Board=Create(tier);Paint();}
        private void Remove(int i){if(paused || !Board.Remove(i))return;if(Board.Ended)try{Host.Record("score-"+tier,Board.Score,false);}catch(Exception ex){Host.Notify("成绩未保存："+ex.Message);}Paint();}
        private void Undo(){if(!paused && Board.Undo())Paint();}
        private void Paint(){string[] colors={"#E8E1D5","#B8554C","#C9A14F","#5D907D","#6489AD","#9578AA"};string[] symbols={"","火","莲","叶","云","星"};tiles.Rows=Board.Height;tiles.Columns=Board.Width;tiles.Width=Board.Width*36;tiles.Height=Board.Height*36;tiles.Children.Clear();for(int i=0;i<Board.Cells.Length;i++){int index=i,v=Board.Cells[i];Button tile=Ui.Button(symbols[v],delegate{Remove(index);},false);tile.Tag=index;tile.Background=Ui.Color(colors[v]);tile.Foreground=Brushes.White;tile.FontSize=16;tile.Margin=new Thickness(1.5);tile.Padding=new Thickness(0);tile.HorizontalAlignment=HorizontalAlignment.Stretch;tile.VerticalAlignment=VerticalAlignment.Stretch;tile.IsEnabled=!paused && v!=0;tiles.Children.Add(tile);}score.Text="本局 "+Board.Score+" · 最佳 "+Host.Best("score-"+tier,0)+" · 消除 "+Board.Moves+" 次";message.Text=paused?"已暂停":Board.Won?"全部消除，奖励 1000 分！可撤销再尝试。":Board.Ended?"没有相邻同色了，可撤销或新开一局。":"点两块以上相邻同色，一大片得分更高；可撤销最近20步。";}
        public override bool CanResume{get{return true;}}protected override object CaptureSession(){return Board.Ended?null:Board.Capture(tier);}protected override void RestoreSession(string json){SameState state=MiniGameSession.Read<SameState>(json);MiniGameSession.Require(state.Tier>=0 && state.Tier<3);SameBoard restored=Create(state.Tier);restored.Restore(state);Board=restored;tier=state.Tier;difficulty.SelectedIndex=tier;Paint();}
        public override void SetHostPaused(bool value){paused=value;if(Board!=null)Paint();}public override void Dispose(){}
    }
}
