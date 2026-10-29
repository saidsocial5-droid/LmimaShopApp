using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Serilog;

namespace LmimaShopApp
{
    /// <summary>
    /// التحديث التلقائي من إصدارات GitHub (مستودع خاص):
    /// - البرنامج: يُنزَّل ملف LmimaShopApp-win-x64.zip ويُتحقق من بصمته SHA-256، ثم يُثبَّت بعد موافقة المستخدم
    ///   مع نسخة احتياطية لقاعدة البيانات وللملفات القديمة. لا يُلمس settings.json ولا ProductImages ولا السجلات.
    /// - تطبيق الهاتف: يُنزَّل LmimaShop.apk تلقائياً ويقدمه الخادم المحلي للهواتف عبر /api/version و /LmimaShop.apk.
    /// </summary>
    public static class UpdateService
    {
        public const string DesktopAssetName = "LmimaShopApp-win-x64.zip";
        public const string ApkAssetName = "LmimaShop.apk";
        public const string ChecksumsAssetName = "SHA256SUMS.txt";

        private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
        private static readonly string AppDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LmimaShop");
        private static readonly string TokenFile = Path.Combine(AppDataDir, "update_token.dat");
        private static readonly string UpdatesDir = Path.Combine(AppDataDir, "updates");

        public static string ServedApkPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LmimaShop.apk");
        private static string ServedApkInfoPath => ServedApkPath + ".json";

        private static readonly SemaphoreSlim _checkLock = new SemaphoreSlim(1, 1);
        private static Timer? _timer;
        private static string? _declinedVersion;

        public static string Repository =>
            Environment.GetEnvironmentVariable("LMIMA_UPDATE_REPO") ?? AppVersion.GetMetadata("UpdateRepo") ?? "";

        #region Token

        /// <summary>
        /// مفتاح القراءة: من ملف مشفر محلياً (DPAPI) أولاً، ثم المفتاح المدمج عند البناء في GitHub Actions
        /// </summary>
        private static string GetToken()
        {
            try
            {
                if (File.Exists(TokenFile))
                {
                    byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(TokenFile), null, DataProtectionScope.CurrentUser);
                    string t = Encoding.UTF8.GetString(plain).Trim();
                    if (!string.IsNullOrEmpty(t)) return t;
                }
            }
            catch (Exception ex)
            {
                Log.Warning("Could not read local update token: {Msg}", ex.Message);
            }

            string? env = Environment.GetEnvironmentVariable("LMIMA_UPDATE_TOKEN");
            if (!string.IsNullOrWhiteSpace(env)) return env.Trim();

            return AppVersion.GetMetadata("UpdateToken") ?? "";
        }

        public static void SaveToken(string token)
        {
            Directory.CreateDirectory(AppDataDir);
            byte[] enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(token.Trim()), null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(TokenFile, enc);
        }

        public static bool IsConfigured => !string.IsNullOrWhiteSpace(Repository) && !string.IsNullOrWhiteSpace(GetToken());

        #endregion

        #region Served APK info

        public static string GetServedApkVersion()
        {
            try
            {
                if (File.Exists(ServedApkInfoPath))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(ServedApkInfoPath));
                    if (doc.RootElement.TryGetProperty("version", out var v)) return v.GetString() ?? AppVersion.Current;
                }
            }
            catch { }
            return AppVersion.Current;
        }

        public static string GetServedApkNotes()
        {
            try
            {
                if (File.Exists(ServedApkInfoPath))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(ServedApkInfoPath));
                    if (doc.RootElement.TryGetProperty("notes", out var n) && !string.IsNullOrWhiteSpace(n.GetString())) return n.GetString()!;
                }
            }
            catch { }
            return "يتوفر إصدار جديد من تطبيق LMIMA SHOP";
        }

        #endregion

        public static void StartBackgroundChecks()
        {
            if (_timer != null) return;
            if (!IsConfigured)
            {
                Log.Information("Auto-update disabled: no repository/token configured");
                return;
            }
            _timer = new Timer(_ => _ = CheckForUpdatesAsync(interactive: false), null, TimeSpan.FromSeconds(45), CheckInterval);
        }

        /// <summary>
        /// فحص التحديثات. في الوضع التفاعلي تظهر رسالة حتى عند عدم وجود تحديث.
        /// </summary>
        public static async Task CheckForUpdatesAsync(bool interactive)
        {
            if (!await _checkLock.WaitAsync(0)) return;
            try
            {
                if (!IsConfigured)
                {
                    if (interactive) ShowMessage("التحديث التلقائي غير مفعّل: لم يتم إعداد مستودع GitHub أو مفتاح القراءة.", MessageBoxImage.Warning);
                    return;
                }

                using var http = CreateClient();
                var release = await GetLatestReleaseAsync(http);
                if (release == null)
                {
                    if (interactive) ShowMessage("لا توجد إصدارات منشورة بعد.", MessageBoxImage.Information);
                    return;
                }

                var checksums = await DownloadChecksumsAsync(http, release);

                // 1. تطبيق الهاتف: يُنزَّل تلقائياً بدون أي تدخل لأنه لا يغير شيئاً في الحاسوب
                await UpdateServedApkAsync(http, release, checksums);

                // 2. برنامج الحاسوب
                bool newer = AppVersion.Compare(release.Version, AppVersion.Current) > 0;
                if (!newer)
                {
                    if (interactive) ShowMessage($"أنت تستخدم أحدث إصدار ({AppVersion.Current}).", MessageBoxImage.Information);
                    return;
                }

                if (!interactive && _declinedVersion == release.Version) return;

                var asset = release.Assets.FirstOrDefault(a => a.Name == DesktopAssetName);
                if (asset == null)
                {
                    Log.Warning("Release {Tag} has no {Asset}", release.Tag, DesktopAssetName);
                    return;
                }

                string stagedDir = await DownloadAndStageDesktopAsync(http, release, asset, checksums);
                PromptAndInstall(release, stagedDir);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Update check failed");
                if (interactive) ShowMessage("تعذر فحص التحديثات: " + ex.Message, MessageBoxImage.Error);
            }
            finally
            {
                _checkLock.Release();
            }
        }

        private static HttpClient CreateClient()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LmimaShopApp", AppVersion.Current));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GetToken());
            http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            return http;
        }

        private static async Task<ReleaseInfo?> GetLatestReleaseAsync(HttpClient http)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var resp = await http.SendAsync(req);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            var info = new ReleaseInfo
            {
                Tag = root.GetProperty("tag_name").GetString() ?? "",
                Notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : ""
            };
            info.Version = info.Tag.TrimStart('v', 'V');
            foreach (var a in root.GetProperty("assets").EnumerateArray())
            {
                info.Assets.Add(new ReleaseAsset
                {
                    Name = a.GetProperty("name").GetString() ?? "",
                    ApiUrl = a.GetProperty("url").GetString() ?? "",
                    Size = a.TryGetProperty("size", out var s) ? s.GetInt64() : 0
                });
            }
            return info;
        }

        private static async Task<Dictionary<string, string>> DownloadChecksumsAsync(HttpClient http, ReleaseInfo release)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var asset = release.Assets.FirstOrDefault(a => a.Name == ChecksumsAssetName);
            if (asset == null) return map;

            string tmp = Path.GetTempFileName();
            try
            {
                await DownloadAssetAsync(http, asset, tmp);
                foreach (var line in File.ReadAllLines(tmp))
                {
                    var parts = line.Split(new[] { ' ', '\t', '*' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2) map[parts[^1]] = parts[0].ToLowerInvariant();
                }
            }
            finally
            {
                try { File.Delete(tmp); } catch { }
            }
            return map;
        }

        private static async Task DownloadAssetAsync(HttpClient http, ReleaseAsset asset, string destination)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, asset.ApiUrl);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            await using var src = await resp.Content.ReadAsStreamAsync();
            await using var dst = File.Create(destination);
            await src.CopyToAsync(dst);
        }

        private static void VerifyChecksum(string file, string assetName, Dictionary<string, string> checksums)
        {
            if (!checksums.TryGetValue(assetName, out var expected))
                throw new InvalidOperationException($"لا توجد بصمة SHA-256 للملف {assetName} في الإصدار، تم إيقاف التحديث للأمان.");

            string actual;
            using (var fs = File.OpenRead(file))
            {
                actual = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
            }
            if (actual != expected)
                throw new InvalidOperationException($"بصمة الملف {assetName} غير مطابقة (الملف تالف أو معدّل)، تم إيقاف التحديث.");
        }

        private static async Task UpdateServedApkAsync(HttpClient http, ReleaseInfo release, Dictionary<string, string> checksums)
        {
            var apk = release.Assets.FirstOrDefault(a => a.Name == ApkAssetName);
            if (apk == null) return;
            if (File.Exists(ServedApkPath) && AppVersion.Compare(release.Version, GetServedApkVersion()) <= 0) return;

            Directory.CreateDirectory(UpdatesDir);
            string tmp = Path.Combine(UpdatesDir, $"LmimaShop_{release.Version}.apk");
            await DownloadAssetAsync(http, apk, tmp);
            VerifyChecksum(tmp, ApkAssetName, checksums);

            File.Copy(tmp, ServedApkPath + ".new", true);
            if (File.Exists(ServedApkPath)) File.Replace(ServedApkPath + ".new", ServedApkPath, null, true);
            else File.Move(ServedApkPath + ".new", ServedApkPath);

            File.WriteAllText(ServedApkInfoPath, JsonSerializer.Serialize(new
            {
                version = release.Version,
                notes = FirstLines(release.Notes, 3),
                downloadedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            }), new UTF8Encoding(false));
            try { File.Delete(tmp); } catch { }

            Log.Information("Mobile APK updated to {Version}", release.Version);
            _ = LmimaShopApp.Core.Sync.SseManager.BroadcastEventAsync("app_update", release.Version);
        }

        private static async Task<string> DownloadAndStageDesktopAsync(HttpClient http, ReleaseInfo release, ReleaseAsset asset, Dictionary<string, string> checksums)
        {
            string versionDir = Path.Combine(UpdatesDir, release.Version);
            string stagedDir = Path.Combine(versionDir, "app");
            string marker = Path.Combine(versionDir, "ready");
            if (File.Exists(marker) && Directory.Exists(stagedDir)) return stagedDir;

            if (Directory.Exists(versionDir)) Directory.Delete(versionDir, true);
            Directory.CreateDirectory(versionDir);

            string zip = Path.Combine(versionDir, DesktopAssetName);
            await DownloadAssetAsync(http, asset, zip);
            VerifyChecksum(zip, DesktopAssetName, checksums);
            ZipFile.ExtractToDirectory(zip, stagedDir);

            if (!File.Exists(Path.Combine(stagedDir, "LmimaShopApp.exe")))
                throw new InvalidOperationException("حزمة التحديث لا تحتوي على LmimaShopApp.exe");

            // حماية إضافية: الحزمة يجب ألا تستبدل بيانات المتجر
            foreach (var f in new[] { "settings.json", "LmimaShop.apk", "LmimaShop.apk.json" })
            {
                string p = Path.Combine(stagedDir, f);
                if (File.Exists(p)) File.Delete(p);
            }
            foreach (var d in new[] { "ProductImages", "Logs", "MobileSync" })
            {
                string p = Path.Combine(stagedDir, d);
                if (Directory.Exists(p)) Directory.Delete(p, true);
            }

            File.WriteAllText(marker, release.Tag);
            try { File.Delete(zip); } catch { }
            return stagedDir;
        }

        private static void PromptAndInstall(ReleaseInfo release, string stagedDir)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                string notes = FirstLines(release.Notes, 12);
                var answer = MessageBox.Show(
                    $"يتوفر إصدار جديد من البرنامج: {release.Version}\nالإصدار الحالي: {AppVersion.Current}\n\n{notes}\n\n" +
                    "سيتم أخذ نسخة احتياطية من قاعدة البيانات ثم إعادة تشغيل البرنامج. بياناتك وإعداداتك لن تتغير.\n\nهل تريد التثبيت الآن؟",
                    "تحديث LMIMA SHOP", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (answer != MessageBoxResult.Yes)
                {
                    _declinedVersion = release.Version;
                    return;
                }

                try
                {
                    InstallStagedUpdate(release.Version, stagedDir);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Install update failed");
                    MessageBox.Show("تعذر تثبيت التحديث، البرنامج الحالي لم يتغير:\n" + ex.Message, "خطأ في التحديث", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
        }

        private static void InstallStagedUpdate(string version, string stagedDir)
        {
            // 1. نسخة احتياطية لقاعدة البيانات قبل أي تغيير (إلزامية)
            string backupFolder = BackupService.GetConfiguredBackupPath();
            if (string.IsNullOrWhiteSpace(backupFolder) || backupFolder.Contains("لم يتم تحديد")) backupFolder = Path.Combine(AppDataDir, "Backups");
            Directory.CreateDirectory(backupFolder);
            string dbBackup = Path.Combine(backupFolder, $"LmimaShop_BeforeUpdate_{version}_{DateTime.Now:yyyy_MM_dd_HHmmss}.sql");
            BackupService.GenerateBackup(dbBackup);
            Log.Information("Database backed up before update to {File}", dbBackup);

            // 2. سكربت يُكمل التحديث بعد إغلاق البرنامج، مع استرجاع الملفات القديمة عند الفشل
            string appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            string oldFilesBackup = Path.Combine(UpdatesDir, "previous_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            string exePath = Path.Combine(appDir, "LmimaShopApp.exe");
            string logFile = Path.Combine(UpdatesDir, "update.log");
            string script = Path.Combine(UpdatesDir, "apply_update.ps1");

            File.WriteAllText(script, UpdaterScript, new UTF8Encoding(true));

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\" " +
                            $"-ProcessId {Environment.ProcessId} -Source \"{stagedDir}\" -Target \"{appDir}\" " +
                            $"-Backup \"{oldFilesBackup}\" -Exe \"{exePath}\" -Log \"{logFile}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi);
            Log.Information("Update {Version} handed to updater script; shutting down", version);
            Application.Current.Shutdown();
        }

        private const string UpdaterScript = @"param(
    [int]$ProcessId,
    [string]$Source,
    [string]$Target,
    [string]$Backup,
    [string]$Exe,
    [string]$Log
)
function Write-Log($m) { Add-Content -Path $Log -Value ((Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + '  ' + $m) -Encoding UTF8 }
Write-Log ""Update started: $Source -> $Target""
try { Wait-Process -Id $ProcessId -Timeout 90 -ErrorAction SilentlyContinue } catch {}
Start-Sleep -Seconds 1

# Files and folders that belong to the shop and must never be touched
$keepDirs  = @('ProductImages', 'Logs', 'MobileSync', 'Backups')
$keepFiles = @('settings.json', 'LmimaShop.apk', 'LmimaShop.apk.json')

robocopy $Target $Backup /E /R:2 /W:1 /NFL /NDL /NJH /NJS /XD $keepDirs | Out-Null
if ($LASTEXITCODE -ge 8) { Write-Log ""Backup of old files failed ($LASTEXITCODE); update cancelled""; Start-Process $Exe; exit 1 }

robocopy $Source $Target /E /R:5 /W:2 /NFL /NDL /NJH /NJS /XD $keepDirs /XF $keepFiles | Out-Null
$code = $LASTEXITCODE
if ($code -ge 8) {
    Write-Log ""Copy failed ($code); restoring previous version""
    robocopy $Backup $Target /E /R:5 /W:2 /NFL /NDL /NJH /NJS | Out-Null
} else {
    Write-Log 'Update applied successfully'
}
Start-Process $Exe
";

        private static void ShowMessage(string text, MessageBoxImage icon)
        {
            Application.Current?.Dispatcher.Invoke(() => MessageBox.Show(text, "تحديث LMIMA SHOP", MessageBoxButton.OK, icon));
        }

        private static string FirstLines(string text, int count)
        {
            var lines = (text ?? "").Replace("\r", "").Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).Take(count);
            return string.Join("\n", lines);
        }

        private class ReleaseInfo
        {
            public string Tag { get; set; } = "";
            public string Version { get; set; } = "";
            public string Notes { get; set; } = "";
            public List<ReleaseAsset> Assets { get; } = new List<ReleaseAsset>();
        }

        private class ReleaseAsset
        {
            public string Name { get; set; } = "";
            public string ApiUrl { get; set; } = "";
            public long Size { get; set; }
        }
    }
}
