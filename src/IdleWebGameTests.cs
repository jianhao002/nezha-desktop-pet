using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using NezhaDesktop.MiniGames;

namespace NezhaDesktop
{
    public static class IdleWebGameTests
    {
        private static void Require(bool value, string text) { if (!value) throw new InvalidOperationException(text); }
        private static async Task Wait(WebMiniGameControl game, string predicate = null)
        {
            DateTime end = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < end) {
                if (game.LastError != null) throw new InvalidOperationException(game.LastError);
                if (game.ReadyForTest && (predicate == null || await game.ExecuteForTest(predicate) == "true")) return;
                await Task.Delay(50);
            }
            throw new TimeoutException("网页未就绪：" + predicate + " / " + await game.ExecuteForTest("JSON.stringify({paused:window.NezhaDesktop&&NezhaDesktop.isPaused(),hidden:document.hidden,focus:document.hasFocus(),dialog:!!document.querySelector('#nezhaConfirm[open]')})") + " / " + await game.ExecuteForTest("document.body.innerText.slice(0,1500)"));
        }
        private static async Task Save(WebMiniGameControl game)
        {
            DateTime end = DateTime.UtcNow.AddSeconds(10);
            while (game.SavingForTest) { if (DateTime.UtcNow >= end) throw new TimeoutException("保存仍在进行"); await Task.Delay(20); }
            game.SaveSession();
        }
        public static int Run(Application app, string output, string resumeData = null)
        {
            if (Directory.Exists(output)) throw new IOException("验证目录必须是新的。");
            Directory.CreateDirectory(output); var results = new List<string>();
            string root = Path.Combine(output, "runtime"), data = resumeData ?? Path.Combine(output, "data"); Directory.CreateDirectory(root);
            var window = new MiniGameWindow(root, data, null, false) { ShowActivated = true, ShowInTaskbar = false, Topmost = true };
            window.Loaded += async delegate {
                try {
                    var catalog = new MiniGameCatalog(root, data);
                    foreach (string id in new[] { "adarkroom", "candybox2" }) {
                        var entry = catalog.Scan().Single(e => e.Manifest.Id == id); window.StartForTest(entry);
                        var game = (WebMiniGameControl)window.ActiveGame; await Wait(game);
                        Require(await game.ExecuteForTest("document.characterSet==='UTF-8'&&NezhaDesktop.isPaused()&&document.querySelector('#nezhaContinue').textContent.indexOf('继续')>=0") == "true", id + " 未暂停或中文编码错误");
                        results.Add("PASS " + id + "-embedded-offline-chinese-entry-paused");
                        Require(await game.ExecuteForTest("document.querySelector('#nezhaConfirm') instanceof HTMLDialogElement") == "true", id + " 缺少能适应小窗的重开确认框");
                        string snapshot = id == "adarkroom" ? "JSON.stringify((function(){var s=NezhaDesktop.snapshot().state;return {game:s.game,stores:s.stores,features:s.features,character:s.character,previous:s.previous};})())" : "JSON.stringify(NezhaDesktop.snapshot().state)";
                        string expectedPath = Path.Combine(data, id + ".expected.json");
                        if (resumeData != null) {
                            Require(await game.ExecuteForTest(snapshot) == File.ReadAllText(expectedPath), id + " 跨进程恢复不一致");
                            results.Add("PASS " + id + "-new-process-restores-native-progress-from-session-file");
                            window.ShowCatalog(); continue;
                        }
                        game.SetHostPaused(false); await game.ExecuteForTest("NezhaDesktop.resume()"); await Wait(game, "!NezhaDesktop.isPaused()");
                        // Keep the unrelated first-run sound prompt from
                        // interrupting deterministic dialog / travel fixtures.
                        if (id == "adarkroom") await game.ExecuteForTest("$SM.set('playStats.audioAlertShown',true)");
                        await WebDialogChecks.Run(window, game, id, output, results);
                        // Resizing and closing a modal can hand focus back to the
                        // desktop. Restore the QA surface before testing live timers.
                        window.Activate(); await Task.Delay(100);
                        game.SetHostPaused(false); await game.ExecuteForTest("NezhaDesktop.resume()"); await Wait(game, "!NezhaDesktop.isPaused()");
                        if (id == "adarkroom") {
                            Require(await game.ExecuteForTest("document.querySelector('#lightButton').textContent.indexOf('生火')>=0") == "true", "生火按钮未翻译");
                            await game.ExecuteForTest("document.querySelector('#lightButton').click()");
                            await Wait(game, "$SM.get('game.fire.value')>0");
                            results.Add("PASS adarkroom-real-light-fire-changes-native-state");
                            // Use the real cooling action with a short QA delay to test pause.
                            await game.ExecuteForTest("clearTimeout(Room._fireTimer);Room._fireTimer=Engine.setTimeout(Room.coolFire,800)");
                        } else {
                            await Wait(game, "NezhaCandy().getCandies().getCurrent()>=2");
                            Require(await game.ExecuteForTest("Saving.loadString('gameLanguage')==='zh'&&Database.getTranslatedText('candyBoxEatCandiesButton').length>0") == "true", "中文提示未启用");
                            await game.ExecuteForTest("document.querySelector('.candyBoxEatCandiesButton').dispatchEvent(new MouseEvent('mouseup',{bubbles:true}))");
                            await Wait(game, "NezhaCandy().getCandiesEaten().getCurrent()>0");
                            results.Add("PASS candybox2-real-candy-production-and-eating");
                        }
                        game.SetHostPaused(true); await Wait(game, "NezhaDesktop.isPaused()");
                        string paused = await game.ExecuteForTest(snapshot); await Task.Delay(1200);
                        Require(await game.ExecuteForTest(snapshot) == paused, id + " 暂停时进度变化");
                        results.Add("PASS " + id + "-host-pause-stops-native-timers");
                        game.SetHostPaused(false); await game.ExecuteForTest("NezhaDesktop.resume()");
                        await Wait(game, id == "adarkroom" ? "$SM.get('game.fire.value')<3" : "NezhaCandy().getCandies().getCurrent()>0");
                        game.SetHostPaused(true); await Wait(game, "NezhaDesktop.isPaused()"); await Save(game);
                        game.SetHostPaused(false); await game.ExecuteForTest("NezhaDesktop.resume()");
                        if (id == "adarkroom") {
                            await game.ExecuteForTest("$SM.set('game.builder.level',4);$SM.set('game.temperature',Room.TempEnum.Warm);$SM.set('stores.wood',100);Outside.init();Room.updateBuildButtons();document.querySelector('#build_cart').click()");
                            await Wait(game, "$SM.get('game.buildings.cart')===1&&$SM.get('stores.wood')===70");
                            await game.ExecuteForTest("Engine.travelTo(Outside);document.querySelector('#gatherButton').click()");
                            await Wait(game, "$SM.get('stores.wood')===120&&Outside.panel.hasClass('nezhaActive')");
                            await game.CaptureForTest(Path.Combine(output, "adarkroom-village.png"));
                            await game.ExecuteForTest("Engine.travelTo(Room)");
                            results.Add("PASS adarkroom-unlocked-fixture-builds-cart-gathers-and-switches-panels");
                            await ADarkRoomTravelChecks.Run(window, game, output, results);
                        } else {
                            await game.ExecuteForTest("Saving.saveBool('statusBarUnlockedMap',true);Saving.saveBool('gridItemPossessedMainMap',true);NezhaCandy().goToMainMap()");
                            Require(await game.ExecuteForTest("(function(){var s=document.querySelector('#nezhaStage'),c=document.querySelector('#mainContent');return c.getBoundingClientRect().left+s.scrollLeft>=0&&s.scrollWidth>s.clientWidth&&!!document.querySelector('.mapVillageButton');})()") == "true", "大地图不能滚动或控件被裁切");
                            await game.CaptureForTest(Path.Combine(output, "candybox2-map.png"));
                            await game.ExecuteForTest("document.querySelector('.mapVillageButton').dispatchEvent(new MouseEvent('mouseup',{bubbles:true}))");
                            Require(await game.ExecuteForTest("!!document.querySelector('.mapVillageForgeButton')") == "true", "地图不能进入村庄");
                            await game.ExecuteForTest("NezhaCandy().goToCandyBox()");
                            results.Add("PASS candybox2-unlocked-fixture-scrolls-map-and-enters-village");
                        }
                        game.SetHostPaused(true); await Wait(game, "NezhaDesktop.isPaused()"); await Save(game);
                        string before = await game.ExecuteForTest(snapshot);
                        File.WriteAllText(Path.Combine(output, id + ".before.json"), before);
                        File.Copy(Path.Combine(data, "scores", id, "session.json"), Path.Combine(output, id + ".saved.json"));
                        window.ShowCatalog(); window.StartForTest(entry); game = (WebMiniGameControl)window.ActiveGame; await Wait(game);
                        string after = await game.ExecuteForTest(snapshot);
                        File.WriteAllText(Path.Combine(output, id + ".after.json"), after);
                        Require(after == before, id + " 关闭重开进度不一致");
                        results.Add("PASS " + id + "-dispose-reopen-preserves-native-progress-paused");
                        // Three supported window sizes: controls fit; wide ASCII remains scrollable.
                        foreach (int width in new[] { 310, 380, 470 }) {
                            window.Width = width; window.Height = width == 310 ? 465 : width == 380 ? 550 : 650; window.UpdateLayout(); await Task.Delay(80);
                            Require(await game.ExecuteForTest("(function(){var r=document.querySelector('#nezhaPause section').getBoundingClientRect();return r.left>=0&&r.top>=0&&r.right<=innerWidth&&r.bottom<=innerHeight;})()") == "true", id + " 暂停按钮超出小窗");
                            game.SetHostPaused(false); await game.ExecuteForTest("NezhaDesktop.resume()");
                            if (id == "candybox2") Require(await game.ExecuteForTest("document.querySelector('.candyBoxEatCandiesButton').getBoundingClientRect().left>=0") == "true", "吃糖按钮在小窗左侧被裁切");
                            await game.CaptureForTest(Path.Combine(output, id + "-" + width + ".png"));
                            game.SetHostPaused(true); await Wait(game, "NezhaDesktop.isPaused()");
                        }
                        results.Add("PASS " + id + "-three-small-window-sizes");
                        await game.ExecuteForTest("window.nezhaBlockedProbe=null;fetch('https://example.com/nezha-test').then(function(r){window.nezhaBlockedProbe=r.status},function(){window.nezhaBlockedProbe='blocked'})");
                        await Wait(game, "nezhaBlockedProbe===403||nezhaBlockedProbe==='blocked'");
                        results.Add("PASS " + id + "-external-resource-denied");
                        await Save(game); window.ShowCatalog();
                        string path = Path.Combine(data, "scores", id, "session.json"), saved = File.ReadAllText(path);
                        var serializer = new JavaScriptSerializer(); var bad = new MiniGameSessionRecord { Version = 1, State = serializer.Serialize(new WebGameSession { Version = 1, Json = "{\"version\":1,\"game\":\"" + id + "\",\"state\":3}" }) };
                        string corrupt = serializer.Serialize(bad); File.WriteAllText(path, corrupt);
                        window.StartForTest(entry); game = (WebMiniGameControl)window.ActiveGame; await Wait(game);
                        Require(await game.ExecuteForTest("document.querySelector('#nezhaContinue').disabled&&document.querySelector('#nezhaPause h2').textContent==='存档暂未载入'") == "true", id + " 错误存档未阻止续局");
                        await Save(game); Require(File.ReadAllText(path) == corrupt, id + " 覆盖了错误存档");
                        results.Add("PASS " + id + "-invalid-native-state-preserved-until-explicit-reset");
                        await game.ExecuteForTest("NezhaDesktop.newGame();document.querySelector('#nezhaCancelReset').click()");
                        Require(File.ReadAllText(path) == corrupt, id + " 取消重开仍修改存档");
                        await game.ExecuteForTest("NezhaDesktop.newGame();document.querySelector('#nezhaConfirmReset').click()");
                        await Wait(game, "NezhaDesktop.isPaused()&&window.nezhaInitialSession.state!==3&&document.querySelector('#nezhaPause h2').textContent==='已暂停'");
                        await Save(game); Require(File.ReadAllText(path) != corrupt, id + " 明确重开未生成新存档");
                        results.Add("PASS " + id + "-cancel-reset-keeps-save-confirm-reset-reloads-clean-game");
                        window.ShowCatalog(); File.WriteAllText(path, saved);
                        window.StartForTest(entry); game = (WebMiniGameControl)window.ActiveGame; await Wait(game); await Save(game);
                        File.WriteAllText(expectedPath, await game.ExecuteForTest(snapshot));
                        window.ShowCatalog();
                    }
                    window.Close(); Require(window.ActiveGame == null, "未释放网页控件"); results.Add("PASS normal-window-close-disposes-web-controls");
                    File.WriteAllText(Path.Combine(output, "idle-web-game-results.txt"), String.Join("\n", results) + "\nTOTAL=" + results.Count + " FAILED=0"); app.Shutdown(0);
                } catch (Exception ex) {
                    results.Add("FAIL " + ex); File.WriteAllText(Path.Combine(output, "idle-web-game-results.txt"), String.Join("\n", results));
                    if (window.ActiveGame != null) window.ActiveGame.Dispose(); app.Shutdown(1);
                }
            };
            window.Show(); return app.Run();
        }
    }
}
