using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace NezhaDesktop
{
    public class UpdateWindow : Window
    {
        private readonly PetWindow pet;
        private readonly string root;
        private readonly TextBlock status;
        private readonly ProgressBar progress;
        private readonly Button check;
        private bool busy, applying;
        public UpdateWindow(PetWindow pet, string root)
        {
            this.pet = pet; this.root = root;
            Title = "焰轮哪吒 · 版本更新"; Width = 430; Height = 290;
            ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Ui.Cream; FontFamily = Ui.Font;
            StackPanel body = new StackPanel { Margin = new Thickness(24) };
            body.Children.Add(Ui.Text("让哪吒学会新本领", 22, Ui.Red));
            TextBlock version = Ui.Text("当前版本 v" + pet.Engine.Config.Version + "\n下载完成后自动重启，养成进度保留。", 12, Ui.Muted);
            version.Margin = new Thickness(0, 8, 0, 15); body.Children.Add(version);
            status = Ui.Text("检查已发布的新版本。", 13, Ui.Ink); status.MinHeight = 40; body.Children.Add(status);
            progress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 8, Margin = new Thickness(0, 10, 0, 16), Foreground = Ui.Red }; body.Children.Add(progress);
            check = Ui.Button("检查并更新", StartUpdate, true); body.Children.Add(check); Content = body;
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if (busy && !applying) e.Cancel = true; };
        }
        private async void StartUpdate()
        {
            if (busy) return; busy = true; check.IsEnabled = false; progress.IsIndeterminate = true;
            try
            {
                status.Text = "正在检查已发布版本…";
                UpdateClient client = new UpdateClient(root);
                UpdateCheck result = await Task.Run(delegate { return client.Check(pet.Engine.Config.Version); });
                if (!result.Available) { status.Text = "已经是最新版本 v" + pet.Engine.Config.Version + "。"; return; }
                status.Text = "正在下载 v" + result.LatestVersion + "…"; progress.IsIndeterminate = false;
                PreparedUpdate prepared = await Task.Run(delegate {
                    return client.Download(result, delegate(int value, string message) {
                        Dispatcher.BeginInvoke(new Action(delegate { progress.Value = value; status.Text = message; }));
                    });
                });
                status.Text = "下载校验通过，正在保存进度并重启…";
                pet.PrepareForUpdate();
                try { client.Launch(prepared, Process.GetCurrentProcess().Id); }
                catch { pet.CancelPreparedUpdate(); throw; }
                applying = true; pet.Close();
            }
            catch (Exception ex)
            {
                WebException network = ex as WebException;
                HttpWebResponse response = network == null ? null : network.Response as HttpWebResponse;
                status.Text = response != null && response.StatusCode == HttpStatusCode.NotFound
                    ? "仓库还没有发布更新包。当前版本可以继续使用。"
                    : "更新未完成：" + ex.Message + "\n当前版本和存档保留。";
            }
            finally { busy = false; check.IsEnabled = true; progress.IsIndeterminate = false; }
        }
    }
}
