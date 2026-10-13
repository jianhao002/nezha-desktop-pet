using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NezhaDesktop.MiniGames;
namespace NezhaDesktop
{
    internal static class NewGameTests
    {
        private static void Check(bool valid,string message){if(!valid)throw new InvalidOperationException(message);}
        private static object Get(object item,string name){return item.GetType().GetProperty(name).GetValue(item,null);}
        private static object Field(object item,string name){return item.GetType().GetField(name).GetValue(item);}
        private static object Call(object item,string name,params object[] args){try{return item.GetType().GetMethod(name).Invoke(item,args);}catch(TargetInvocationException ex){throw ex.InnerException;}}
        private static object New(IMiniGame game,string name,params object[] args){return Activator.CreateInstance(game.GetType().Assembly.GetType("NezhaGames."+name,true),args);}
        public static void Run(Action<string,Action> test,Dictionary<string,IMiniGame> games)
        {
            if(games.ContainsKey("link-match")){
                IMiniGame game=games["link-match"];
                test("link-match-outer-border-two-turns-and-blocked-path",delegate{
                    object board=New(game,"LinkBoard",6,6,new Random(1));int[] cells=(int[])Field(board,"Cells");Array.Clear(cells,0,cells.Length);cells[0]=cells[5]=1;cells[1]=2;
                    Check((bool)Call(board,"CanConnect",0,5),"outside path rejected");Check((bool)Call(board,"Match",0,5) && cells[0]==0 && cells[5]==0,"matching pair not removed");
                    for(int i=0;i<36;i++)cells[i]=2;cells[14]=cells[21]=1;Check(!(bool)Call(board,"CanConnect",14,21),"fully trapped pair connected");Check(!(bool)Call(board,"Match",14,15),"different symbols removed");
                });
                test("link-match-generates-even-pairs-and-shuffle-keeps-remaining",delegate{
                    foreach(int seed in Enumerable.Range(0,16)){object board=New(game,"LinkBoard",10,8,new Random(seed));int[] cells=(int[])Field(board,"Cells");Check(cells.All(v=>v>0) && cells.GroupBy(v=>v).All(g=>g.Count()%2==0),"unpaired symbols");int[] pair=(int[])Call(board,"FindPair");Check(pair!=null && (bool)Call(board,"Match",pair[0],pair[1]),"no opening move");int[] remaining=cells.Where(v=>v>0).OrderBy(v=>v).ToArray();Call(board,"Shuffle",new Random(seed));Check(cells.Where(v=>v>0).OrderBy(v=>v).SequenceEqual(remaining) && Call(board,"FindPair")!=null,"shuffle changed symbols or left no move");}
                });
            }
            if(games.ContainsKey("same-game")){
                IMiniGame game=games["same-game"];
                test("same-game-component-fall-column-compression-and-undo",delegate{
                    object board=New(game,"SameBoard",3,3,3,new Random(1));int[] cells=(int[])Field(board,"Cells");int[] initial={1,2,3,1,2,3,1,2,3};Array.Copy(initial,cells,9);Check((bool)Call(board,"Remove",0),"region not removed");Check(cells.SequenceEqual(new[]{2,3,0,2,3,0,2,3,0}) && (int)Get(board,"Score")==4,"wrong collapse or score");Check((bool)Call(board,"Undo") && cells.SequenceEqual(initial) && (int)Get(board,"Score")==0,"undo lost board or score");
                    Array.Copy(new[]{1,2,3,2,3,1,3,1,2},cells,9);Check(!(bool)Call(board,"Remove",0) && (bool)Get(board,"Ended"),"singleton accepted");
                });
                test("same-game-clear-bonus-and-no-more-moves",delegate{
                    object board=New(game,"SameBoard",3,3,3,new Random(1));int[] cells=(int[])Field(board,"Cells");for(int i=0;i<9;i++)cells[i]=1;Call(board,"Remove",4);Check((bool)Get(board,"Won") && (bool)Get(board,"Ended") && (int)Get(board,"Score")==1064,"clear bonus incorrect");
                });
            }
            if(games.ContainsKey("net-lights")){
                IMiniGame game=games["net-lights"];
                test("net-lights-generated-tree-recoverable-and-lock-respected",delegate{
                    foreach(int size in new[]{5,7,9})foreach(int seed in Enumerable.Range(0,10)){object board=New(game,"NetBoard",size,new Random(seed));int[] pipes=(int[])Field(board,"Pipes");Check(!(bool)Get(board,"Won") && pipes.All(p=>p>=1 && p<=15) && pipes.Sum(p=>Enumerable.Range(0,4).Count(d=>(p&(1<<d))!=0))==2*(size*size-1),"invalid generated pipe graph");int old=pipes[0];Call(board,"ToggleLock",0);Check(!(bool)Call(board,"Rotate",0) && pipes[0]==old,"locked pipe rotated");Call(board,"ToggleLock",0);Check((bool)Call(board,"Rotate",0),"unlock failed");}
                });
                test("net-lights-connected-cells-with-open-end-do-not-win",delegate{
                    object board=New(game,"NetBoard",2,new Random(1));int[] pipes=(int[])Field(board,"Pipes");Array.Copy(new[]{6,12,3,9},pipes,4);Check((bool)Get(board,"Won"),"connected closed network rejected");pipes[0]|=1;Check(!(bool)Get(board,"Won"),"open boundary accepted as win");
                });
            }
            if(games.ContainsKey("solitaire")){
                IMiniGame game=games["solitaire"];
                test("solitaire-deal-52-distinct-hidden-columns-draw-three-and-recycle",delegate{
                    object board=New(game,"SolitaireBoard",3,new Random(17));List<int>[] columns=(List<int>[])Field(board,"Columns");List<int> stock=(List<int>)Field(board,"Stock"),waste=(List<int>)Field(board,"Waste");int[] hidden=(int[])Field(board,"Hidden");Check(columns.Select((c,i)=>c.Count==i+1 && hidden[i]==i).All(v=>v) && stock.Concat(columns.SelectMany(c=>c)).Distinct().Count()==52,"invalid deal");int first=stock.Last();Call(board,"Draw");Check(stock.Count==21 && waste.Count==3 && waste[0]==first,"three-card draw incorrect");for(int i=0;i<7;i++)Call(board,"Draw");int[] before=waste.ToArray();Call(board,"Draw");Check(waste.Count==0 && stock.SequenceEqual(before.Reverse()),"recycle order incorrect");
                });
                test("solitaire-alternating-stacks-king-only-empty-foundation-rank-and-undo",delegate{
                    object board=New(game,"SolitaireBoard",1,new Random(1));List<int>[] columns=(List<int>[])Field(board,"Columns");List<int> waste=(List<int>)Field(board,"Waste");int[] hidden=(int[])Field(board,"Hidden");foreach(List<int> col in columns)col.Clear();Array.Clear(hidden,0,7);waste.Clear();columns[0].AddRange(new[]{11,23});columns[1].Add(25);
                    Check((bool)Call(board,"CanMove",0,0,1),"valid alternating stack rejected");Check(!(bool)Call(board,"Move",0,1,2),"nonking moved to empty");Check((bool)Call(board,"Move",0,0,1) && columns[0].Count==0 && columns[1].Count==3,"stack movement failed");Check((bool)Call(board,"Undo") && columns[0].SequenceEqual(new[]{11,23}),"stack undo failed");
                    waste.Add(13);Check((bool)Call(board,"ToFoundation",-1,0) && ((int[])Field(board,"Foundation"))[1]==1,"ace rejected");waste.Add(15);Check(!(bool)Call(board,"ToFoundation",-1,0),"skipped foundation rank");
                });
                test("solitaire-reveals-hidden-card-after-moving-top",delegate{
                    object board=New(game,"SolitaireBoard",1,new Random(1));List<int>[] columns=(List<int>[])Field(board,"Columns");int[] hidden=(int[])Field(board,"Hidden");foreach(List<int> col in columns)col.Clear();Array.Clear(hidden,0,7);columns[0].AddRange(new[]{4,12});hidden[0]=1;Check((bool)Call(board,"Move",0,1,1) && hidden[0]==0,"new top stayed hidden");
                });
            }
        }
    }
}
