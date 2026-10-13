using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NezhaDesktop;
using NezhaDesktop.MiniGames;
namespace NezhaGames
{
    public sealed class SolitairePosition{public int[] Stock,Waste,Hidden,Foundation;public int[][] Columns;public int Moves;}
    public sealed class SolitaireState{public int DrawCount;public SolitairePosition Current;public SolitairePosition[] Undo;}
    public sealed class SolitaireBoard
    {
        public readonly List<int> Stock=new List<int>(),Waste=new List<int>();public readonly List<int>[] Columns=Enumerable.Range(0,7).Select(i=>new List<int>()).ToArray();public readonly int[] Hidden=new int[7],Foundation=new int[4];
        private readonly List<SolitairePosition> history=new List<SolitairePosition>();public readonly int DrawCount;public int Moves{get;private set;}public bool Won{get{return Foundation.Sum()==52;}}public bool CanUndo{get{return history.Count>0;}}
        public static int Rank(int card){return card%13+1;}public static bool Red(int card){return card/13==1 || card/13==2;}
        public SolitaireBoard(int drawCount,Random random){if(drawCount!=1 && drawCount!=3)throw new ArgumentException("翻牌模式无效。");DrawCount=drawCount;int[] cards=Enumerable.Range(0,52).ToArray();for(int i=51;i>0;i--){int j=random.Next(i+1),old=cards[i];cards[i]=cards[j];cards[j]=old;}int at=0;for(int c=0;c<7;c++){for(int i=0;i<=c;i++)Columns[c].Add(cards[at++]);Hidden[c]=c;}Stock.AddRange(cards.Skip(at));}
        public SolitairePosition Position(){return new SolitairePosition{Stock=Stock.ToArray(),Waste=Waste.ToArray(),Hidden=(int[])Hidden.Clone(),Foundation=(int[])Foundation.Clone(),Columns=Columns.Select(c=>c.ToArray()).ToArray(),Moves=Moves};}
        private void Remember(){history.Add(Position());if(history.Count>20)history.RemoveAt(0);Moves++;}
        public bool Draw(){if(Won || Stock.Count==0 && Waste.Count==0)return false;Remember();if(Stock.Count==0){Stock.AddRange(Waste.AsEnumerable().Reverse());Waste.Clear();}else for(int n=0;n<DrawCount && Stock.Count>0;n++){Waste.Add(Stock[Stock.Count-1]);Stock.RemoveAt(Stock.Count-1);}return true;}
        private List<int> Source(int column){return column==-1?Waste:column>=0 && column<7?Columns[column]:null;}
        private bool Selectable(int source,int index){List<int> cards=Source(source);return cards!=null && index>=0 && index<cards.Count && (source==-1?index==cards.Count-1:index>=Hidden[source]);}
        public bool CanMove(int source,int index,int target)
        {
            if(Won || target<0 || target>=7 || source==target || !Selectable(source,index))return false;List<int> cards=Source(source);int card=cards[index];
            for(int i=index+1;i<cards.Count;i++)if(Rank(cards[i])!=Rank(cards[i-1])-1 || Red(cards[i])==Red(cards[i-1]))return false;
            return Columns[target].Count==0?Rank(card)==13:Rank(Columns[target].Last())==Rank(card)+1 && Red(Columns[target].Last())!=Red(card);
        }
        private void Reveal(int source){if(source>=0 && Columns[source].Count>0 && Hidden[source]==Columns[source].Count)Hidden[source]--;else if(source>=0 && Columns[source].Count==0)Hidden[source]=0;}
        public bool Move(int source,int index,int target){if(!CanMove(source,index,target))return false;Remember();List<int> cards=Source(source);Columns[target].AddRange(cards.Skip(index));cards.RemoveRange(index,cards.Count-index);Reveal(source);return true;}
        public bool CanFoundation(int source,int index){if(Won || !Selectable(source,index))return false;List<int> cards=Source(source);return index==cards.Count-1 && Rank(cards[index])==Foundation[cards[index]/13]+1;}
        public bool ToFoundation(int source,int index){if(!CanFoundation(source,index))return false;Remember();List<int> cards=Source(source);int card=cards[index];Foundation[card/13]++;cards.RemoveAt(index);Reveal(source);return true;}
        public bool Undo(){if(!CanUndo)return false;SolitairePosition position=history.Last();history.RemoveAt(history.Count-1);Assign(position);return true;}
        private void Validate(SolitairePosition position)
        {
            MiniGameSession.Require(position!=null && position.Stock!=null && position.Waste!=null && position.Columns!=null && position.Columns.Length==7 && position.Columns.All(c=>c!=null && c.Length<=52) && MiniGameSession.Range(position.Hidden,7,0,6) && MiniGameSession.Range(position.Foundation,4,0,13) && position.Stock.Length<=52 && position.Waste.Length<=52 && position.Moves>=0);
            int[] all=position.Stock.Concat(position.Waste).Concat(position.Columns.SelectMany(c=>c)).Concat(Enumerable.Range(0,4).SelectMany(s=>Enumerable.Range(0,position.Foundation[s]).Select(rank=>s*13+rank))).ToArray();MiniGameSession.Require(all.Length==52 && all.All(v=>v>=0 && v<52) && all.Distinct().Count()==52);
            for(int c=0;c<7;c++){MiniGameSession.Require(position.Columns[c].Length==0?position.Hidden[c]==0:position.Hidden[c]<position.Columns[c].Length);for(int i=position.Hidden[c]+1;i<position.Columns[c].Length;i++)MiniGameSession.Require(Rank(position.Columns[c][i])==Rank(position.Columns[c][i-1])-1 && Red(position.Columns[c][i])!=Red(position.Columns[c][i-1]));}
        }
        private void Assign(SolitairePosition p){Stock.Clear();Stock.AddRange(p.Stock);Waste.Clear();Waste.AddRange(p.Waste);for(int i=0;i<7;i++){Columns[i].Clear();Columns[i].AddRange(p.Columns[i]);Hidden[i]=p.Hidden[i];}Array.Copy(p.Foundation,Foundation,4);Moves=p.Moves;}
        public SolitaireState Capture(){return new SolitaireState{DrawCount=DrawCount,Current=Position(),Undo=history.ToArray()};}
        public void Restore(SolitaireState state){Validate(state.Current);MiniGameSession.Require(state.DrawCount==DrawCount && state.Undo!=null && state.Undo.Length<=20);foreach(SolitairePosition p in state.Undo)Validate(p);Assign(state.Current);history.Clear();history.AddRange(state.Undo);}
        public int[] Hint(){for(int from=-1;from<7;from++){List<int> cards=Source(from);if(cards.Count==0)continue;int start=from<0?cards.Count-1:Hidden[from];for(int i=cards.Count-1;i>=start;i--){if(CanFoundation(from,i))return new[]{from,i,7+cards[i]/13};for(int to=0;to<7;to++)if(CanMove(from,i,to) && !(from>=0 && i==0 && Columns[to].Count==0))return new[]{from,i,to};}}return null;}
    }
    public sealed class SolitaireGame:MiniGameControl
    {
        public SolitaireBoard Board{get;private set;}private ComboBox difficulty;private Canvas table;private TextBlock score,message;private int selectedSource=-2,selectedIndex=-1,hintTarget=-1;private bool paused,recorded;
        protected override UIElement Build(){Grid layout=new Grid();foreach(GridLength row in new[]{GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto})layout.RowDefinitions.Add(new RowDefinition{Height=row});WrapPanel bar=new WrapPanel();difficulty=new ComboBox{ItemsSource=new[]{"翻一张","翻三张"},SelectedIndex=0,Width=76,Margin=new Thickness(0,0,6,5)};bar.Children.Add(difficulty);bar.Children.Add(Ui.Button("新一局",NewGame,false));bar.Children.Add(Ui.Button("撤销",Undo,false));bar.Children.Add(Ui.Button("提示",Hint,false));layout.Children.Add(bar);score=Ui.Text("",12,Ui.Muted);Grid.SetRow(score,1);layout.Children.Add(score);table=new Canvas{Width=360,Height=460,Background=Ui.Color("#E5EBDD")};Viewbox box=new Viewbox{Child=table,Stretch=Stretch.Uniform,Margin=new Thickness(0,8,0,8)};Grid.SetRow(box,2);layout.Children.Add(box);message=Ui.Text("",11,Ui.Muted);Grid.SetRow(message,3);layout.Children.Add(message);NewGame();return layout;}
        private void ClearSelection(){selectedSource=-2;selectedIndex=-1;hintTarget=-1;}
        private void NewGame(){NewSession();Board=new SolitaireBoard(difficulty.SelectedIndex==1?3:1,new Random());ClearSelection();recorded=false;Paint();}
        private void Draw(){if(paused || !Board.Draw())return;ClearSelection();Paint();}
        private void Undo(){if(paused || !Board.Undo())return;ClearSelection();recorded=false;Paint();}
        private void Choose(int source,int index)
        {
            if(paused || Board.Won)return;
            if(selectedSource!=-2 && source>=0 && Board.Move(selectedSource,selectedIndex,source)){ClearSelection();Paint();return;}
            List<int> cards=source==-1?Board.Waste:source>=0?Board.Columns[source]:null;
            if(cards!=null && index>=0 && index<cards.Count && (source==-1?index==cards.Count-1:index>=Board.Hidden[source])){if(selectedSource==source && selectedIndex==index)ClearSelection();else{selectedSource=source;selectedIndex=index;hintTarget=-1;}}Paint();
        }
        private void Foundation(int suit)
        {
            if(paused || selectedSource==-2)return;List<int> cards=selectedSource==-1?Board.Waste:Board.Columns[selectedSource];if(selectedIndex>=cards.Count || cards[selectedIndex]/13!=suit || !Board.ToFoundation(selectedSource,selectedIndex)){Host.Notify("收牌从 A 开始，同花色依次向上；只能收最上面的牌。");return;}ClearSelection();if(Board.Won && !recorded){recorded=true;try{Host.Record("moves-"+Board.DrawCount,Board.Moves,true);}catch(Exception ex){Host.Notify("成绩未保存："+ex.Message);}}Paint();
        }
        private void Hint(){if(paused || Board.Won)return;int[] hint=Board.Hint();if(hint==null){Host.Notify(Board.Stock.Count+Board.Waste.Count>0?"可以点击左上角牌堆翻牌，或撤销试一条路线。":"暂无可移动的牌，可以撤销或新开一局。");return;}selectedSource=hint[0];selectedIndex=hint[1];hintTarget=hint[2];Paint();Host.Notify(hintTarget>=7?"选中的牌可以收进高亮花色。":"选中的牌组可以移到高亮列。");}
        private string Label(int card){string[] ranks={"A","2","3","4","5","6","7","8","9","10","J","Q","K"};return ranks[card%13]+new[]{"♠","♥","♦","♣"}[card/13];}
        private Button Card(string label,double x,double y,Brush background,Brush foreground,Action action)
        {Button card=Ui.Button(label,action,false);card.Width=47;card.Height=62;card.Padding=new Thickness(3,2,0,0);card.FontSize=16;card.FontWeight=FontWeights.SemiBold;card.HorizontalContentAlignment=HorizontalAlignment.Left;card.VerticalContentAlignment=VerticalAlignment.Top;card.Background=background;card.Foreground=foreground;card.BorderBrush=Ui.Color("#AFB6A8");card.BorderThickness=new Thickness(1);card.IsEnabled=!paused && !Board.Won;Canvas.SetLeft(card,x);Canvas.SetTop(card,y);table.Children.Add(card);return card;}
        private void Paint()
        {
            table.Children.Clear();Card(Board.Stock.Count>0?"莲\n"+Board.Stock.Count:"↻",3,6,Ui.Color("#688B78"),Brushes.White,Draw);
            if(Board.Waste.Count==0)Card("翻牌",54,6,Ui.Color("#D2DCCB"),Ui.Muted,delegate{});else{int i=Board.Waste.Count-1,v=Board.Waste[i];Card(Label(v),54,6,selectedSource==-1?Ui.Color("#F4D59C"):Brushes.White,SolitaireBoard.Red(v)?Ui.Red:Ui.Ink,delegate{Choose(-1,i);});}
            for(int suit=0;suit<4;suit++){int s=suit,rank=Board.Foundation[suit];Card(rank==0?new[]{"♠","♥","♦","♣"}[suit]:Label(suit*13+rank-1),126+suit*57,6,hintTarget==7+suit?Ui.Color("#F4D59C"):Ui.Color("#D2DCCB"),suit==1 || suit==2?Ui.Red:Ui.Ink,delegate{Foundation(s);});}
            double height=360;
            for(int c=0;c<7;c++){int col=c;List<int> cards=Board.Columns[c];Card("K",3+c*51,94,hintTarget==c?Ui.Color("#F4D59C"):Ui.Color("#D2DCCB"),Ui.Muted,delegate{Choose(col,-1);});double y=94;
                for(int i=0;i<cards.Count;i++){int index=i,v=cards[i];bool hidden=i<Board.Hidden[c];Button button=Card(hidden?"莲":Label(v),3+c*51,y,hidden?Ui.Color("#688B78"):selectedSource==c && i>=selectedIndex?Ui.Color("#F4D59C"):Brushes.White,hidden?Brushes.White:SolitaireBoard.Red(v)?Ui.Red:Ui.Ink,delegate{Choose(col,index);});button.IsEnabled=!paused && !hidden && !Board.Won;button.Tag="card-"+c+"-"+i;y+=hidden?11:23;}height=Math.Max(height,y+65);
            }table.Height=height;score.Text="移动 "+Board.Moves+" · 收牌 "+Board.Foundation.Sum()+"/52 · "+(Board.DrawCount==1?"翻一张":"翻三张");message.Text=Board.Won?"四个花色归位，接龙完成！":paused?"已暂停":"点牌或牌组，再点目标列；红黑交替降序，空列放 K；点花色收牌。";
        }
        public override bool CanResume{get{return true;}}protected override object CaptureSession(){return Board.Won?null:Board.Capture();}protected override void RestoreSession(string json){SolitaireState state=MiniGameSession.Read<SolitaireState>(json);MiniGameSession.Require(state.DrawCount==1 || state.DrawCount==3);SolitaireBoard restored=new SolitaireBoard(state.DrawCount,new Random());restored.Restore(state);Board=restored;difficulty.SelectedIndex=state.DrawCount==3?1:0;ClearSelection();Paint();}public override void SetHostPaused(bool value){paused=value;if(Board!=null)Paint();}public override void Dispose(){}
    }
}
