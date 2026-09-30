using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace NezhaDesktop
{
    public class AnimationSpec
    {
        public string Sheet { get; set; }
        public int Row { get; set; }
        public int[] Frames { get; set; }
        public string[] Files { get; set; }
        public int FrameMs { get; set; }
        public int FrameWidth { get; set; }
        public int FrameHeight { get; set; }
        public int Columns { get; set; }
        public int DurationMs { get; set; }
        public bool? Loop { get; set; }
    }
    public class Interaction
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string Animation { get; set; }
        public string Message { get; set; }
        public double Mood { get; set; }
        public bool Contains(double x, double y)
        { return x >= X && x <= X + Width && y >= Y && y <= Y + Height; }
    }
    public class Food
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int Price { get; set; }
        public double Satiety { get; set; }
        public double Energy { get; set; }
        public double Mood { get; set; }
        public string Animation { get; set; }
        public int MinimumLevel { get; set; }
        public int Experience { get; set; }
    }
    public class Job
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int Seconds { get; set; }
        public int Reward { get; set; }
        public double EnergyCost { get; set; }
        public double SatietyCost { get; set; }
        public int Experience { get; set; }
        public string Animation { get; set; }
        public int MinimumLevel { get; set; }
    }
    public class LevelSpec
    {
        public int Level { get; set; }
        public int Experience { get; set; }
        public string Title { get; set; }
        public int GiftCoins { get; set; }
        public int WorkBonusPercent { get; set; }
        public string Benefit { get; set; }
    }
    public class GrowthSpec
    {
        public int FeedDailyCap { get; set; }
        public int TouchDailyCap { get; set; }
        public LevelSpec[] Levels { get; set; }
    }
    public class RestSpec
    {
        public int Seconds { get; set; }
        public double EnergyReward { get; set; }
        public double MoodReward { get; set; }
        public string Animation { get; set; }
    }
    public class PetConfig
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public int FrameWidth { get; set; }
        public int FrameHeight { get; set; }
        public int DefaultSize { get; set; }
        public Dictionary<string, AnimationSpec> Animations { get; set; }
        public Interaction[] Interactions { get; set; }
        public Food[] Foods { get; set; }
        public Job[] Jobs { get; set; }
        public GrowthSpec Growth { get; set; }
        public RestSpec Rest { get; set; }

        public static PetConfig Load(string root)
        {
            PetConfig config = Json.Read<PetConfig>(File.ReadAllText(Path.Combine(root, "config.json")));
            config.Validate(root);
            return config;
        }
        public static string AssetPath(string root, string relative)
        {
            if (String.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
                throw new InvalidDataException("素材路径必须是包内相对路径。");
            string basePath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string result = Path.GetFullPath(Path.Combine(basePath, relative));
            if (!result.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("素材路径不能离开桌宠目录。");
            return result;
        }
        public void Validate(string root)
        {
            if (String.IsNullOrWhiteSpace(Name) || FrameWidth <= 0 || FrameHeight <= 0 || DefaultSize < 120 || DefaultSize > 600)
                throw new InvalidDataException("名称、帧尺寸或默认大小无效。");
            if (Animations == null || !Animations.ContainsKey("idle") || !Animations.ContainsKey("wave") || !Animations.ContainsKey("upset") || !Animations.ContainsKey("wait"))
                throw new InvalidDataException("需要 idle、wave、upset、wait 基础动作。");
            foreach (KeyValuePair<string, AnimationSpec> pair in Animations)
            {
                AnimationSpec animation = pair.Value;
                if (animation == null || animation.FrameMs < 30 || animation.FrameMs > 3000 || animation.Row < 0 || animation.FrameWidth < 0 || animation.FrameHeight < 0 || animation.DurationMs < 0 || animation.Columns < 0 || animation.Columns > 32)
                    throw new InvalidDataException("动作参数无效：" + pair.Key);
                if (animation.Files != null && animation.Files.Length > 0)
                {
                    foreach (string file in animation.Files)
                        if (!File.Exists(AssetPath(root, file))) throw new FileNotFoundException("缺少动作文件：" + file);
                }
                else
                {
                    if (animation.Frames == null || animation.Frames.Length == 0 || animation.Frames.Any(f => f < 0))
                        throw new InvalidDataException("动作没有有效帧：" + pair.Key);
                    if (!File.Exists(AssetPath(root, animation.Sheet))) throw new FileNotFoundException("缺少图集：" + animation.Sheet);
                }
            }
            if (Interactions == null || Foods == null || Jobs == null || Foods.Length == 0 || Jobs.Length == 0)
                throw new InvalidDataException("互动、零食和打工配置不能为空。");
            RequireUnique(Interactions.Select(i => i.Id), "互动");
            RequireUnique(Foods.Select(i => i.Id), "零食");
            RequireUnique(Jobs.Select(i => i.Id), "工作");
            foreach (Interaction i in Interactions)
                if (!Animations.ContainsKey(i.Animation) || i.X < 0 || i.Y < 0 || i.Width <= 0 || i.Height <= 0 || i.X + i.Width > 1 || i.Y + i.Height > 1 || i.Mood < 0 || i.Mood > 10)
                    throw new InvalidDataException("互动配置无效：" + i.Id);
            foreach (Food f in Foods)
                if (!Animations.ContainsKey(f.Animation) || f.Price <= 0 || f.Satiety < 0 || f.Energy < 0 || f.Mood < 0 || f.MinimumLevel < 1 || f.Experience < 0)
                    throw new InvalidDataException("零食配置无效：" + f.Id);
            foreach (Job j in Jobs)
                if (!Animations.ContainsKey(j.Animation) || j.Seconds < 10 || j.Reward <= 0 || j.EnergyCost < 0 || j.EnergyCost > 100 || j.SatietyCost < 0 || j.SatietyCost > 100 || j.Experience < 0 || j.MinimumLevel < 1)
                    throw new InvalidDataException("工作配置无效：" + j.Id);
            if (Growth == null || Growth.Levels == null || Growth.Levels.Length == 0 || Growth.FeedDailyCap < 0 || Growth.TouchDailyCap < 0)
                throw new InvalidDataException("成长配置无效。");
            for (int index = 0; index < Growth.Levels.Length; index++)
            {
                LevelSpec level = Growth.Levels[index];
                if (level == null || level.Level != index + 1 || String.IsNullOrWhiteSpace(level.Title) || level.GiftCoins < 0 || level.WorkBonusPercent < 0 || level.WorkBonusPercent > 100
                    || (index == 0 ? level.Experience != 0 : level.Experience <= Growth.Levels[index - 1].Experience))
                    throw new InvalidDataException("等级必须从 1 开始连续，经验门槛递增。");
            }
            if (Foods.Any(f => f.MinimumLevel > Growth.Levels.Length) || Jobs.Any(j => j.MinimumLevel > Growth.Levels.Length))
                throw new InvalidDataException("解锁等级超出了成长路线。");
            if (Rest == null || Rest.Seconds < 10 || Rest.Seconds > 86400 || !ValidReward(Rest.EnergyReward) || Rest.EnergyReward <= 0
                || !ValidReward(Rest.MoodReward) || String.IsNullOrWhiteSpace(Rest.Animation) || !Animations.ContainsKey(Rest.Animation))
                throw new InvalidDataException("休息时长、恢复量或动作配置无效。");
        }
        private static bool ValidReward(double value)
        { return !Double.IsNaN(value) && !Double.IsInfinity(value) && value >= 0 && value <= 100; }
        private static void RequireUnique(IEnumerable<string> ids, string name)
        {
            string[] values = ids.ToArray();
            if (values.Any(String.IsNullOrWhiteSpace) || values.Distinct(StringComparer.Ordinal).Count() != values.Length)
                throw new InvalidDataException(name + " ID 不能为空或重复。");
        }
        public Interaction Hit(double x, double y)
        { return Interactions.FirstOrDefault(i => i.Contains(x, y)); }
    }
    public class ActiveWork
    {
        public string JobId { get; set; }
        public string Name { get; set; }
        public string Animation { get; set; }
        public DateTime EndsUtc { get; set; }
        public int Reward { get; set; }
        public int Experience { get; set; }
    }
    public class ActiveRest
    {
        public DateTime StartedUtc { get; set; }
        public DateTime EndsUtc { get; set; }
        public double EnergyReward { get; set; }
        public double MoodReward { get; set; }
        public string Animation { get; set; }
    }
    public class SaveData
    {
        public int SchemaVersion { get; set; }
        public int Coins { get; set; }
        public double Satiety { get; set; }
        public double Energy { get; set; }
        public double Mood { get; set; }
        public int Experience { get; set; }
        public Dictionary<string, int> Inventory { get; set; }
        public ActiveWork Work { get; set; }
        public ActiveRest Rest { get; set; }
        public DateTime LastUpdatedUtc { get; set; }
        public DateTime LastTouchUtc { get; set; }
        public double PetSize { get; set; }
        public bool Roaming { get; set; }
        public bool HasPosition { get; set; }
        public double Left { get; set; }
        public double Top { get; set; }
        public List<int> RewardedLevels { get; set; }
        public string CareDate { get; set; }
        public int FeedExperienceToday { get; set; }
        public int TouchExperienceToday { get; set; }

        public static SaveData Fresh(PetConfig config, DateTime now)
        {
            Dictionary<string, int> items = config.Foods.ToDictionary(f => f.Id, f => 0);
            items[config.Foods[0].Id] = 2;
            return new SaveData { SchemaVersion = 3, Coins = 50, Satiety = 70, Energy = 85, Mood = 80,
                Inventory = items, LastUpdatedUtc = now, LastTouchUtc = DateTime.MinValue,
                PetSize = config.DefaultSize, Roaming = false, RewardedLevels = new List<int> { 1 },
                CareDate = now.ToLocalTime().ToString("yyyy-MM-dd"), FeedExperienceToday = 0, TouchExperienceToday = 0 };
        }
        public void Validate()
        {
            if ((SchemaVersion != 1 && SchemaVersion != 2 && SchemaVersion != 3) || Coins < 0 || Experience < 0 || Inventory == null || Inventory.Any(p => p.Value < 0)
                || !ValidStat(Satiety) || !ValidStat(Energy) || !ValidStat(Mood)
                || Double.IsNaN(PetSize) || Double.IsInfinity(PetSize) || PetSize < 120 || PetSize > 600
                || Double.IsNaN(Left) || Double.IsNaN(Top) || Double.IsInfinity(Left) || Double.IsInfinity(Top)
                || LastUpdatedUtc == DateTime.MinValue)
                throw new InvalidDataException("存档格式或数值无效。");
            if (Work != null && (String.IsNullOrWhiteSpace(Work.JobId) || Work.Reward < 0 || Work.Experience < 0 || Work.EndsUtc == DateTime.MinValue))
                throw new InvalidDataException("打工存档无效。");
            if (SchemaVersion >= 2 && (RewardedLevels == null || RewardedLevels.Any(level => level < 1) || RewardedLevels.Distinct().Count() != RewardedLevels.Count
                || FeedExperienceToday < 0 || TouchExperienceToday < 0 || !ValidDate(CareDate)))
                throw new InvalidDataException("成长存档无效。");
            if (Rest != null && (SchemaVersion < 3 || Work != null || Rest.StartedUtc == DateTime.MinValue || Rest.EndsUtc <= Rest.StartedUtc
                || (Rest.EndsUtc - Rest.StartedUtc).TotalSeconds > 86400 || !ValidStat(Rest.EnergyReward) || Rest.EnergyReward <= 0
                || !ValidStat(Rest.MoodReward) || String.IsNullOrWhiteSpace(Rest.Animation)))
                throw new InvalidDataException("休息存档无效。");
        }
        private static bool ValidDate(string value)
        {
            DateTime parsed;
            return DateTime.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out parsed);
        }
        private static bool ValidStat(double value)
        { return !Double.IsNaN(value) && !Double.IsInfinity(value) && value >= 0 && value <= 100; }
    }
    public static class Json
    {
        public static T Read<T>(string text) { return new JavaScriptSerializer().Deserialize<T>(text); }
        public static string Write(object value) { return new JavaScriptSerializer().Serialize(value); }
        public static T Clone<T>(T value) { return Read<T>(Write(value)); }
    }
    public class SaveStore
    {
        public string FilePath { get; private set; }
        public bool RecoveredBackup { get; private set; }
        public bool RecoveredDuringLoad { get; private set; }
        public SaveStore(string path) { FilePath = Path.GetFullPath(path); }
        public SaveData Load(PetConfig config, DateTime now)
        {
            if (!File.Exists(FilePath))
            {
                if (File.Exists(FilePath + ".bak"))
                {
                    SaveData previous = ReadValid(FilePath + ".bak");
                    RecoveredBackup = RecoveredDuringLoad = true;
                    return previous;
                }
                return SaveData.Fresh(config, now);
            }
            try { return ReadValid(FilePath); }
            catch (Exception)
            {
                if (!File.Exists(FilePath + ".bak")) throw new InvalidDataException("存档无法读取。请保留 data 目录，检查 save.json；程序没有重置进度。");
                SaveData previous = ReadValid(FilePath + ".bak");
                RecoveredBackup = RecoveredDuringLoad = true;
                return previous;
            }
        }
        private static SaveData ReadValid(string path)
        {
            SaveData result = Json.Read<SaveData>(File.ReadAllText(path));
            if (result == null) throw new InvalidDataException("空存档。");
            result.Validate();
            return result;
        }
        public void Write(SaveData data)
        {
            data.Validate();
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string temporary = FilePath + ".tmp";
            using (FileStream stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            using (StreamWriter writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
            {
                writer.Write(Json.Write(data));
                writer.Flush();
                stream.Flush(true);
            }
            if (File.Exists(FilePath))
            {
                // A recovered backup must remain valid even if the old primary file was corrupt.
                File.Replace(temporary, FilePath, RecoveredBackup ? null : FilePath + ".bak");
            }
            else File.Move(temporary, FilePath);
            RecoveredBackup = false;
        }
    }
    public class ActionResult
    {
        public bool Ok { get; private set; }
        public string Message { get; private set; }
        public string Animation { get; private set; }
        public string GrowthMessage { get; set; }
        public ActionResult(bool ok, string message, string animation)
        { Ok = ok; Message = message; Animation = animation; }
    }
    public class GameEngine
    {
        public PetConfig Config { get; private set; }
        public SaveData State { get; private set; }
        public string InitialGrowthMessage { get; private set; }
        private readonly SaveStore store;
        public GameEngine(PetConfig config, SaveStore saveStore, DateTime now)
        {
            Config = config; store = saveStore; State = store.Load(config, now);
            if (State.SchemaVersion == 1)
            {
                State.SchemaVersion = 2;
                State.RewardedLevels = new List<int> { 1 };
                State.CareDate = now.ToLocalTime().ToString("yyyy-MM-dd");
                State.FeedExperienceToday = State.TouchExperienceToday = 0;
            }
            if (State.SchemaVersion == 2)
            { State.SchemaVersion = 3; State.Rest = null; }
            foreach (Food food in config.Foods)
                if (!State.Inventory.ContainsKey(food.Id)) State.Inventory[food.Id] = 0;
            Advance(now);
            ResetCareDay(now);
            InitialGrowthMessage = AwardLevelGifts();
            store.Write(State);
        }
        public LevelSpec CurrentLevel { get { return Config.Growth.Levels.Last(level => State.Experience >= level.Experience); } }
        public int Level { get { return CurrentLevel.Level; } }
        public LevelSpec NextLevel { get { return Config.Growth.Levels.FirstOrDefault(level => level.Level > Level); } }
        public int LevelProgress { get { return State.Experience - CurrentLevel.Experience; } }
        public int LevelGoal { get { return NextLevel == null ? 0 : NextLevel.Experience - CurrentLevel.Experience; } }
        public int WorkReward(Job job) { return checked((int)Math.Round(job.Reward * (1 + CurrentLevel.WorkBonusPercent / 100.0), MidpointRounding.AwayFromZero)); }
        public bool WorkReady(DateTime now) { return State.Work != null && now >= State.Work.EndsUtc; }
        public int RemainingSeconds(DateTime now)
        { return State.Work == null ? 0 : Math.Max(0, (int)Math.Ceiling((State.Work.EndsUtc - now).TotalSeconds)); }
        public bool IsResting { get { return State.Rest != null; } }
        public double RestProgress(DateTime now)
        {
            if (!IsResting) return 0;
            double duration = (State.Rest.EndsUtc - State.Rest.StartedUtc).TotalSeconds;
            return Math.Max(0, Math.Min(1, (now - State.Rest.StartedUtc).TotalSeconds / duration));
        }
        public int RestRemainingSeconds(DateTime now)
        {
            if (!IsResting) return 0;
            return (int)Math.Ceiling((State.Rest.EndsUtc - State.Rest.StartedUtc).TotalSeconds * (1 - RestProgress(now)));
        }
        public void Advance(DateTime now)
        {
            ResetCareDay(now);
            // Offline changes are capped at one hour; wall-clock rollback does not duplicate elapsed time.
            if (now <= State.LastUpdatedUtc) return;
            double seconds = Math.Min(3600, (now - State.LastUpdatedUtc).TotalSeconds);
            State.Satiety = Clamp(State.Satiety - seconds / 600);
            // Rest has one explicit reward settlement on waking, including offline time.
            // Do not also grant passive energy or drain the mood while it is active.
            if (!IsResting)
            {
                State.Mood = Clamp(State.Mood - seconds / 1800);
                if (State.Work == null) State.Energy = Clamp(State.Energy + seconds / 180);
            }
            State.LastUpdatedUtc = now;
        }
        public void Save() { store.Write(State); }
        private ActionResult Change(DateTime now, Func<ActionResult> operation)
        {
            SaveData before = Json.Clone(State);
            try
            {
                Advance(now);
                ResetCareDay(now);
                ActionResult result = operation();
                if (result.Ok) result.GrowthMessage = AwardLevelGifts();
                store.Write(State);
                return result;
            }
            catch (Exception) { State = before; throw; }
        }
        public ActionResult Touch(string id, DateTime now)
        {
            return Change(now, delegate {
                if (IsResting) return RestFail("睡觉中……等我醒来再一起玩吧。");
                Interaction interaction = Config.Interactions.FirstOrDefault(i => i.Id == id);
                if (interaction == null) return Fail("这个部位还没有设置互动。");
                if ((now - State.LastTouchUtc).TotalSeconds >= 8)
                {
                    State.Mood = Clamp(State.Mood + interaction.Mood); State.LastTouchUtc = now;
                    if (State.TouchExperienceToday < Config.Growth.TouchDailyCap)
                    { State.Experience = checked(State.Experience + 1); State.TouchExperienceToday++; }
                }
                return new ActionResult(true, interaction.Message, interaction.Animation);
            });
        }
        public ActionResult Buy(string id, DateTime now)
        {
            return Change(now, delegate {
                Food food = Config.Foods.FirstOrDefault(f => f.Id == id);
                if (food == null) return Fail("找不到这份零食。");
                if (Level < food.MinimumLevel) return Fail(food.Name + "会在 Lv." + food.MinimumLevel + " 解锁。");
                if (State.Coins < food.Price) return Fail("金币不够，先去打工赚一点吧。");
                if (State.Inventory[id] >= 999) return Fail("背包里已经有很多了，先吃一点吧。");
                State.Coins -= food.Price; State.Inventory[id]++;
                return new ActionResult(true, "买到一份" + food.Name + "，已放入背包。", "wave");
            });
        }
        public ActionResult Feed(string id, DateTime now)
        {
            return Change(now, delegate {
                Food food = Config.Foods.FirstOrDefault(f => f.Id == id);
                if (food == null) return Fail("找不到这份零食。");
                if (IsResting) return RestFail("还在睡觉，先叫醒我再吃点心吧。");
                if (State.Work != null) return Fail("先完成打工并领取报酬，再回来吃点心。");
                if (State.Inventory[id] <= 0) return Fail("背包里没有了，去零食铺买一份吧。");
                if (State.Satiety >= 99.99) return Fail("已经吃饱啦，晚点再吃。");
                State.Inventory[id]--; State.Satiety = Clamp(State.Satiety + food.Satiety);
                State.Energy = Clamp(State.Energy + food.Energy); State.Mood = Clamp(State.Mood + food.Mood);
                int experience = Math.Min(food.Experience, Math.Max(0, Config.Growth.FeedDailyCap - State.FeedExperienceToday));
                State.Experience = checked(State.Experience + experience); State.FeedExperienceToday += experience;
                string message = "吃了" + food.Name + "，谢谢你！" + (experience > 0 ? " 经验 +" + experience : " 今天的喂食经验已满，照顾仍然有效。");
                return new ActionResult(true, message, food.Animation);
            });
        }
        public ActionResult StartWork(string id, DateTime now)
        {
            return Change(now, delegate {
                Job job = Config.Jobs.FirstOrDefault(j => j.Id == id);
                if (job == null) return Fail("找不到这份工作。");
                if (IsResting) return RestFail("还在睡觉，先叫醒我再去打工吧。");
                if (Level < job.MinimumLevel) return Fail(job.Name + "会在 Lv." + job.MinimumLevel + " 解锁。");
                if (State.Work != null) return Fail("已有一份工作，完成后先领取报酬。");
                if (State.Energy < job.EnergyCost || State.Satiety < job.SatietyCost + 10)
                    return Fail("体力或饱腹不足，先喂食或者休息一会儿。");
                State.Energy -= job.EnergyCost; State.Satiety -= job.SatietyCost;
                State.Work = new ActiveWork { JobId = job.Id, Name = job.Name, Animation = job.Animation,
                    EndsUtc = now.AddSeconds(job.Seconds), Reward = WorkReward(job), Experience = job.Experience };
                return new ActionResult(true, "出发去" + job.Name + "！完成后记得领取报酬。", job.Animation);
            });
        }
        public ActionResult Collect(DateTime now)
        {
            return Change(now, delegate {
                if (State.Work == null) return Fail("现在没有待领取的报酬。");
                if (!WorkReady(now)) return Fail("还在打工，请稍等。剩余 " + RemainingSeconds(now) + " 秒。");
                int reward = State.Work.Reward;
                int experience = State.Work.Experience;
                State.Coins = checked(State.Coins + reward);
                State.Experience = checked(State.Experience + experience);
                State.Work = null;
                return new ActionResult(true, "打工完成，赚到 " + reward + " 金币！经验 +" + experience, "jump");
            });
        }
        public ActionResult StartRest(DateTime now)
        {
            return Change(now, delegate {
                if (IsResting) return RestFail("已经在睡觉啦，想结束休息可以点“叫醒”。");
                if (State.Work != null) return Fail("先完成打工并领取报酬，再回来睡觉。");
                if (State.Energy >= 99.99) return Fail("体力已经充足，先一起玩一会儿吧。");
                RestSpec rest = Config.Rest;
                State.Rest = new ActiveRest { StartedUtc = now, EndsUtc = now.AddSeconds(rest.Seconds),
                    EnergyReward = rest.EnergyReward, MoodReward = rest.MoodReward, Animation = rest.Animation };
                return new ActionResult(true, "睡个小觉，醒来恢复体力和心情。随时可以叫醒我。", rest.Animation);
            });
        }
        public ActionResult Wake(DateTime now)
        {
            return Change(now, delegate {
                if (!IsResting) return Fail("现在没有在睡觉。");
                double progress = RestProgress(now);
                double energyBefore = State.Energy, moodBefore = State.Mood;
                State.Energy = Clamp(State.Energy + State.Rest.EnergyReward * progress);
                State.Mood = Clamp(State.Mood + State.Rest.MoodReward * progress);
                State.Rest = null;
                string prefix = progress < 1 ? "提前醒来啦！" : "睡好啦！";
                string message = prefix + "体力 +" + (State.Energy - energyBefore).ToString("0.#")
                    + "，心情 +" + (State.Mood - moodBefore).ToString("0.#") + "。";
                return new ActionResult(true, message, "wave");
            });
        }
        private void ResetCareDay(DateTime now)
        {
            string today = now.ToLocalTime().ToString("yyyy-MM-dd");
            // Moving the clock backwards does not reset care limits repeatedly.
            if (String.CompareOrdinal(today, State.CareDate) > 0)
            { State.CareDate = today; State.FeedExperienceToday = State.TouchExperienceToday = 0; }
        }
        private string AwardLevelGifts()
        {
            int gift = 0; bool awarded = false;
            foreach (LevelSpec level in Config.Growth.Levels.Where(level => level.Experience <= State.Experience))
            {
                if (State.RewardedLevels.Contains(level.Level)) continue;
                State.Coins = checked(State.Coins + level.GiftCoins); gift = checked(gift + level.GiftCoins);
                State.RewardedLevels.Add(level.Level); awarded = true;
            }
            return awarded ? "升到 Lv." + Level + " " + CurrentLevel.Title + "！成长礼 +" + gift + " 金币。" : null;
        }
        public void SetAppearance(double size, bool roaming, double left, double top)
        {
            SaveData before = Json.Clone(State);
            try { State.PetSize = Math.Max(160, Math.Min(420, size)); State.Roaming = roaming;
                State.Left = left; State.Top = top; State.HasPosition = true; store.Write(State); }
            catch (Exception) { State = before; throw; }
        }
        private static double Clamp(double value) { return Math.Max(0, Math.Min(100, value)); }
        private ActionResult RestFail(string message) { return new ActionResult(false, message, State.Rest.Animation); }
        private static ActionResult Fail(string message) { return new ActionResult(false, message, "upset"); }
    }
}
