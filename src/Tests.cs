using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NezhaDesktop
{
    public static class Tests
    {
        private static readonly DateTime Now = new DateTime(2026, 9, 30, 8, 0, 0, DateTimeKind.Utc);
        public static int Run(PetConfig config, string output)
        {
            Directory.CreateDirectory(output);
            List<string> results = new List<string>(); int failures = 0;
            Action<string, Action> test = delegate(string name, Action body) {
                try { body(); results.Add("PASS " + name); }
                catch (Exception ex) { failures++; results.Add("FAIL " + name + ": " + ex.Message); }
            };
            test("shopping-feeding-reload", delegate {
                string path = Path.Combine(output, "economy", "save.json");
                GameEngine engine = New(config, path);
                Food food = config.Foods[0];
                int initial = engine.State.Inventory[food.Id];
                Check(engine.Buy(food.Id, Now).Ok, "购买失败");
                Check(engine.State.Coins == 50 - food.Price && engine.State.Inventory[food.Id] == initial + 1, "购买账目不一致");
                Check(engine.Feed(food.Id, Now).Ok, "喂食失败");
                GameEngine restarted = new GameEngine(config, new SaveStore(path), Now);
                Check(restarted.State.Inventory[food.Id] == initial && restarted.State.Coins == 50 - food.Price, "重启未保留经济状态");
                Check(restarted.State.Satiety == Math.Min(100, 70 + food.Satiety), "属性增长错误");
            });
            test("insufficient-coins-and-empty-bag", delegate {
                GameEngine engine = New(config, Path.Combine(output, "reject", "save.json"));
                Food food = config.Foods[0]; engine.State.Coins = 0; engine.State.Inventory[food.Id] = 0; engine.Save();
                Check(!engine.Buy(food.Id, Now).Ok && !engine.Feed(food.Id, Now).Ok, "非法消费通过");
                Check(engine.State.Coins == 0 && engine.State.Inventory[food.Id] == 0, "失败操作扣减了物品");
                Check(!engine.Buy("missing", Now).Ok, "未知零食通过");
            });
            test("full-pet-does-not-consume-food", delegate {
                GameEngine engine = New(config, Path.Combine(output, "full", "save.json"));
                engine.State.Satiety = 100; int initial = engine.State.Inventory[config.Foods[0].Id];
                Check(!engine.Feed(config.Foods[0].Id, Now).Ok, "吃饱后仍可喂食");
                Check(engine.State.Inventory[config.Foods[0].Id] == initial, "吃饱后消耗物品");
            });
            test("work-persists-and-rewards-exactly-once", delegate {
                string path = Path.Combine(output, "work", "save.json"); GameEngine engine = New(config, path);
                Job job = config.Jobs[0]; Check(engine.StartWork(job.Id, Now).Ok, "打工未开始");
                Check(!engine.StartWork(job.Id, Now).Ok, "同时开始两份工作");
                Check(!engine.Collect(Now.AddSeconds(job.Seconds - 1)).Ok, "提前领取通过");
                Check(!engine.Feed(config.Foods[0].Id, Now).Ok, "打工中可以喂食");
                DateTime finished = Now.AddSeconds(job.Seconds);
                GameEngine restarted = new GameEngine(config, new SaveStore(path), finished);
                Check(restarted.WorkReady(finished) && restarted.Collect(finished).Ok, "重启后无法领取");
                Check(restarted.State.Coins == 50 + job.Reward && restarted.State.Experience == job.Experience, "报酬错误");
                Check(!restarted.Collect(finished).Ok && restarted.State.Coins == 50 + job.Reward, "重复领取发钱");
                GameEngine again = new GameEngine(config, new SaveStore(path), finished);
                Check(!again.Collect(finished).Ok && again.State.Coins == 50 + job.Reward, "重启后重复发钱");
            });
            test("work-insufficient-resources", delegate {
                GameEngine engine = New(config, Path.Combine(output, "resources", "save.json"));
                engine.State.Energy = 0; double initial = engine.State.Satiety;
                Check(!engine.StartWork(config.Jobs[0].Id, Now).Ok && engine.State.Work == null, "体力不足仍开始打工");
                Check(engine.State.Satiety == initial, "拒绝打工时仍扣饱腹");
            });
            test("save-failure-rolls-back-purchase", delegate {
                string path = Path.Combine(output, "write-failure", "save.json"); GameEngine engine = New(config, path);
                int initial = engine.State.Inventory[config.Foods[0].Id]; bool failed = false;
                using (FileStream locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    try { engine.Buy(config.Foods[0].Id, Now); } catch (IOException) { failed = true; }
                }
                Check(failed && engine.State.Coins == 50 && engine.State.Inventory[config.Foods[0].Id] == initial, "写入失败没有回滚");
                GameEngine reloaded = new GameEngine(config, new SaveStore(path), Now);
                Check(reloaded.State.Coins == 50, "失败写入修改了磁盘存档");
            });
            test("backup-recovery-preserves-valid-backup", delegate {
                string path = Path.Combine(output, "backup", "save.json"); GameEngine engine = New(config, path);
                engine.Buy(config.Foods[0].Id, Now); File.WriteAllText(path, "broken-save");
                SaveStore store = new SaveStore(path); SaveData restored = store.Load(config, Now);
                Check(store.RecoveredBackup && restored.Coins == 50, "未使用有效备份");
                store.Write(restored);
                Json.Read<SaveData>(File.ReadAllText(path + ".bak")).Validate();
                Json.Read<SaveData>(File.ReadAllText(path)).Validate();
            });
            test("corrupt-save-is-not-silently-reset", delegate {
                string path = Path.Combine(output, "corrupt", "save.json"); Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, "broken"); bool rejected = false;
                try { new SaveStore(path).Load(config, Now); } catch (InvalidDataException) { rejected = true; }
                Check(rejected && File.ReadAllText(path) == "broken", "损坏存档被悄悄重置");
            });
            test("offline-cap-and-clock-rollback", delegate {
                GameEngine engine = New(config, Path.Combine(output, "clock", "save.json"));
                engine.Advance(Now.AddDays(2)); Check(Math.Abs(engine.State.Satiety - 64) < 0.001, "离线衰减超过一小时");
                double after = engine.State.Satiety; engine.Advance(Now);
                Check(engine.State.Satiety == after && engine.State.LastUpdatedUtc == Now.AddDays(2), "时间回退重复结算");
            });
            test("touch-regions-and-mood-cooldown", delegate {
                Check(config.Hit(0.5, 0.3).Id == "head", "头部与神环重叠优先级错误");
                Check(config.Hit(0.5, 0.55).Id == "body", "身体未命中");
                Check(config.Hit(0.5, 0.9).Id == "wheels", "风火轮未命中");
                Check(config.Hit(0, 0) == null, "透明边角匹配了部位");
                GameEngine engine = New(config, Path.Combine(output, "touch", "save.json"));
                engine.Touch("head", Now); double after = engine.State.Mood;
                engine.Touch("head", Now); Check(engine.State.Mood == after, "连续点击刷心情");
            });
            test("asset-path-cannot-escape-package", delegate {
                bool rejected = false;
                try { PetConfig.AssetPath(output, "../outside.png"); } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "素材路径离开程序目录");
            });
            test("level-gates-apply-before-spending", delegate {
                GameEngine engine = New(config, Path.Combine(output, "level-gates", "save.json"));
                Food locked = config.Foods.First(f => f.MinimumLevel > 1);
                Job lockedJob = config.Jobs.First(j => j.MinimumLevel > 1);
                Check(!engine.Buy(locked.Id, Now).Ok && !engine.StartWork(lockedJob.Id, Now).Ok, "等级限制没有生效");
                Check(engine.State.Coins == 50 && engine.State.Inventory[locked.Id] == 0 && engine.State.Work == null, "锁定内容扣掉了资源");
            });
            test("exact-threshold-unlocks-and-gift-is-once", delegate {
                string path = Path.Combine(output, "level-gift", "save.json"); GameEngine engine = New(config, path);
                engine.State.Experience = config.Growth.Levels[1].Experience - 1; engine.Save();
                ActionResult touch = engine.Touch("head", Now);
                Check(engine.Level == 2 && engine.LevelProgress == 0 && !String.IsNullOrEmpty(touch.GrowthMessage), "达到门槛没有升级提示");
                Check(engine.State.Coins == 50 + config.Growth.Levels[1].GiftCoins, "成长礼错误");
                GameEngine restarted = new GameEngine(config, new SaveStore(path), Now);
                Check(restarted.State.Coins == engine.State.Coins && restarted.State.RewardedLevels.Count == 2, "重启重复发成长礼");
                Check(restarted.Buy(config.Foods.First(f => f.MinimumLevel == 2).Id, Now).Ok, "升级后没有解锁零食");
            });
            test("multi-level-jump-awards-all-gifts-once", delegate {
                PetConfig adjusted = Json.Clone(config); adjusted.Jobs[0].Experience = config.Growth.Levels.Last().Experience + 10;
                string path = Path.Combine(output, "multi-level", "save.json"); GameEngine engine = New(adjusted, path);
                Job job = adjusted.Jobs[0]; engine.StartWork(job.Id, Now); engine.Collect(Now.AddSeconds(job.Seconds));
                int expected = 50 + job.Reward + adjusted.Growth.Levels.Sum(level => level.GiftCoins);
                Check(engine.Level == adjusted.Growth.Levels.Length && engine.NextLevel == null && engine.State.Coins == expected, "跨级奖励遗漏或错误");
                GameEngine restarted = new GameEngine(adjusted, new SaveStore(path), Now.AddSeconds(job.Seconds));
                Check(restarted.State.Coins == expected && restarted.State.RewardedLevels.Count == adjusted.Growth.Levels.Length, "跨级奖励重复");
            });
            test("legacy-save-migrates-with-inventory-and-active-work", delegate {
                string path = Path.Combine(output, "migration", "save.json");
                SaveData legacy = SaveData.Fresh(config, Now); legacy.SchemaVersion = 1; legacy.Experience = 65;
                legacy.RewardedLevels = null; legacy.CareDate = null;
                legacy.Inventory[config.Foods[1].Id] = 3;
                legacy.Work = new ActiveWork { JobId = "old-job", Name = "旧版工作", EndsUtc = Now.AddSeconds(90), Reward = 57, Experience = 12, Animation = "busy" };
                new SaveStore(path).Write(legacy);
                GameEngine migrated = new GameEngine(config, new SaveStore(path), Now);
                Check(migrated.State.SchemaVersion == 2 && migrated.State.Experience == 65 && migrated.Level == 3, "旧经验未保留");
                Check(migrated.State.Inventory[config.Foods[1].Id] == 3 && migrated.State.Work.Reward == 57, "旧物品或进行中工作丢失");
                int before = migrated.State.Coins;
                Check(migrated.Collect(Now.AddSeconds(90)).Ok && migrated.State.Coins == before + 57 && migrated.State.Experience == 77, "旧版报酬快照未按原值结算");
                GameEngine restarted = new GameEngine(config, new SaveStore(path), Now.AddSeconds(90));
                Check(restarted.State.Coins == migrated.State.Coins, "迁移奖励重复发放");
            });
            test("food-specific-animations-and-legacy-stock", delegate {
                Check(config.Foods.Select(food => food.Animation).Distinct().Count() == config.Foods.Length, "不同食物共用了动画");
                foreach (Food food in config.Foods)
                {
                    GameEngine engine = New(config, Path.Combine(output, "food-" + food.Id, "save.json"));
                    engine.State.Satiety = 0; engine.State.Inventory[food.Id] = 1;
                    ActionResult result = engine.Feed(food.Id, Now);
                    Check(result.Ok && result.Animation == food.Animation && engine.State.Experience == food.Experience, "食物动作或经验映射错误");
                    Check(engine.State.Inventory[food.Id] == 0, "旧版已有的高等级物品无法正常使用");
                }
            });
            test("daily-care-cap-does-not-stop-feeding-and-resets", delegate {
                GameEngine engine = New(config, Path.Combine(output, "care-cap", "save.json")); Food food = config.Foods[0];
                engine.State.Satiety = 0; engine.State.Inventory[food.Id] = 10;
                engine.State.FeedExperienceToday = config.Growth.FeedDailyCap - 1;
                Check(engine.Feed(food.Id, Now).Ok && engine.State.Experience == 1, "临近上限没有只补剩余经验");
                engine.State.Satiety = 0;
                Check(engine.Feed(food.Id, Now).Ok && engine.State.Experience == 1 && engine.State.Satiety == food.Satiety, "达到经验上限后无法照顾");
                engine.State.Satiety = 0;
                Check(engine.Feed(food.Id, Now.AddDays(1)).Ok && engine.State.FeedExperienceToday == food.Experience, "新一天未恢复喂食经验");
                string day = engine.State.CareDate; int before = engine.State.FeedExperienceToday;
                engine.State.Satiety = 0; engine.Feed(food.Id, Now);
                Check(engine.State.CareDate == day && engine.State.FeedExperienceToday >= before, "时间回退重置每日限制");
            });
            test("touch-experience-cooldown-and-daily-limit", delegate {
                GameEngine engine = New(config, Path.Combine(output, "touch-xp", "save.json"));
                for (int i = 0; i < config.Growth.TouchDailyCap + 3; i++) engine.Touch("head", Now.AddSeconds(i * 8));
                Check(engine.State.Experience == config.Growth.TouchDailyCap && engine.State.TouchExperienceToday == config.Growth.TouchDailyCap, "互动经验超过上限");
            });
            test("work-bonus-uses-starting-level-snapshot", delegate {
                GameEngine engine = New(config, Path.Combine(output, "bonus-snapshot", "save.json"));
                engine.State.Experience = config.Growth.Levels[3].Experience; engine.Save(); Job job = config.Jobs.Last();
                int reward = engine.WorkReward(job); Check(reward > job.Reward, "收益加成未生效");
                Check(engine.StartWork(job.Id, Now).Ok && engine.State.Work.Reward == reward, "接工作没有保存收益快照");
                engine.State.Experience = config.Growth.Levels[4].Experience; engine.Save();
                int before = engine.State.Coins;
                engine.Collect(Now.AddSeconds(job.Seconds));
                Check(engine.State.Coins == before + reward + config.Growth.Levels[4].GiftCoins, "升级追溯改变了旧工作收入");
            });
            test("failed-save-rolls-back-level-and-gift", delegate {
                string path = Path.Combine(output, "gift-rollback", "save.json"); GameEngine engine = New(config, path);
                engine.State.Experience = config.Growth.Levels[1].Experience - 1; engine.Save(); bool failed = false;
                using (FileStream locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                { try { engine.Touch("head", Now); } catch (IOException) { failed = true; } }
                Check(failed && engine.Level == 1 && engine.State.Coins == 50 && engine.State.RewardedLevels.Count == 1, "存档失败仍升级或发钱");
                Check(engine.Touch("head", Now).Ok && engine.Level == 2 && engine.State.Coins == 50 + config.Growth.Levels[1].GiftCoins, "失败后重试不能正确升级");
            });
            results.Add("RESULT " + (results.Count - failures) + " passed, " + failures + " failed");
            File.WriteAllLines(Path.Combine(output, "test-results.txt"), results.ToArray(), new System.Text.UTF8Encoding(false));
            Console.WriteLine(String.Join(Environment.NewLine, results.ToArray()));
            return failures == 0 ? 0 : 1;
        }
        private static GameEngine New(PetConfig config, string path)
        { if (File.Exists(path)) throw new InvalidOperationException("测试目录必须是新的：" + path); return new GameEngine(config, new SaveStore(path), Now); }
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
