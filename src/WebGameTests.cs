using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NezhaDesktop.MiniGames;

namespace NezhaDesktop
{
    public static class WebGameTests
    {
        private static void Require(bool value, string name) { if (!value) throw new InvalidOperationException(name); }
        private static async Task Wait(WebMiniGameControl game, string predicate)
        {
            DateTime end = DateTime.UtcNow.AddSeconds(25);
            while (DateTime.UtcNow < end) {
                if (game.LastError != null) throw new InvalidOperationException(game.LastError);
                if (game.ReadyForTest && (predicate == null || await game.ExecuteForTest(predicate) == "true")) return;
                await Task.Delay(50);
            }
            string diagnostic = await game.ExecuteForTest("JSON.stringify((function(){var e=require('app/engine'),w=require('app/world'),s=require('app/gamestate'),d=w.getDude();return {paused:e.paused,death:NezhaDesktop.waitingForRetry(),saving:NezhaDesktop.saving,health:s.health,deaths:s.counts&&s.counts.DEATHS,dude:d&&{gone:d.gone,frame:d.frame,action:!!d.action},board:require('app/gameboard').desktopStable(),world:w.desktopStable(),capture:window.deathCaptureResult};})())");
            throw new TimeoutException("game not ready: " + predicate + " / " + game.LastError + " / " + diagnostic);
        }
        private static async Task Save(WebMiniGameControl game)
        {
            DateTime end = DateTime.UtcNow.AddSeconds(10);
            while (game.SavingForTest) {
                if (DateTime.UtcNow >= end) throw new TimeoutException("automatic session save did not finish");
                await Task.Delay(20);
            }
            game.SaveSession();
        }
        public static int Run(Application app, string output, string resumeData = null)
        {
            if (Directory.Exists(output)) throw new IOException("网页游戏验证目录必须是新的。");
            Directory.CreateDirectory(output); List<string> results = new List<string>();
            string root = Path.Combine(output, "runtime"), data = resumeData ?? Path.Combine(output, "data");
            Directory.CreateDirectory(root);
            // Keep the QA surface visible: an occluded browser may stop animation
            // frames or leave CapturePreviewAsync pending while timers still run.
            MiniGameWindow window = new MiniGameWindow(root, data, null, false) { ShowActivated = true, ShowInTaskbar = false, Topmost = true };
            window.Loaded += async delegate {
                try {
                    MiniGameCatalog catalog = new MiniGameCatalog(root, data);
                    MiniGameEntry entry = catalog.Scan().Single(e => e.Manifest.Id == "gridland");
                    window.StartForTest(entry); WebMiniGameControl game = (WebMiniGameControl)window.ActiveGame;
                    await Wait(game, null); results.Add("PASS embedded-sdk-loader-assets-and-offline-page");
                    if (resumeData == null) {
                        await game.ExecuteForTest("document.body.classList.add('desktopSaving')");
                        await Task.Delay(80);
                        string savingText = new JavaScriptSerializer().Deserialize<string>(await game.ExecuteForTest("getComputedStyle(document.querySelector('#desktopPause button'),'::after').content"));
                        await game.CaptureForTest(Path.Combine(output, "gridland-saving-text.png"));
                        await game.ExecuteForTest("document.body.classList.remove('desktopSaving')");
                        Require(savingText.Contains("正在保存本局…"), "saving prompt decoded incorrectly: " + savingText);
                        results.Add("PASS css-saving-prompt-displays-chinese");
                        Require(await game.ExecuteForTest("document.characterSet==='UTF-8'") == "true", "offline document is not UTF-8");
                        Require(await game.ExecuteForTest("document.querySelector('#desktopPause button').textContent==='继续游戏'") == "true", "resume prompt decoded incorrectly");
                        results.Add("PASS offline-page-and-resume-prompt-use-utf8");
                        Require(await game.ExecuteForTest("require('app/graphics/graphics').getText('ARE_YOU_SURE')==='确定重新开始？'&&require('app/graphics/graphics').getText('CANCEL')==='取消'") == "true", "menu confirmation text decoded incorrectly");
                        results.Add("PASS chinese-menu-confirmation-text");
                    }
                    string processScript = "JSON.stringify({tiles:require('app/gameboard').desktopCapture().tiles,night:require('app/world').isNight(),actors:NezhaDesktop.testSnapshot().world.actors.map(function(a){return {kind:a.kind,name:a.name,hp:a.data._hp,position:a.data._position,gone:!!a.data.gone};}),health:require('app/gamestate').health,position:require('app/world').desktopState().position})";
                    if (resumeData != null) {
                        await Wait(game, "require('app/engine').isStarted()&&require('app/engine').paused");
                        Require(await game.ExecuteForTest(processScript) == File.ReadAllText(Path.Combine(data, "web.expected.json")), "new process did not restore the same board / battle");
                        results.Add("PASS real-new-process-resumes-board-and-battle-paused"); await Save(game); window.Close();
                        File.WriteAllText(Path.Combine(output, "web-game-results.txt"), String.Join("\n", results) + "\nTOTAL=" + results.Count + " FAILED=0"); app.Shutdown(0); return;
                    }
                    await game.ExecuteForTest("NezhaDesktop.testStart();");
                    await Wait(game, "require('app/engine').isStarted()&&require('app/engine').desktopStable()&&require('app/gameboard').desktopStable()");
                    string board = await game.ExecuteForTest("require('app/gameboard').desktopCapture().tiles");
                    Require(board.Length > 65, "missing 8x8 board"); results.Add("PASS first-game-8x8-board");
                    await game.ExecuteForTest("NezhaDesktop.testResume();");
                    Require(await game.ExecuteForTest("NezhaDesktop.testMove()") == "true", "no playable move");
                    await Wait(game, "require('app/engine').desktopStable()&&require('app/gameboard').desktopStable()");
                    Require(await game.ExecuteForTest("require('app/gameboard').desktopCapture().tiles") != board, "matching move did not change board");
                    board = await game.ExecuteForTest("require('app/gameboard').desktopCapture().tiles"); results.Add("PASS real-match-removal-and-refill");
                    game.SetHostPaused(true);
                    await game.CaptureForTest(Path.Combine(output, "gridland-game.png"));
                    window.PreviewRoot.UpdateLayout();
                    RenderTargetBitmap full = new RenderTargetBitmap((int)window.PreviewRoot.ActualWidth, (int)window.PreviewRoot.ActualHeight, 96, 96, PixelFormats.Pbgra32); full.Render(window.PreviewRoot);
                    PngBitmapEncoder png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(full)); using (FileStream file = File.Create(Path.Combine(output, "gridland-window.png"))) png.Save(file);
                    game.SetHostPaused(true); await Save(game);
                    string filePath = Path.Combine(data, "scores", "gridland", "session.json");
                    Require(File.Exists(filePath), "session not saved"); results.Add("PASS pause-and-atomic-session-save");
                    string session = File.ReadAllText(filePath); Require(session.Contains("tiles") && session.Contains("progress") && session.Contains("world"), "session missing rule state");
                    window.ShowCatalog(); window.StartForTest(entry); game = (WebMiniGameControl)window.ActiveGame;
                    await Wait(game, "require('app/engine').isStarted()&&require('app/engine').paused");
                    Require(await game.ExecuteForTest("require('app/gameboard').desktopCapture().tiles") == board, "board changed on resume");
                    results.Add("PASS actual-control-dispose-reopen-restores-same-board-paused");
                    await game.ExecuteForTest("NezhaDesktop.testResume();");
                    await Wait(game, "!require('app/engine').paused"); game.SetHostPaused(true);
                    await Wait(game, "require('app/engine').paused");
                    string before = await game.ExecuteForTest("require('app/world').desktopState().dude._position"); await Task.Delay(350);
                    Require(await game.ExecuteForTest("require('app/world').desktopState().dude._position") == before, "actor moved while paused");
                    results.Add("PASS host-pause-stops-world-movement");
                    // Real zero-HP death, including the original animation / count / checkpoint path.
                    await game.ExecuteForTest("NezhaDesktop.testResume();");
                    await Wait(game, "require('app/world').desktopStable()&&require('app/gamestate').hasBase()");
                    await game.ExecuteForTest("var s=require('app/gamestate');s.dayNumber=3;s.xp=7;s.health=s.maxHealth();s.counts.DEATHS=0;require('app/eventmanager').trigger('dayBreak',[3]);require('app/eventmanager').trigger('phaseChange',[true]);");
                    await Wait(game, "require('app/world').desktopStable()&&require('app/world').isNight()");
                    await Save(game);
                    string growthScript = "JSON.stringify((function(){var s=require('app/gamestate');return {buildings:s.buildings.map(function(b){return {type:b.options.type.className,built:b.built,obsolete:b.obsolete,resources:b.requiredResources};}),stores:s.stores.map(function(b){return {type:b.options.type.className,quantity:b._quantity};}),level:s.level,xp:s.xp,day:s.dayNumber,items:s.items,gem:s.gem,mana:s.mana,prestige:s.prestige};})())";
                    string growth = await game.ExecuteForTest(growthScript);
                    await game.ExecuteForTest("var d=require('app/world').getDude();d.shield=0;d.takeDamage(10000);");
                    Require(await game.ExecuteForTest("require('app/gamestate').health===0") == "true", "death fixture did not reach zero HP");
                    await Wait(game, "NezhaDesktop.waitingForRetry()&&require('app/gamestate').counts.DEATHS===1&&require('app/engine').paused");
                    Require(await game.ExecuteForTest("document.querySelector('#desktopDeathTitle').textContent==='你倒下了'&&document.querySelector('#desktopDeathMessage').textContent==='已保存的成长保留，将从第 3 天白天重试。'") == "true", "death / retry text is wrong");
                    results.Add("PASS real-zero-hp-death-counts-once-and-shows-saved-day");
                    foreach (int width in new[] { 310, 380, 470 }) {
                        window.Width=width; window.Height=width==310?465:width==380?550:650; window.UpdateLayout(); await Task.Delay(100);
                        Require(await game.ExecuteForTest("(function(){var d=document.querySelector('#desktopDeath [role=dialog]'),r=d.getBoundingClientRect();return r.width>0&&r.left>=0&&r.right<=innerWidth&&r.top>=0&&r.bottom<=innerHeight&&d.scrollHeight<=d.clientHeight;})()") == "true", "death dialog does not fit small window");
                        await game.CaptureForTest(Path.Combine(output,"gridland-death-"+width+".png"));
                    }
                    results.Add("PASS death-dialog-fits-three-small-window-sizes");
                    await game.ExecuteForTest("NezhaDesktop.testResume();require('app/eventmanager').trigger('unpause');document.body.click();");
                    await Task.Delay(3000);
                    Require(await game.ExecuteForTest("NezhaDesktop.waitingForRetry()&&require('app/engine').paused&&require('app/world').isNight()&&require('app/gamestate').health===0&&require('app/gamestate').counts.DEATHS===1") == "true", "death resumed without Continue or counted twice");
                    results.Add("PASS death-waits-for-explicit-continue-and-blocks-background-resume");
                    await Save(game);
                    Require(new JavaScriptSerializer().Deserialize<MiniGameSessionRecord>(File.ReadAllText(filePath)).State == null, "defeated battle was not marked ended");
                    Require(await game.ExecuteForTest("JSON.parse(localStorage.slot0).health===0&&JSON.parse(localStorage.slot0).counts.DEATHS===1") == "true", "checkpoint was overwritten while waiting");
                    window.ShowCatalog(); window.StartForTest(entry); game=(WebMiniGameControl)window.ActiveGame;
                    await Wait(game, "NezhaDesktop.waitingForRetry()&&require('app/engine').paused");
                    Require(await game.ExecuteForTest("!require('app/engine').isStarted()&&document.querySelector('#desktopDeathMessage').textContent.indexOf('第 3 天')>=0") == "true", "reopened defeated battle or lost retry day");
                    results.Add("PASS close-reopen-preserves-death-dialog-without-resurrecting-battle");
                    // Repeat click while initialization is pending must still create only one retry.
                    await game.ExecuteForTest("document.querySelector('#desktopDeath button').click();document.querySelector('#desktopDeath button').click();");
                    await Wait(game, "require('app/engine').isStarted()&&!NezhaDesktop.waitingForRetry()&&!require('app/world').isNight()&&require('app/gamestate').health===require('app/gamestate').maxHealth()");
                    Require(await game.ExecuteForTest(growthScript) == growth, "retry changed the retained growth checkpoint");
                    Require(await game.ExecuteForTest("require('app/gamestate').counts.DEATHS===1&&!localStorage.nezhaDeathRetry&&!document.body.classList.contains('desktopDeath')") == "true", "retry retained death flag or changed death count");
                    results.Add("PASS continue-once-restores-saved-growth-and-full-health-in-daylight");
                    await game.ExecuteForTest("NezhaDesktop.testResume();");
                    await Wait(game, "require('app/world').desktopStable()&&require('app/world').desktopState().celestial!=null");
                    await game.ExecuteForTest("require('app/eventmanager').trigger('phaseChange',[true]);");
                    await Wait(game,"require('app/world').isNight()&&require('app/world').desktopStable()&&require('app/engine').desktopStable()&&require('app/gameboard').desktopStable()");
                    // Begin a capture while a real tile swap is settling, then die before it completes.
                    Require(await game.ExecuteForTest("NezhaDesktop.testMove()") == "true", "no playable move for capture/death fixture");
                    await game.ExecuteForTest("setTimeout(function(){var d=require('app/world').getDude();d.shield=0;d.takeDamage(10000);},50);");
                    await Save(game);
                    await Wait(game,"NezhaDesktop.waitingForRetry()");
                    Require(await game.ExecuteForTest("JSON.parse(localStorage.slot0).counts.DEATHS===2&&require('app/engine').paused") == "true", "capture racing with death overwrote checkpoint or resumed battle");
                    await Save(game);
                    results.Add("PASS death-during-in-flight-capture-keeps-checkpoint-and-retry-paused");
                    await game.ExecuteForTest("document.querySelector('#desktopDeath button').click();");
                    await Wait(game,"require('app/engine').isStarted()&&!NezhaDesktop.waitingForRetry()&&!require('app/world').isNight()&&require('app/gamestate').health>0");
                    await game.ExecuteForTest("NezhaDesktop.testResume();");
                    await Wait(game,"require('app/world').desktopStable()&&require('app/world').desktopState().celestial!=null");
                    await game.ExecuteForTest("var d=require('app/world').getDude();d.shield=0;d.takeDamage(10000);");
                    await Save(game); // Save/close immediately, before the death animation finishes.
                    Require(await game.ExecuteForTest("NezhaDesktop.waitingForRetry()&&JSON.parse(localStorage.slot0).counts.DEATHS===3") == "true", "immediate close lost death checkpoint");
                    window.ShowCatalog(); window.StartForTest(entry); game=(WebMiniGameControl)window.ActiveGame;
                    await Wait(game,"NezhaDesktop.waitingForRetry()&&require('app/engine').paused");
                    results.Add("PASS immediate-save-close-finishes-death-and-restores-retry-dialog");
                    await game.ExecuteForTest("document.querySelector('#desktopDeath button').click();");
                    await Wait(game,"require('app/engine').isStarted()&&!NezhaDesktop.waitingForRetry()&&!require('app/world').isNight()&&require('app/gamestate').health>0");
                    await game.ExecuteForTest("NezhaDesktop.testResume();");
                    await Wait(game, "require('app/world').desktopState().celestial!=null");
                    game.SetHostPaused(true); await Wait(game, "require('app/engine').paused");
                    await game.ExecuteForTest("NezhaDesktop.testPhase();");
                    await Wait(game, "require('app/world').desktopStable()&&require('app/world').isNight()");
                    await game.ExecuteForTest("require('app/eventmanager').trigger('tilesCleared',[{grain:3},'left']);require('app/world').desktopRestoreEffect('haste',8000);");
                    string nightScript = "JSON.stringify({night:require('app/world').isNight(),actors:NezhaDesktop.testSnapshot().world.actors.map(function(a){return {kind:a.kind,name:a.name,hp:a.data._hp,position:a.data._position,gone:!!a.data.gone};}),health:require('app/gamestate').health,position:require('app/world').desktopState().position})";
                    string night = await game.ExecuteForTest(nightScript);
                    Require(night.Contains("zombie"), "night fixture did not spawn monster");
                    await Save(game); window.ShowCatalog(); window.StartForTest(entry); game=(WebMiniGameControl)window.ActiveGame;
                    await Wait(game, "require('app/engine').isStarted()&&require('app/engine').paused");
                    string restoredNight=await game.ExecuteForTest(nightScript);
                    Require(restoredNight==night,"night actors / health / time changed on resume"); results.Add("PASS night-monster-health-position-and-day-night-roundtrip");
                    string buff=await game.ExecuteForTest("require('app/world').desktopState().effects.haste.remaining"); await Task.Delay(200);
                    Require(await game.ExecuteForTest("require('app/world').desktopState().effects.haste.remaining")==buff,"paused spell expired"); results.Add("PASS spell-time-restores-and-freezes-while-paused");
                    File.WriteAllText(Path.Combine(data, "web.expected.json"), await game.ExecuteForTest(processScript));
                    await game.CaptureForTest(Path.Combine(output, "gridland-night.png"));
                    foreach (int width in new[] { 310, 380, 470 }) {
                        window.Width = width; window.Height = width == 310 ? 465 : width == 380 ? 550 : 650; window.UpdateLayout(); await Task.Delay(150);
                        await game.CaptureForTest(Path.Combine(output, "gridland-" + width + ".png"));
                    }
                    results.Add("PASS three-small-window-render-sizes");
                    await Save(game); window.Close(); Require(window.ActiveGame == null, "normal close did not dispose"); results.Add("PASS normal-window-close-persists-and-disposes");
                    File.WriteAllText(Path.Combine(output, "web-game-results.txt"), String.Join("\n", results) + "\nTOTAL=" + results.Count + " FAILED=0"); app.Shutdown(0);
                } catch (Exception ex) {
                    results.Add("FAIL " + ex); File.WriteAllText(Path.Combine(output, "web-game-results.txt"), String.Join("\n", results));
                    if (window.ActiveGame != null) window.ActiveGame.Dispose(); app.Shutdown(1);
                }
            };
            window.Show(); return app.Run();
        }
    }
}
