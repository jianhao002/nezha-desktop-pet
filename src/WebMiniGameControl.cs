using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace NezhaDesktop.MiniGames
{
    public sealed class WebGameSession { public int Version { get; set; } public string Json { get; set; } }
    // SDK-specific types remain private; trusted C# game wrappers use the existing v1 API.
    public class WebMiniGameControl : UserControl, IMiniGame, IMiniGameSession
    {
        private WebView2CompositionControl web;
        private MiniGameContext host;
        private Grid layout;
        private TextBlock message;
        private bool disposed, ready, initialized, hostPaused = true, saving, resumeBlocked;
        private int requestId;
        private TaskCompletionSource<string> saveRequest;
        private string initial;
        private string startupScriptId;
        public FrameworkElement View { get { return this; } }
        public bool CanResume { get { return true; } }
        public bool ReadyForTest { get { return ready; } }
        public bool SavingForTest { get { return saving; } }
        public string LastError { get; private set; }
        private static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = 262144, RecursionLimit = 48 }; }
        public void Initialize(MiniGameContext context)
        {
            host = context; Focusable = true; layout = new Grid(); Content = layout;
            message = new TextBlock { Text = "正在准备格子王国…", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10), Foreground = Brushes.DarkSlateGray };
            layout.Children.Add(message);
            try {
                string json = host.LoadSession();
                if (json != null) { WebGameSession record = Json().Deserialize<WebGameSession>(json); MiniGameSession.Require(record != null && record.Version == 1 && record.Json != null && record.Json.Length <= 120000); Json().DeserializeObject(record.Json); initial = record.Json; }
            } catch (Exception ex) { resumeBlocked = true; host.Notify("续局未读取：" + ex.Message + "；旧文件保留，请在游戏内选择新游戏后重新保存。"); }
            Loaded += InitializeBrowser;
        }
        private async void InitializeBrowser(object sender, RoutedEventArgs args)
        {
            if (initialized || disposed) return; initialized = true;
            try {
                string data = Path.GetDirectoryName(Path.GetDirectoryName(host.StorageDirectory));
                WebRuntime.Prepare(data); string assets = WebRuntime.Assets(data);
                web = new WebView2CompositionControl { DefaultBackgroundColor = System.Drawing.Color.White };
                layout.Children.Insert(0, web);
                string profile = Path.Combine(host.StorageDirectory, "web-profile"); UpdateSafety.AssertNoReparsePath(profile);
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, profile, null);
                if (disposed) return;
                await web.EnsureCoreWebView2Async(environment);
                if (disposed) return;
                CoreWebView2 core = web.CoreWebView2;
                core.Settings.AreDevToolsEnabled = false; core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += delegate(object s, CoreWebView2WebResourceRequestedEventArgs e) {
                    Uri resource;
                    if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out resource) || resource.Scheme != "https" || resource.Host != "gridland.nezha.invalid" || !resource.IsDefaultPort)
                        e.Response = environment.CreateWebResourceResponse(new MemoryStream(), 403, "Local game only", "Content-Type: text/plain");
                };
                core.SetVirtualHostNameToFolderMapping("gridland.nezha.invalid", assets, CoreWebView2HostResourceAccessKind.DenyCors);
                core.NavigationStarting += delegate(object s, CoreWebView2NavigationStartingEventArgs e) {
                    Uri address;
                    if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out address) || address.Scheme != "https" || address.Host != "gridland.nezha.invalid" || !address.IsDefaultPort) e.Cancel = true;
                };
                core.NewWindowRequested += delegate(object s, CoreWebView2NewWindowRequestedEventArgs e) { e.Handled = true; host.Notify("外部链接不会在游戏中打开。"); };
                core.PermissionRequested += delegate(object s, CoreWebView2PermissionRequestedEventArgs e) { e.State = CoreWebView2PermissionState.Deny; };
                core.WebMessageReceived += Receive;
                core.ProcessFailed += delegate { ready = false; LastError = "网页游戏进程异常，请返回列表后再打开；已保存的本局保留。"; message.Text = LastError; message.Visibility = Visibility.Visible; };
                startupScriptId = await core.AddScriptToExecuteOnDocumentCreatedAsync(StartupScript());
                if (!disposed) core.Navigate("https://gridland.nezha.invalid/index.html?silent");
            } catch (Exception ex) {
                if (disposed) return; LastError = ex.Message;
                message.Text = "网页游戏暂时无法启动。请确认已安装 Microsoft Edge WebView2 Runtime，然后返回游戏列表重试。\n" + ex.Message;
                host.Notify("格子王国未启动；其余小游戏仍可使用。");
            }
        }
        private string StartupScript()
        { return "window.nezhaHost=true;window.nezhaHostPaused=" + (hostPaused ? "true" : "false") + ";window.nezhaResumeBlocked=" + (resumeBlocked ? "true" : "false") + ";window.nezhaInitialSession=JSON.parse(" + Json().Serialize(initial ?? "null") + ");"; }
        private async void RetryBrowser()
        {
            if (!ready || disposed) return;
            ready = false; initial = null;
            try {
                // Reset pending animation callbacks by reloading only the local game page.
                // Saving skips navigation; the persisted death marker already ends the old battle.
                CoreWebView2 core = web.CoreWebView2;
                core.RemoveScriptToExecuteOnDocumentCreated(startupScriptId);
                startupScriptId = await core.AddScriptToExecuteOnDocumentCreatedAsync(StartupScript());
                if (!disposed) core.Reload();
            } catch (Exception ex) {
                if (disposed) return;
                LastError = ex.Message;
                message.Text = "重试暂未启动，请返回游戏列表后再打开；已保存的成长保留。";
                message.Visibility = Visibility.Visible; host.Notify(message.Text);
            }
        }
        private void Receive(object sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            if (disposed || args.Source != "https://gridland.nezha.invalid/index.html?silent") return;
            try {
                Dictionary<string, object> note = Json().Deserialize<Dictionary<string, object>>(args.WebMessageAsJson);
                string kind = Convert.ToString(note["kind"]);
                if (kind == "ready") { ready = true; message.Visibility = Visibility.Collapsed; SetHostPaused(hostPaused); host.Notify("离线格子王国 · 点击相邻格子交换；恢复后点击继续。"); }
                else if (kind == "saved" && saveRequest != null && Convert.ToInt32(note["id"]) == requestId) saveRequest.TrySetResult(Convert.ToString(note["json"]));
                else if (kind == "save-error" && saveRequest != null && Convert.ToInt32(note["id"]) == requestId) saveRequest.TrySetException(new IOException(Convert.ToString(note["message"])));
                else if (kind == "new-session") { resumeBlocked = false; initial = null; }
                else if (kind == "resume-error") { resumeBlocked = true; host.Notify("续局格式不兼容，旧文件暂保留。请在游戏中点击新游戏重新开始。"); }
                else if (kind == "ended") { host.StoreSession(null); initial = null; }
                else if (kind == "retry") { RetryBrowser(); }
                else if (kind == "error") { LastError = Convert.ToString(note["message"]); host.Notify("游戏提示：" + LastError); }
            } catch (Exception ex) { LastError = ex.Message; if (saveRequest != null) saveRequest.TrySetException(ex); else host.Notify("游戏消息未处理：" + ex.Message); }
        }
        public void SetHostPaused(bool paused)
        {
            hostPaused = paused; if (!ready || disposed) return;
            Send("window.NezhaDesktop&&NezhaDesktop.setHostPaused(" + (paused ? "true" : "false") + ")");
        }
        private async void Send(string script)
        { try { if (web != null && web.CoreWebView2 != null && !disposed) await web.CoreWebView2.ExecuteScriptAsync(script); } catch (Exception ex) { if (!disposed) { LastError = ex.Message; host.Notify("游戏未响应：" + ex.Message); } } }
        public void SaveSession()
        {
            if (disposed || !ready || resumeBlocked) return;
            if (saving) throw new IOException("游戏正在保存，请稍候重试。");
            saving = true;
            try {
                saveRequest = new TaskCompletionSource<string>(); int id = ++requestId;
                Send("NezhaDesktop.save(" + id + ")");
                string json = Await(saveRequest.Task, 10000);
                if (json == "null") { host.StoreSession(null); initial = null; }
                else if (!String.IsNullOrEmpty(json) && json != "menu") {
                    if (json.Length > 120000) throw new IOException("格子王国本局记录过大，当前游戏保留。");
                    host.StoreSession(new WebGameSession { Version = 1, Json = json }); initial = json;
                }
            } finally { saveRequest = null; saving = false; }
        }
        // Pump the WPF dispatcher while the renderer settles an in-flight tile animation.
        // This preserves the synchronous v1 close/save contract without deadlocking WebView2 callbacks.
        private static T Await<T>(Task<T> task, int milliseconds)
        {
            DispatcherFrame frame = new DispatcherFrame(); DateTime deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
            DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += delegate { if (task.IsCompleted || DateTime.UtcNow >= deadline) frame.Continue = false; };
            try { if (!task.IsCompleted) { timer.Start(); Dispatcher.PushFrame(frame); } }
            finally { timer.Stop(); }
            if (!task.IsCompleted) throw new IOException("本局保存超时，请保留窗口并重试。");
            return task.GetAwaiter().GetResult();
        }
        public Task<string> ExecuteForTest(string script) { return web.CoreWebView2.ExecuteScriptAsync(script); }
        public Task CaptureForTest(string file) { return Capture(file); }
        private async Task Capture(string file) { using (FileStream output = File.Create(file)) await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, output); }
        public void Dispose()
        {
            if (disposed) return; disposed = true; ready = false; Loaded -= InitializeBrowser;
            if (saveRequest != null) saveRequest.TrySetException(new IOException("游戏已关闭。"));
            if (web != null) web.Dispose();
        }
    }
}
