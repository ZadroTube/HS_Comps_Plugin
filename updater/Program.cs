using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace HSCompsPlugin.Updater
{
    internal class Program
    {
        private const string GITHUB_REPO = "ZadroTube/HS_Comps_Plugin";
        private const string GITHUB_API_URL = "https://api.github.com/repos/" + GITHUB_REPO + "/releases/latest";

        private static bool _silentMode = false;
        private static bool _forceMode = false;
        private static string _customDir = null;
        private static string _githubToken = null;

        private static void Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = System.Text.Encoding.UTF8;
            }
            catch { }

            try
            {
                Console.Title = "HS Comps Plugin — Авто-апдейтер (ZadroTube)";
            }
            catch { }

            ParseArguments(args);

            PrintHeader();

            try
            {
                RunUpdateAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                PrintColor($"\n[!] Критическая ошибка при обновлении: {ex.Message}", ConsoleColor.Red);
                if (ex.InnerException != null)
                {
                    PrintColor($"    Подробности: {ex.InnerException.Message}", ConsoleColor.DarkRed);
                }
            }

            if (!_silentMode)
            {
                Console.WriteLine("\nНажмите любую клавишу для выхода...");
                try { Console.ReadKey(); } catch { }
            }
        }

        private static void ParseArguments(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i].ToLowerInvariant();
                if (arg == "--silent" || arg == "-s")
                {
                    _silentMode = true;
                }
                else if (arg == "--force" || arg == "-f")
                {
                    _forceMode = true;
                }
                else if ((arg == "--dir" || arg == "-d") && i + 1 < args.Length)
                {
                    _customDir = args[++i];
                }
                else if ((arg == "--token" || arg == "-t") && i + 1 < args.Length)
                {
                    _githubToken = args[++i];
                }
            }

            if (string.IsNullOrEmpty(_githubToken))
            {
                _githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
            }
        }

        private static void PrintHeader()
        {
            PrintColor("╔══════════════════════════════════════════════════════════════╗", ConsoleColor.Cyan);
            PrintColor("║          HS COMPS PLUGIN — АВТОМАТИЧЕСКИЙ АПДЕЙТЕР           ║", ConsoleColor.Cyan);
            PrintColor("║             Автор: ZadroTube  •  Hearthstone Battlegrounds   ║", ConsoleColor.Cyan);
            PrintColor("╚══════════════════════════════════════════════════════════════╝", ConsoleColor.Cyan);
            Console.WriteLine();
        }

        private static async Task RunUpdateAsync()
        {
            // 1. Определение целевого каталога установки плагина
            string targetDir = ResolvePluginDirectory();
            PrintColor($"[•] Каталог плагина: {targetDir}", ConsoleColor.Gray);

            // 2. Определение текущей установленной версии
            string localVersion = GetLocalPluginVersion(targetDir);
            if (!string.IsNullOrEmpty(localVersion))
            {
                PrintColor($"[•] Установленная версия: v{localVersion}", ConsoleColor.Yellow);
            }
            else
            {
                PrintColor("[•] Плагин ещё не установлен в целевую папку (режим чистой установки)", ConsoleColor.Yellow);
            }

            // 3. Проверка запущенного процесса HDT
            bool wasHdtClosed = CheckAndHandleRunningHdt();

            // 4. Запрос последнего релиза на GitHub
            PrintColor("\n[1/4] Проверка обновлений на GitHub...", ConsoleColor.Cyan);
            var releaseInfo = await FetchLatestReleaseInfoAsync();

            PrintColor($"[✓] Найдена последняя версия: {releaseInfo.TagName} («{releaseInfo.Name}»)", ConsoleColor.Green);
            PrintColor($"    Файл релиза: {releaseInfo.AssetFileName} ({FormatBytes(releaseInfo.AssetSize)})", ConsoleColor.DarkGray);

            // 5. Сравнение версий
            if (!_forceMode && !string.IsNullOrEmpty(localVersion))
            {
                string normLocal = NormalizeVersion(localVersion);
                string normRemote = NormalizeVersion(releaseInfo.TagName);

                if (string.Equals(normLocal, normRemote, StringComparison.OrdinalIgnoreCase))
                {
                    PrintColor($"\n[✓] У вас уже установлена актуальная версия {releaseInfo.TagName}!", ConsoleColor.Green);
                    if (!_silentMode)
                    {
                        Console.Write("Переустановить / перезаписать файлы заново? [y/N]: ");
                        string ans = Console.ReadLine()?.Trim().ToLowerInvariant();
                        if (ans != "y" && ans != "yes" && ans != "д" && ans != "да")
                        {
                            PrintColor("Обновление отменено пользователем.", ConsoleColor.Gray);
                            OfferRestartHdt(wasHdtClosed);
                            return;
                        }
                    }
                    else
                    {
                        OfferRestartHdt(wasHdtClosed);
                        return;
                    }
                }
            }

            // 6. Скачивание релизного архива
            PrintColor($"\n[2/4] Скачивание {releaseInfo.AssetFileName}...", ConsoleColor.Cyan);
            string tempZipPath = Path.Combine(Path.GetTempPath(), $"HSCompsUpdate_{Guid.NewGuid():N}.zip");
            string tempExtractDir = Path.Combine(Path.GetTempPath(), $"HSCompsExtract_{Guid.NewGuid():N}");

            try
            {
                await DownloadFileWithProgressAsync(releaseInfo.DownloadUrl, tempZipPath);
                PrintColor("[✓] Архив успешно загружен!", ConsoleColor.Green);

                // 7. Распаковка архива
                PrintColor("\n[3/4] Распаковка и подготовка файлов...", ConsoleColor.Cyan);
                if (Directory.Exists(tempExtractDir)) Directory.Delete(tempExtractDir, true);
                Directory.CreateDirectory(tempExtractDir);

                ZipFile.ExtractToDirectory(tempZipPath, tempExtractDir);

                // 8. Замена файлов плагина
                PrintColor("\n[4/4] Замена файлов плагина...", ConsoleColor.Cyan);
                int replacedFiles = DeployExtractedFiles(tempExtractDir, targetDir);

                PrintColor($"[✓] Успешно обновлено файлов: {replacedFiles}", ConsoleColor.Green);
                PrintColor($"\n🎉 Плагин успешно обновлен до версии {releaseInfo.TagName}!", ConsoleColor.Green);
            }
            finally
            {
                // Очистка временных файлов
                try
                {
                    if (File.Exists(tempZipPath)) File.Delete(tempZipPath);
                    if (Directory.Exists(tempExtractDir)) Directory.Delete(tempExtractDir, true);
                }
                catch { }
            }

            // 9. Предложение запустить HDT обратно
            OfferRestartHdt(wasHdtClosed);
        }

        private static string ResolvePluginDirectory()
        {
            if (!string.IsNullOrEmpty(_customDir))
            {
                Directory.CreateDirectory(_customDir);
                return Path.GetFullPath(_customDir);
            }

            // Если апдейтер лежит прямо в папке плагина
            string currentDir = AppDomain.CurrentDomain.BaseDirectory;
            if (File.Exists(Path.Combine(currentDir, "HSCompsPlugin.dll")))
            {
                return currentDir;
            }

            // Стандартное расположение HDT плагина в %APPDATA%
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string defaultPluginDir = Path.Combine(appData, "HearthstoneDeckTracker", "Plugins", "HSCompsPlugin");

            if (!Directory.Exists(defaultPluginDir))
            {
                Directory.CreateDirectory(defaultPluginDir);
            }

            return defaultPluginDir;
        }

        private static string GetLocalPluginVersion(string targetDir)
        {
            string dllPath = Path.Combine(targetDir, "HSCompsPlugin.dll");
            if (File.Exists(dllPath))
            {
                try
                {
                    var vi = FileVersionInfo.GetVersionInfo(dllPath);
                    if (!string.IsNullOrEmpty(vi.FileVersion)) return vi.FileVersion;
                    if (!string.IsNullOrEmpty(vi.ProductVersion)) return vi.ProductVersion;
                }
                catch { }
            }
            return null;
        }

        private static string NormalizeVersion(string ver)
        {
            if (string.IsNullOrEmpty(ver)) return "";
            ver = ver.Trim();
            if (ver.StartsWith("v", StringComparison.OrdinalIgnoreCase)) ver = ver.Substring(1);
            var parts = ver.Split('.');
            if (parts.Length >= 3)
            {
                return $"{parts[0]}.{parts[1]}.{parts[2]}";
            }
            return ver;
        }

        private static bool CheckAndHandleRunningHdt()
        {
            var processes = Process.GetProcessesByName("HearthstoneDeckTracker");
            if (processes.Length == 0) return false;

            PrintColor("\n[!] Обнаружен запущенный Hearthstone Deck Tracker.", ConsoleColor.Yellow);
            PrintColor("    Для корректной замены DLL файлов трекер должен быть временно закрыт.", ConsoleColor.Yellow);

            if (_silentMode)
            {
                CloseHdtProcesses(processes);
                return true;
            }

            Console.Write("Закрыть Hearthstone Deck Tracker автоматически? [Y/n]: ");
            string answer = Console.ReadLine()?.Trim().ToLowerInvariant();

            if (answer == "n" || answer == "no" || answer == "н" || answer == "нет")
            {
                PrintColor("Пожалуйста, закройте HDT вручную и нажмите Enter для продолжения...", ConsoleColor.Yellow);
                Console.ReadLine();
                return false;
            }

            CloseHdtProcesses(processes);
            return true;
        }

        private static void CloseHdtProcesses(Process[] processes)
        {
            PrintColor("Закрытие Hearthstone Deck Tracker...", ConsoleColor.Gray);
            foreach (var p in processes)
            {
                try
                {
                    p.CloseMainWindow();
                    if (!p.WaitForExit(3000))
                    {
                        p.Kill();
                    }
                }
                catch { }
            }
            Thread.Sleep(500);
            PrintColor("[✓] Hearthstone Deck Tracker закрыт.", ConsoleColor.Green);
        }

        private class GitHubReleaseInfo
        {
            public string TagName { get; set; }
            public string Name { get; set; }
            public string AssetFileName { get; set; }
            public string DownloadUrl { get; set; }
            public long AssetSize { get; set; }
        }

        private static async Task<GitHubReleaseInfo> FetchLatestReleaseInfoAsync()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)3072;

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", "HSCompsPlugin-Updater/3.0");
                client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");

                if (!string.IsNullOrEmpty(_githubToken))
                {
                    client.DefaultRequestHeaders.Add("Authorization", "Bearer " + _githubToken);
                }

                HttpResponseMessage response = await client.GetAsync(GITHUB_API_URL);
                if (!response.IsSuccessStatusCode)
                {
                    string errorBody = await response.Content.ReadAsStringAsync();
                    throw new Exception($"GitHub API вернул статус {response.StatusCode} ({(int)response.StatusCode}): {errorBody}");
                }

                string jsonContent = await response.Content.ReadAsStringAsync();
                var serializer = new JavaScriptSerializer();
                var json = serializer.Deserialize<Dictionary<string, object>>(jsonContent);
                if (json == null)
                {
                    throw new Exception("Не удалось разобрать ответ GitHub API.");
                }

                string tagName = json.ContainsKey("tag_name") ? json["tag_name"]?.ToString() : "latest";
                string releaseName = json.ContainsKey("name") ? json["name"]?.ToString() : tagName;

                var assets = json.ContainsKey("assets") ? json["assets"] as System.Collections.ArrayList : null;
                if (assets == null || assets.Count == 0)
                {
                    throw new Exception($"В релизе {tagName} отсутствуют прикрепленные файлы (assets).");
                }

                // Поиск .zip архива плагина
                Dictionary<string, object> targetAsset = null;
                foreach (var item in assets)
                {
                    if (item is Dictionary<string, object> assetDict)
                    {
                        string aName = assetDict.ContainsKey("name") ? assetDict["name"]?.ToString() : "";
                        if (aName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                            aName.IndexOf("HSComps", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            targetAsset = assetDict;
                            break;
                        }
                    }
                }

                if (targetAsset == null)
                {
                    foreach (var item in assets)
                    {
                        if (item is Dictionary<string, object> assetDict)
                        {
                            string aName = assetDict.ContainsKey("name") ? assetDict["name"]?.ToString() : "";
                            if (aName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            {
                                targetAsset = assetDict;
                                break;
                            }
                        }
                    }
                }

                if (targetAsset == null)
                {
                    throw new Exception($"В релизе {tagName} не найден ZIP-архив с плагином.");
                }

                long assetSize = 0;
                if (targetAsset.ContainsKey("size") && targetAsset["size"] != null)
                {
                    long.TryParse(targetAsset["size"].ToString(), out assetSize);
                }

                return new GitHubReleaseInfo
                {
                    TagName = tagName,
                    Name = releaseName,
                    AssetFileName = targetAsset.ContainsKey("name") ? targetAsset["name"]?.ToString() : "",
                    DownloadUrl = targetAsset.ContainsKey("browser_download_url") ? targetAsset["browser_download_url"]?.ToString() : "",
                    AssetSize = assetSize
                };
            }
        }

        private static async Task DownloadFileWithProgressAsync(string downloadUrl, string destinationPath)
        {
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", "HSCompsPlugin-Updater/3.0");
                if (!string.IsNullOrEmpty(_githubToken))
                {
                    client.DefaultRequestHeaders.Add("Authorization", "Bearer " + _githubToken);
                }

                using (var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    long? totalBytes = response.Content.Headers.ContentLength;

                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                    {
                        byte[] buffer = new byte[81920];
                        long totalRead = 0;
                        int bytesRead;
                        var sw = Stopwatch.StartNew();
                        int lastProgressPercent = -1;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalRead += bytesRead;

                            if (totalBytes.HasValue && totalBytes.Value > 0)
                            {
                                int percent = (int)((totalRead * 100) / totalBytes.Value);
                                if (percent != lastProgressPercent || sw.ElapsedMilliseconds > 200)
                                {
                                    lastProgressPercent = percent;
                                    double speedMb = (totalRead / 1024.0 / 1024.0) / Math.Max(0.1, sw.Elapsed.TotalSeconds);
                                    DrawProgressBar(percent, totalRead, totalBytes.Value, speedMb);
                                }
                            }
                        }

                        if (totalBytes.HasValue)
                        {
                            double speedMb = (totalRead / 1024.0 / 1024.0) / Math.Max(0.1, sw.Elapsed.TotalSeconds);
                            DrawProgressBar(100, totalRead, totalBytes.Value, speedMb);
                            Console.WriteLine();
                        }
                    }
                }
            }
        }

        private static void DrawProgressBar(int percent, long currentBytes, long totalBytes, double speedMbPerSec)
        {
            const int barLength = 26;
            int filled = (percent * barLength) / 100;
            string bar = new string('█', filled) + new string('░', barLength - filled);

            double curMb = currentBytes / 1024.0 / 1024.0;
            double totMb = totalBytes / 1024.0 / 1024.0;

            string line = $"\r    [{bar}] {percent,3}% ({curMb:0.0} / {totMb:0.0} МБ) {speedMbPerSec:0.0} МБ/с  ";
            Console.Write(line);
        }

        private static int DeployExtractedFiles(string sourceExtractDir, string targetDir)
        {
            // Проверка, есть ли внутри распакованного архива папка HSCompsPlugin
            string effectiveSource = sourceExtractDir;
            string innerFolder = Path.Combine(sourceExtractDir, "HSCompsPlugin");
            if (Directory.Exists(innerFolder) && File.Exists(Path.Combine(innerFolder, "HSCompsPlugin.dll")))
            {
                effectiveSource = innerFolder;
            }

            int count = 0;
            var allFiles = Directory.GetFiles(effectiveSource, "*.*", SearchOption.AllDirectories);

            foreach (var srcFile in allFiles)
            {
                string relativePath = srcFile.Substring(effectiveSource.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                // Пропускаем сам запущенный файл апдейтера, если он уже есть в папке плагина
                if (string.Equals(Path.GetFileName(srcFile), "HSCompsUpdater.exe", StringComparison.OrdinalIgnoreCase))
                {
                    // Скопируем через резервную копию, если не заблокирован
                    try
                    {
                        string destExe = Path.Combine(targetDir, relativePath);
                        string destExeDir = Path.GetDirectoryName(destExe);
                        if (!Directory.Exists(destExeDir)) Directory.CreateDirectory(destExeDir);
                        File.Copy(srcFile, destExe, true);
                        count++;
                    }
                    catch { }
                    continue;
                }

                string destFile = Path.Combine(targetDir, relativePath);
                string destDir = Path.GetDirectoryName(destFile);
                if (!Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                File.Copy(srcFile, destFile, true);
                count++;
            }

            return count;
        }

        private static void OfferRestartHdt(bool wasHdtClosed)
        {
            string hdtPath = FindHdtExecutable();
            if (string.IsNullOrEmpty(hdtPath) || !File.Exists(hdtPath))
            {
                return;
            }

            if (_silentMode)
            {
                if (wasHdtClosed)
                {
                    try { Process.Start(hdtPath); } catch { }
                }
                return;
            }

            Console.WriteLine();
            Console.Write("Запустить Hearthstone Deck Tracker? [Y/n]: ");
            string ans = Console.ReadLine()?.Trim().ToLowerInvariant();

            if (ans != "n" && ans != "no" && ans != "н" && ans != "нет")
            {
                try
                {
                    Process.Start(hdtPath);
                    PrintColor("[✓] Hearthstone Deck Tracker запущен!", ConsoleColor.Green);
                }
                catch (Exception ex)
                {
                    PrintColor($"Не удалось запустить HDT: {ex.Message}", ConsoleColor.Yellow);
                }
            }
        }

        private static string FindHdtExecutable()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string candidate1 = Path.Combine(localAppData, "HearthstoneDeckTracker", "HearthstoneDeckTracker.exe");
            if (File.Exists(candidate1)) return candidate1;

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string candidate2 = Path.Combine(appData, "HearthstoneDeckTracker", "HearthstoneDeckTracker.exe");
            if (File.Exists(candidate2)) return candidate2;

            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string candidate3 = Path.Combine(programFiles, "Hearthstone Deck Tracker", "HearthstoneDeckTracker.exe");
            if (File.Exists(candidate3)) return candidate3;

            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string candidate4 = Path.Combine(programFilesX86, "Hearthstone Deck Tracker", "HearthstoneDeckTracker.exe");
            if (File.Exists(candidate4)) return candidate4;

            return null;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 Б";
            double mb = bytes / 1024.0 / 1024.0;
            if (mb >= 1.0) return $"{mb:0.0} МБ";
            double kb = bytes / 1024.0;
            return $"{kb:0.0} КБ";
        }

        private static void PrintColor(string text, ConsoleColor color)
        {
            var oldColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ForegroundColor = oldColor;
        }
    }
}
