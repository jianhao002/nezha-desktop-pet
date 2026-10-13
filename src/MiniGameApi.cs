using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;

namespace NezhaDesktop.MiniGames
{
    // Version 1 deliberately exposes no mutable GameEngine or currency operations.
    public interface IMiniGame : IDisposable
    {
        FrameworkElement View { get; }
        void Initialize(MiniGameContext context);
        void SetHostPaused(bool paused);
    }

    // Optional capability: existing local extensions keep the v1 interface.
    public interface IMiniGameSession
    {
        bool CanResume { get; }
        void SaveSession();
    }
    public sealed class MiniGameSessionRecord
    {
        public int Version { get; set; }
        public string State { get; set; }
    }
    public static class MiniGameSession
    {
        public static T Read<T>(string json) where T : class
        {
            Require(json != null && json.Length <= 262144);
            T state = new JavaScriptSerializer { MaxJsonLength = 262144, RecursionLimit = 32 }.Deserialize<T>(json);
            Require(state != null); return state;
        }
        public static void Require(bool valid) { if (!valid) throw new InvalidDataException("本局存档格式或数值无效。"); }
        public static bool Range(int[] items, int count, int min, int max)
        { return items != null && items.Length == count && Array.TrueForAll(items, v => v >= min && v <= max); }
    }

    public sealed class PetSnapshot
    {
        public readonly string Name;
        public readonly int Level;
        public readonly double Energy, Mood;
        public PetSnapshot(string name, int level, double energy, double mood)
        { Name = name; Level = level; Energy = energy; Mood = mood; }
    }

    public sealed class MiniGameContext
    {
        private readonly Func<PetSnapshot> snapshot;
        private readonly Action<string> notice;
        private readonly string directory;
        private Dictionary<string, int> records;
        public string GameDirectory { get; private set; }
        public PetSnapshot Pet { get { return snapshot == null ? null : snapshot(); } }
        public MiniGameContext(string gameDirectory, string dataDirectory, Func<PetSnapshot> snapshot, Action<string> notice)
        {
            GameDirectory = Path.GetFullPath(gameDirectory); directory = Path.GetFullPath(dataDirectory);
            this.snapshot = snapshot; this.notice = notice;
        }
        public void Notify(string message)
        { if (notice != null && !String.IsNullOrWhiteSpace(message)) notice(message.Length > 180 ? message.Substring(0, 180) : message); }
        public int Best(string key, int fallback)
        {
            ValidateKey(key); EnsureRecords(); int value;
            return records.TryGetValue(key, out value) ? value : fallback;
        }
        public bool Record(string key, int value, bool lowerIsBetter)
        {
            ValidateKey(key); if (value < 0) throw new ArgumentOutOfRangeException("value");
            EnsureRecords(); int old;
            if (records.TryGetValue(key, out old) && (lowerIsBetter ? value >= old : value <= old)) return false;
            Dictionary<string, int> candidate = new Dictionary<string, int>(records); candidate[key] = value;
            MiniGameFiles.WriteJson(Path.Combine(directory, "records.json"), candidate); records = candidate; return true;
        }
        private static void ValidateKey(string key)
        { if (key == null || !Regex.IsMatch(key, "^[a-z0-9-]{1,48}$")) throw new ArgumentException("成绩名称无效。"); }
        private void EnsureRecords()
        {
            if (records != null) return;
            records = MiniGameFiles.ReadJson<Dictionary<string, int>>(Path.Combine(directory, "records.json")) ?? new Dictionary<string, int>();
        }
        public string LoadSession()
        {
            string path = Path.Combine(directory, "session.json");
            MiniGameSessionRecord record = MiniGameFiles.ReadJson<MiniGameSessionRecord>(path);
            if (record == null) {
                if (File.Exists(path) || File.Exists(path + ".bak")) throw new InvalidDataException("本局存档无法读取。");
                return null;
            }
            MiniGameSession.Require(record.Version == 1); return record.State;
        }
        public void StoreSession(object state)
        {
            string json = state == null ? null : new JavaScriptSerializer { MaxJsonLength = 262144, RecursionLimit = 32 }.Serialize(state);
            // A null payload is an explicit finished-game marker; never resurrect .bak.
            MiniGameFiles.WriteJson(Path.Combine(directory, "session.json"), new MiniGameSessionRecord { Version = 1, State = json });
        }
    }

    public abstract class MiniGameControl : UserControl, IMiniGame, IMiniGameSession
    {
        protected MiniGameContext Host { get; private set; }
        public FrameworkElement View { get { return this; } }
        private bool resumeBlocked;
        private string lastSaved;
        public virtual bool CanResume { get { return false; } }
        public void Initialize(MiniGameContext context)
        {
            Host = context; Content = Build(); Focusable = true;
            if (!CanResume) return;
            try { string state = Host.LoadSession(); if (state != null) { SetHostPaused(true); RestoreSession(state); Host.Notify("已继续上一局；动态游戏点击继续后开始。"); } }
            catch (Exception ex) { resumeBlocked = true; Host.Notify("续局未读取：" + ex.Message + " 点击新一局可重新开始，旧存档暂保留。"); }
        }
        protected void NewSession() { resumeBlocked = false; lastSaved = null; }
        protected virtual object CaptureSession() { return null; }
        protected virtual void RestoreSession(string json) { }
        public void SaveSession()
        {
            if (!CanResume || resumeBlocked) return;
            object state = CaptureSession(); string stamp = new JavaScriptSerializer().Serialize(state);
            if (stamp == lastSaved) return;
            Host.StoreSession(state); lastSaved = stamp;
        }
        protected abstract UIElement Build();
        public abstract void SetHostPaused(bool paused);
        public abstract void Dispose();
    }

    internal static class MiniGameFiles
    {
        internal static T ReadJson<T>(string file) where T : class
        {
            foreach (string candidate in new[] { file, file + ".bak" })
            {
                try {
                    UpdateSafety.AssertNoReparsePath(candidate);
                    if (File.Exists(candidate) && new FileInfo(candidate).Length <= 262144)
                        return new JavaScriptSerializer { MaxJsonLength = 262144 }.Deserialize<T>(File.ReadAllText(candidate, Encoding.UTF8));
                } catch (IOException) { } catch (UnauthorizedAccessException) { } catch (ArgumentException) { } catch (InvalidOperationException) { }
            }
            return null;
        }
        internal static void WriteJson(string file, object value)
        {
            UpdateSafety.AssertNoReparsePath(file); Directory.CreateDirectory(Path.GetDirectoryName(file));
            string temp = file + ".tmp-" + Guid.NewGuid().ToString("N");
            try {
                byte[] bytes = new UTF8Encoding(false).GetBytes(new JavaScriptSerializer().Serialize(value));
                if (bytes.Length > 262144) throw new InvalidDataException("小游戏记录过大。");
                using (FileStream stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (File.Exists(file)) File.Replace(temp, file, file + ".bak"); else File.Move(temp, file);
            } finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
