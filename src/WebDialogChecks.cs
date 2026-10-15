using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using NezhaDesktop.MiniGames;

namespace NezhaDesktop
{
    internal static class WebDialogChecks
    {
        private static void Require(bool value, string text) { if (!value) throw new InvalidOperationException(text); }
        private static async Task Fits(WebMiniGameControl game, string selector)
        {
            string script = "(function(){var p=document.querySelector('" + selector + "'),r=p.getBoundingClientRect();return r.width>0&&r.left>=0&&r.top>=0&&r.right<=innerWidth&&r.bottom<=innerHeight&&p.scrollWidth<=p.clientWidth+1;})()";
            Require(await game.ExecuteForTest(script) == "true", selector + " 越界或出现横向滚动");
        }
        public static async Task Run(MiniGameWindow window, WebMiniGameControl game, string id, string output, List<string> results)
        {
            string before = await game.ExecuteForTest("JSON.stringify(NezhaDesktop.snapshot())");
            await game.ExecuteForTest("NezhaDesktop.newGame();NezhaDesktop.newGame()");
            Require(await game.ExecuteForTest("document.querySelector('#nezhaConfirm').open&&document.querySelectorAll('#nezhaConfirm').length===1&&NezhaDesktop.isPaused()") == "true", "确认期间没有暂停或出现多个确认框");
            foreach (int width in new[] { 290, 310, 380, 470 }) {
                window.Width = width; window.Height = width <= 310 ? 380 : width == 380 ? 550 : 650; window.UpdateLayout(); await Task.Delay(80);
                await Fits(game, "#nezhaConfirm");
                Require(await game.ExecuteForTest("(function(){var d=document.querySelector('#nezhaConfirm');d.scrollTop=d.scrollHeight;return Array.from(d.querySelectorAll('button')).every(function(b){var r=b.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth&&r.top>=0&&r.bottom<=innerHeight});})()") == "true", "确认按钮无法完整显示");
                await game.CaptureForTest(Path.Combine(output, id + "-reset-" + width + ".png"));
            }
            window.Activate(); await Task.Delay(100);
            await game.ExecuteForTest("document.querySelector('#nezhaCancelReset').click()");
            Require(await game.ExecuteForTest("!document.querySelector('#nezhaConfirm').open&&!NezhaDesktop.isPaused()") == "true", "取消确认后未恢复此前运行状态");
            Require(await game.ExecuteForTest("JSON.stringify(NezhaDesktop.snapshot())") == before, "取消重开改变了原进度");
            results.Add("PASS " + id + "-real-reset-dialog-fits-four-sizes-cancel-keeps-progress");
            window.Width = 380; window.Height = 550; window.UpdateLayout();
            if (id != "adarkroom") return;
            // Reproduce the real random event in the user's screenshot. Directly
            // select its fixture; avoid waiting minutes or editing personal saves.
            await game.ExecuteForTest("window.nezhaOldFx=$.fx.off;$.fx.off=true;Events.startEvent(Events.Room.filter(function(e){return e.title===_('The Shady Builder')})[0],{width:600})");
            await Task.Delay(80);
            foreach (int width in new[] { 290, 310, 380, 470 }) {
                window.Width=width; window.Height=width<=310?380:width==380?550:650; window.UpdateLayout(); await Task.Delay(80);
                await Fits(game, "#event");
                Require(await game.ExecuteForTest("(function(){var p=document.querySelector('#event');p.scrollTop=0;var t=p.querySelector('.eventTitle'),r=t.getBoundingClientRect(),hit=document.elementFromPoint(r.left+4,r.top+4);return t.textContent===_('The Shady Builder')&&r.top>=0&&r.bottom<=innerHeight&&(hit===t||t.contains(hit));})()") == "true", "事件标题被裁切或遮挡");
                await game.CaptureForTest(Path.Combine(output, "adarkroom-builder-"+width+".png"));
                Require(await game.ExecuteForTest("(function(){var p=document.querySelector('#event');p.scrollTop=p.scrollHeight;return Array.from(p.querySelectorAll('#buttons .button')).every(function(b){var r=b.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth&&r.top>=0&&r.bottom<=innerHeight});})()") == "true", "事件选项无法滚动到可见范围");
            }
            await game.ExecuteForTest("Events.endEvent();$.fx.off=nezhaOldFx");
            results.Add("PASS adarkroom-real-builder-title-and-options-fit-four-sizes");
            Require(await game.ExecuteForTest("Events.Marketing.length===0&&!Events.EventPool.some(function(e){return e.scenes&&e.scenes.start&&e.scenes.start.buttons&&e.scenes.start.buttons['give in']})") == "true", "离线版仍随机选择联网推广事件");
            Require(await game.ExecuteForTest("_('A Two-Headed Creature')==='双头怪物'&&_('A Ravaged Battleship')==='残破战舰'&&_('give in')==='回应呼唤'") == "true", "新增事件中文未在事件注册前载入");
            results.Add("PASS adarkroom-story-translations-loaded-and-network-promotion-excluded");
            // A long existing story and an inline width previously caused two
            // scrollbars. The host keeps it inside the viewport with vertical scroll.
            await game.ExecuteForTest("window.nezhaOldFx=$.fx.off;$.fx.off=true;var e={title:_('The Shady Builder'),scenes:{start:{text:Array(40).fill(_('a shady builder passes through')),buttons:{leave:{text:_('say goodbye'),nextScene:'end'}}}}};Events.startEvent(e,{width:900})");
            await Task.Delay(50); await Fits(game, "#event");
            Require(await game.ExecuteForTest("(function(){var p=document.querySelector('#event');p.scrollTop=p.scrollHeight;var buttons=p.querySelectorAll('#buttons .button'),b=buttons[buttons.length-1].getBoundingClientRect();return p.scrollHeight>p.clientHeight&&b.top>=0&&b.bottom<=innerHeight;})()") == "true", "长事件底部按钮不可达");
            await game.CaptureForTest(Path.Combine(output, "adarkroom-long-story.png"));
            await game.ExecuteForTest("Events.endEvent();$.fx.off=nezhaOldFx");
            results.Add("PASS adarkroom-long-story-scrolls-to-last-choice-without-horizontal-overflow");
            window.Width=380; window.Height=550; window.UpdateLayout();
            await game.ExecuteForTest("window.nezhaOldFx=$.fx.off;$.fx.off=true;window.nezhaOldOutfit=Path.outfit;window.nezhaOldHealth=World.health;Path.outfit={};World.health=World.getMaxHealth();Events.startEvent(Events.Encounters.filter(function(e){return e.title===_('A Two-Headed Creature')})[0])");
            await Task.Delay(80); await Fits(game, "#event");
            Require(await game.ExecuteForTest("document.querySelector('#event .eventTitle').textContent==='双头怪物'&&!!document.querySelector('#event #wanderer')&&!!document.querySelector('#event #enemy')&&document.querySelectorAll('#event #attackButtons .button').length>0") == "true", "战斗事件中文标题或战斗控件缺失");
            await game.CaptureForTest(Path.Combine(output, "adarkroom-combat-dialog.png"));
            await game.ExecuteForTest("Events.clearTimeouts();Events.endEvent();Path.outfit=nezhaOldOutfit;World.health=nezhaOldHealth;$.fx.off=nezhaOldFx");
            results.Add("PASS adarkroom-native-combat-dialog-keeps-title-fighters-and-actions");
        }
    }
}
