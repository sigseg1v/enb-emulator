using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using MsBoxIcon = MsBox.Avalonia.Enums.Icon;
using CommonTools.Cryptography;

namespace EnbPatcherAvalonia
{
    // Avalonia port of tools/enbpatcher/Form1.cs (which had no csproj
    // and was unbuildable). Same shape as toolspatcher-avalonia — same
    // upstream author wrote both — but patches the game client itself,
    // not the editor suite.
    //
    // Differences from toolspatcher: patch URL, exe self-name, target
    // launcher, and the game's bin dir (c:\net7\bin vs c:\net7\tools).
    public partial class MainWindow : Window
    {
        const string Net7PatchUrl = "http://patch.net-7.org/";
        const string Me           = "./EnBPatcher.exe";
        const string MeAlt        = "./EnBPatcher1.exe";
        const string LauncherExe  = "./LaunchNet7.exe";
        const string GameBinDir   = "c:\\net7\\bin";

        readonly HttpClient _http = new HttpClient();
        readonly CancellationTokenSource _cts = new CancellationTokenSource();

        string _currentVer;
        string _serverVer;
        string _fileList;

        public MainWindow()
        {
            InitializeComponent();
            Opened += async (_, _) => await Task.Run(() => RunUpdate(_cts.Token));
            Closing += (_, _) => _cts.Cancel();
        }

        async Task<string> FetchTextAsync(string relative, CancellationToken ct)
            => await _http.GetStringAsync(Net7PatchUrl + relative, ct);

        async Task<bool> DownloadFileAsync(string fileName, string copyName, CancellationToken ct)
        {
            try
            {
                using var resp = await _http.GetAsync(
                    Net7PatchUrl + fileName,
                    HttpCompletionOption.ResponseHeadersRead,
                    ct);
                resp.EnsureSuccessStatusCode();

                long total = resp.Content.Headers.ContentLength ?? -1;

                var parts = fileName.Split('/');
                if (parts.Length > 1)
                {
                    string cur = ".";
                    for (int i = 0; i < parts.Length - 1; i++)
                    {
                        cur = Path.Combine(cur, parts[i]);
                        Directory.CreateDirectory(cur);
                    }
                }

                using var local = new FileStream(copyName, FileMode.Create, FileAccess.Write, FileShare.None);
                using var src   = await resp.Content.ReadAsStreamAsync(ct);
                var buffer = new byte[2048];
                int n;
                while ((n = await src.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                {
                    await local.WriteAsync(buffer, 0, n, ct);
                    PostProgress(local.Length, total);
                }
                return true;
            }
            catch (Exception ex)
            {
                await ShowErrorAsync($"Error Patching File \"{fileName}\"\nMake sure application is shut down!\n\n{ex.Message}");
                return false;
            }
        }

        async Task GetServerVersionAsync(CancellationToken ct)
        {
            var text = await FetchTextAsync("Version.txt", ct);
            _serverVer = text.Split('\n', 2)[0].TrimEnd('\r');
        }

        void GetClientVersion()
        {
            try { _currentVer = File.ReadAllLines("Version.txt")[0]; }
            catch { _currentVer = "No Version"; }
        }

        async Task<bool> NeedUpdateAsync(CancellationToken ct)
        {
            await GetServerVersionAsync(ct);
            GetClientVersion();
            if (_serverVer.CompareTo(_currentVer) == 0) return false;
            _fileList = await FetchTextAsync("Files.txt", ct);
            return true;
        }

        async Task RunUpdate(CancellationToken ct)
        {
            try
            {
                var myExe = Path.GetFileName(Environment.ProcessPath ?? "");
                if (string.Equals(myExe, Path.GetFileName(MeAlt), StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Copy(MeAlt, Me, true); }
                    catch
                    {
                        try { File.Delete("./Version.txt"); } catch { }
                        try { File.Delete(Me + ".crc"); } catch { }
                        await ShowErrorAsync("Error: Can't copy patcher!\nRestart patcher");
                        await CloseFromBgAsync();
                        return;
                    }
                }
                else
                {
                    try { if (File.Exists(MeAlt)) File.Delete(MeAlt); } catch { /* not fatal */ }
                }

                int status = 1;
                if (!await NeedUpdateAsync(ct))
                {
                    await UpdateCompleteAsync(status);
                    return;
                }

                var lines = _fileList.Split('\n');
                var toPatch = new System.Text.StringBuilder();
                foreach (var line in lines)
                {
                    var parts = line.Split('\t');
                    if (parts.Length < 2 || string.IsNullOrEmpty(parts[0])) continue;

                    string crc32;
                    try { crc32 = $"{Crc32.GetFileCRC32(parts[0]):X8}"; }
                    catch
                    {
                        crc32 = "";
                        if (parts[0] == Me)
                        {
                            try { crc32 = File.ReadAllLines(Me + ".crc")[0]; }
                            catch { crc32 = ""; }
                        }
                    }

                    if (crc32.CompareTo(parts[1]) != 0)
                        toPatch.AppendLine($"{parts[0]}\t{parts[1]}");
                }

                var patchLines = toPatch.ToString().Split('\n');
                int loop = 0;
                int totalCount = 0;
                foreach (var l in patchLines) if (!string.IsNullOrEmpty(l)) totalCount++;

                foreach (var line in patchLines)
                {
                    if (string.IsNullOrEmpty(line)) break;
                    var parts = line.Split('\t');
                    loop++;
                    PostFileName(parts[0]);

                    string dest = parts[0] == Me ? MeAlt : parts[0];
                    if (!await DownloadFileAsync(parts[0], dest, ct)) return;

                    if (parts[0] == Me) status = 2;

                    uint crc = Crc32.GetFileCRC32(dest);
                    string crc32 = $"{crc:X8}";
                    if (crc32.CompareTo(parts[1]) != 0)
                    {
                        await ShowErrorAsync($"CRC32 Error in File {parts[0]}\nPlease run the patcher Again");
                        return;
                    }
                    if (parts[0] == Me)
                        File.WriteAllText(Me + ".crc", parts[1] + Environment.NewLine);

                    PostTotalProgress(loop, totalCount);
                }

                await DownloadFileAsync("Version.txt", "Version.txt", ct);
                await ShowInfoAsync("All Files are Updated");
                await UpdateCompleteAsync(status);
            }
            catch (OperationCanceledException) { /* shutdown */ }
            catch (Exception ex)
            {
                await ShowErrorAsync("Patcher fault: " + ex.Message);
            }
        }

        async Task UpdateCompleteAsync(int patchStatus)
        {
            if (patchStatus == 2)
            {
                if (File.Exists(MeAlt))
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = Path.Combine(Directory.GetCurrentDirectory(), MeAlt),
                        UseShellExecute = true,
                    });
                else
                    await ShowErrorAsync($"Error: Can't Find file {MeAlt}");
            }
            else if (patchStatus == 1)
            {
                if (Directory.Exists(GameBinDir))
                    Directory.SetCurrentDirectory(GameBinDir);
                if (File.Exists(LauncherExe))
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = Path.Combine(Directory.GetCurrentDirectory(), LauncherExe),
                        Arguments = "patcher",
                        UseShellExecute = true,
                    });
                else
                    await ShowErrorAsync($"Error: Can't Find file {LauncherExe}");
            }
            await CloseFromBgAsync();
        }

        void PostProgress(long bytesRead, long total)
            => Dispatcher.UIThread.Post(() =>
            {
                int pct = total > 0 ? (int)((bytesRead * 100L) / total) : 0;
                FileProgress.Value = pct;
                ProgressValue.Text = pct + "%";
            });

        void PostFileName(string name)
            => Dispatcher.UIThread.Post(() => CFileName.Text = "File Downloading: " + name);

        void PostTotalProgress(int loop, int total)
            => Dispatcher.UIThread.Post(() =>
            {
                int pct = total > 0 ? (int)((loop / (float)total) * 100) : 0;
                TotalProgress.Value = pct;
                TotalProgressValue.Text = pct + "%";
            });

        Task ShowInfoAsync(string msg)
            => MessageBoxManager.GetMessageBoxStandard("Patching Done", msg, ButtonEnum.Ok, MsBoxIcon.Info)
                                .ShowWindowDialogAsync(this);

        Task ShowErrorAsync(string msg)
            => MessageBoxManager.GetMessageBoxStandard("Error", msg, ButtonEnum.Ok, MsBoxIcon.Error)
                                .ShowWindowDialogAsync(this);

        Task CloseFromBgAsync()
        {
            var tcs = new TaskCompletionSource();
            Dispatcher.UIThread.Post(() => { Close(); tcs.SetResult(); });
            return tcs.Task;
        }
    }
}
