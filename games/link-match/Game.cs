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
    public sealed class LinkState { public int Tier, Attempts, Hints, Shuffles; public int[] Cells; }
    public sealed class LinkBoard
    {
        public readonly int Width, Height;
        public readonly int[] Cells;
        public int Attempts { get; private set; }
        public bool Won { get { return Cells.All(v=>v==0); } }
        public LinkBoard(int width,int height,Random random) { Width=width; Height=height; Cells=new int[width*height]; Generate(random); }
        // Border cells are empty: paths can travel around the outside of the board.
        public bool CanConnect(int a,int b)
        {
            if (a==b || a<0 || b<0 || a>=Cells.Length || b>=Cells.Length) return false;
            int w=Width+2,h=Height+2,start=(a/Width+1)*w+a%Width+1,target=(b/Width+1)*w+b%Width+1;
            int[] best=Enumerable.Repeat(3,w*h*4).ToArray(); Queue<int[]> queue=new Queue<int[]>();
            int[] dx={1,0,-1,0},dy={0,1,0,-1};
            for(int d=0;d<4;d++) { best[start*4+d]=0; queue.Enqueue(new[]{start,d,0}); }
            while(queue.Count>0) {
                int[] node=queue.Dequeue(); if(node[2]!=best[node[0]*4+node[1]]) continue;
                for(int d=0;d<4;d++) { int turns=node[2]+(d==node[1]?0:1); if(turns>2) continue;
                    int x=node[0]%w+dx[d],y=node[0]/w+dy[d]; if(x<0 || y<0 || x>=w || y>=h) continue; int p=y*w+x;
                    if(p==target) return true;
                    if(x>0 && x<w-1 && y>0 && y<h-1 && Cells[(y-1)*Width+x-1]!=0) continue;
                    if(turns<best[p*4+d]) { best[p*4+d]=turns; queue.Enqueue(new[]{p,d,turns}); }
                }
            } return false;
        }
        private void Generate(Random random)
        {
            // Build backwards from an empty board; reverse removal order is a solution.
            for(int retry=0;retry<30;retry++) {
                Array.Clear(Cells,0,Cells.Length); bool complete=true;
                for(int pair=0;pair<Cells.Length/2;pair++) {
                    int[] empty=Enumerable.Range(0,Cells.Length).Where(i=>Cells[i]==0).OrderBy(i=>random.Next()).ToArray(); int first=-1,second=-1;
                    foreach(int a in empty) { foreach(int b in empty) if(a!=b && CanConnect(a,b)) { first=a; second=b; break; } if(first>=0) break; }
                    if(first<0) { complete=false; break; } Cells[first]=Cells[second]=1+random.Next(12);
                } if(complete) return;
            }
            for(int i=0;i<Cells.Length;i+=2) Cells[i]=Cells[i+1]=1+random.Next(12);
        }
        public int[] FindPair()
        { for(int a=0;a<Cells.Length;a++) if(Cells[a]!=0) for(int b=a+1;b<Cells.Length;b++) if(Cells[a]==Cells[b] && CanConnect(a,b)) return new[]{a,b}; return null; }
        public bool Match(int a,int b)
        { if(Won || a==b || a<0 || b<0 || a>=Cells.Length || b>=Cells.Length || Cells[a]==0 || Cells[b]==0) return false; Attempts++; if(Cells[a]!=Cells[b] || !CanConnect(a,b)) return false; Cells[a]=Cells[b]=0; return true; }
        public void Shuffle(Random random)
        {
            int[] remaining=Cells.Where(v=>v>0).ToArray();
            for(int tries=0;tries<100;tries++) { for(int i=remaining.Length-1;i>0;i--) {int j=random.Next(i+1),old=remaining[i];remaining[i]=remaining[j];remaining[j]=old;} Array.Clear(Cells,0,Cells.Length); Array.Copy(remaining,Cells,remaining.Length); if(FindPair()!=null) return; }
            // Even counts permit adjacent pairs, so the fallback always has a move.
            int index=0; foreach(var group in remaining.GroupBy(v=>v)) for(int i=0;i<group.Count();i++) Cells[index++]=group.Key;
        }
        public void Restore(LinkState state)
        { MiniGameSession.Require(MiniGameSession.Range(state.Cells,Cells.Length,0,12) && state.Cells.Where(v=>v>0).GroupBy(v=>v).All(g=>g.Count()%2==0) && state.Attempts>=0); Array.Copy(state.Cells,Cells,Cells.Length);Attempts=state.Attempts; }
    }
    public sealed class LinkMatchGame : MiniGameControl
    {
        public LinkBoard Board {get;private set;}
        private ComboBox difficulty; private UniformGrid tiles; private TextBlock score,message;
        private int tier,selected=-1,hintA=-1,hintB=-1,hints,shuffles; private bool paused,recorded;
        protected override UIElement Build()
        {
            Grid layout=new Grid(); foreach(GridLength row in new[]{GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto}) layout.RowDefinitions.Add(new RowDefinition{Height=row});
            WrapPanel bar=new WrapPanel(); difficulty=new ComboBox{ItemsSource=new[]{"入门 6×6","标准 8×6","挑战 10×8"},SelectedIndex=0,Width=104,Margin=new Thickness(0,0,6,5)};
            bar.Children.Add(difficulty);bar.Children.Add(Ui.Button("新一局",NewGame,false));bar.Children.Add(Ui.Button("提示",Hint,false));bar.Children.Add(Ui.Button("洗牌",Shuffle,false));layout.Children.Add(bar);
            score=Ui.Text("",12,Ui.Muted);Grid.SetRow(score,1);layout.Children.Add(score);tiles=new UniformGrid();Viewbox box=new Viewbox{Child=tiles,Stretch=Stretch.Uniform,Margin=new Thickness(0,8,0,8)};Grid.SetRow(box,2);layout.Children.Add(box);
            message=Ui.Text("",11,Ui.Muted);Grid.SetRow(message,3);layout.Children.Add(message);NewGame();return layout;
        }
        private void NewGame(){NewSession();tier=Math.Max(0,difficulty.SelectedIndex);Board=new LinkBoard(new[]{6,8,10}[tier],tier==2?8:6,new Random());selected=hintA=hintB=-1;hints=shuffles=0;recorded=false;Paint();}
        private void Choose(int index)
        {
            if(paused || Board.Won || Board.Cells[index]==0)return;hintA=hintB=-1;
            if(selected<0){selected=index;Paint();return;}if(selected==index){selected=-1;Paint();return;}
            bool matched=Board.Match(selected,index);selected=matched?-1:index;Paint();
            if(Board.Won && !recorded){recorded=true;if(hints==0 && shuffles==0)try{Host.Record("attempts-"+tier,Board.Attempts,true);}catch(Exception ex){Host.Notify("成绩未保存："+ex.Message);}}
            else if(Board.FindPair()==null)Host.Notify("暂时没有可连的一对，可以洗牌继续。");
        }
        private void Hint(){if(paused || Board.Won)return;int[] pair=Board.FindPair();if(pair==null){Host.Notify("没有可连的一对，请洗牌。");return;}hintA=pair[0];hintB=pair[1];hints++;Paint();}
        private void Shuffle(){if(paused || Board.Won)return;if(shuffles>=3){Host.Notify("本局三次洗牌已用完，可以新开一局。");return;}Board.Shuffle(new Random());shuffles++;selected=hintA=hintB=-1;Paint();}
        private void Paint()
        {
            string[] labels={"","莲","火","风","轮","绫","桃","糖","云","星","月","雷","山"};tiles.Rows=Board.Height;tiles.Columns=Board.Width;tiles.Width=Board.Width*42;tiles.Height=Board.Height*42;tiles.Children.Clear();
            for(int i=0;i<Board.Cells.Length;i++){int index=i;Button tile=Ui.Button(labels[Board.Cells[i]],delegate{Choose(index);},false);tile.Tag=index;tile.Margin=new Thickness(2);tile.Padding=new Thickness(0);tile.FontSize=22;tile.HorizontalAlignment=HorizontalAlignment.Stretch;tile.VerticalAlignment=VerticalAlignment.Stretch;tile.IsEnabled=!paused && Board.Cells[i]>0 && !Board.Won;tile.Background=Board.Cells[i]==0?Ui.Color("#E8E1D5"):i==selected || i==hintA || i==hintB?Ui.Color("#EED39B"):Ui.Cream;tile.Foreground=Board.Cells[i]%2==0?Ui.Red:Ui.Color("#527D72");tiles.Children.Add(tile);}
            score.Text="剩余 "+Board.Cells.Count(v=>v>0)/2+" 对 · 尝试 "+Board.Attempts+" · 洗牌 "+shuffles+"/3";
            message.Text=Board.Won?"莲宝全部归位！使用辅助的本局不计最佳。":paused?"已暂停":"点两枚相同莲宝，连线最多转两次弯，可绕棋盘外侧。";
        }
        public override bool CanResume{get{return true;}}
        protected override object CaptureSession(){return Board.Won?null:new LinkState{Tier=tier,Attempts=Board.Attempts,Hints=hints,Shuffles=shuffles,Cells=Board.Cells};}
        protected override void RestoreSession(string json){LinkState state=MiniGameSession.Read<LinkState>(json);MiniGameSession.Require(state.Tier>=0 && state.Tier<3 && state.Hints>=0 && state.Shuffles>=0 && state.Shuffles<=3);LinkBoard restored=new LinkBoard(new[]{6,8,10}[state.Tier],state.Tier==2?8:6,new Random());restored.Restore(state);Board=restored;tier=state.Tier;difficulty.SelectedIndex=tier;hints=state.Hints;shuffles=state.Shuffles;selected=hintA=hintB=-1;Paint();}
        public override void SetHostPaused(bool value){paused=value;if(Board!=null)Paint();}
        public override void Dispose(){}
    }
}
