using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using NezhaDesktop.MiniGames;

namespace NezhaDesktop
{
    internal static class ADarkRoomTravelChecks
    {
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        public static async Task Run(MiniGameWindow window, WebMiniGameControl game, string output, List<string> results)
        {
            // Independent QA progress only. Use the actual supply arrows and
            // embark button, rather than forcing the map panel visible.
            await game.ExecuteForTest("$SM.set('stores.compass',1);$SM.set('stores[\"cured meat\"]',80);$SM.set('stores[\"bone spear\"]',2);if(!Path.panel)Path.init();Engine.travelTo(Path);document.querySelector('#outfit_row_cured-meat .upBtn').click();document.querySelector('#outfit_row_cured-meat .upBtn').click();document.querySelector('#outfit_row_bone-spear .upBtn').click();window.nezhaTravelStock=$SM.get('stores[\"cured meat\"]');document.querySelector('#embarkButton').click()");
            await Task.Delay(350);
            Require(await game.ExecuteForTest("Engine.activeModule===World&&Path.outfit['cured meat']===2&&$SM.get('stores[\"cured meat\"]')===nezhaTravelStock-2") == "true", "出发物资没有按原作转入背包");
            await game.CaptureForTest(Path.Combine(output, "adarkroom-embark.png"));
            File.WriteAllText(Path.Combine(output, "adarkroom-travel-state.json"), await game.ExecuteForTest("JSON.stringify({module:Engine.activeModule.name,world:World.panel[0].outerHTML,path:Path.panel[0].outerHTML,worldCount:document.querySelectorAll('#worldPanel').length,mapCount:document.querySelectorAll('#map').length,main:getComputedStyle(document.querySelector('#main')).display})"));
            Require(await game.ExecuteForTest("(function(){var m=document.querySelector('#map');return !!m&&m.getBoundingClientRect().width>0&&getComputedStyle(World.panel[0]).display!=='none'&&getComputedStyle(Path.panel[0]).display==='none';})()") == "true", "出发已扣物资，但地图仍隐藏，准备页未切换");
            results.Add("PASS adarkroom-real-embark-transfers-supplies-and-shows-map");
            foreach (int width in new[] { 290, 310, 380, 470 }) {
                window.Width=width; window.Height=width<=310?465:width==380?550:650; window.UpdateLayout(); await Task.Delay(80);
                Require(await game.ExecuteForTest("(function(){var v=document.querySelector('#nezhaMapViewport'),p=document.querySelector('#map .landmark'),r=v.getBoundingClientRect(),a=p.getBoundingClientRect();return r.width>0&&r.height>0&&r.left>=0&&r.right<=innerWidth&&r.bottom<=innerHeight&&a.left>=r.left&&a.right<=r.right&&a.top>=r.top&&a.bottom<=r.bottom&&document.documentElement.scrollWidth<=innerWidth;})()") == "true", "小窗地图越界或人物不在地图可视区域");
                await game.CaptureForTest(Path.Combine(output, "adarkroom-world-"+width+".png"));
            }
            results.Add("PASS adarkroom-map-viewport-centres-player-at-four-window-sizes");
            // Use one safe terrain cell next to home so this test does not
            // depend on a random landmark or encounter.
            await game.ExecuteForTest("World.state.map[31][30]=World.TILE.FIELD;World.fightMove=0;window.nezhaTravelKey=new KeyboardEvent('keydown',{bubbles:true,cancelable:true,key:'ArrowRight',keyCode:39,which:39});document.body.dispatchEvent(nezhaTravelKey)");
            File.WriteAllText(Path.Combine(output,"adarkroom-move-state.json"),await game.ExecuteForTest("JSON.stringify({pos:World.curPos,water:World.water,maxWater:World.getMaxWater(),mask:World.state&&World.state.mask[31][30],keyLock:Engine.keyLock,keyPressed:Engine.keyPressed,paused:NezhaDesktop.isPaused(),module:Engine.activeModule.name,event:Events.activeEvent()&&Events.activeEvent().title,events:Events.eventStack.length,body:document.body.innerText.slice(0,1600)})"));
            Require(await game.ExecuteForTest("World.curPos[0]===31&&World.curPos[1]===30&&World.water===World.getMaxWater()-1&&World.state.mask[31][30]&&nezhaTravelKey.defaultPrevented") == "true", "地图键盘移动 / 消耗 / 探索没有生效，或方向键仍滚动页面");
            await game.ExecuteForTest("Engine.keyDown({which:65})"); await Task.Delay(350);
            Require(await game.ExecuteForTest("Engine.activeModule===Path&&Path.panel.is(':visible')&&!World.panel.is(':visible')&&$SM.get('stores[\"cured meat\"]')===nezhaTravelStock&&$SM.get('game.world.mask')[31][30]") == "true", "回村没有恢复准备页、归还物资或保存探索");
            results.Add("PASS adarkroom-keyboard-explores-and-return-home-keeps-map-and-unused-supplies");
            await game.ExecuteForTest("document.querySelector('#embarkButton').click();World.die()");
            DateTime deadline=DateTime.UtcNow.AddSeconds(7);
            while (DateTime.UtcNow<deadline && await game.ExecuteForTest("Engine.activeModule===Room&&!Engine.keyLock&&Room.panel.is(':visible')&&!World.panel.is(':visible')") != "true") await Task.Delay(80);
            Require(await game.ExecuteForTest("Engine.activeModule===Room&&!Engine.keyLock&&Room.panel.is(':visible')&&!World.panel.is(':visible')&&Object.keys(Path.outfit).length===0") == "true", "遇险返回没有恢复房间或清理原作背包");
            results.Add("PASS adarkroom-native-death-restores-room-and-clears-expedition-bag");
            window.Width=380; window.Height=550; window.UpdateLayout();
        }
    }
}
