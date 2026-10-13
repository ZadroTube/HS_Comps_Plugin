using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HSCompsPlugin.Services
{
    public class DatabaseInfo
    {
        public bool Exists { get; set; }
        public string FilePath { get; set; }
        public DateTime? LastModified { get; set; }
        public int BoardCount { get; set; }
        public double FileSizeKb { get; set; }
    }

    public class UpdateResult
    {
        public bool Success { get; set; }
        public int BoardCount { get; set; }
        public string Message { get; set; }
        public List<string> Logs { get; set; } = new List<string>();
    }

    public class DatabaseUpdateService
    {
        private static DatabaseUpdateService _instance;
        public static DatabaseUpdateService Instance => _instance ?? (_instance = new DatabaseUpdateService());

        public bool IsUpdating { get; private set; }

        public event Action<string> LogReceived;
        public event Action<bool, string> UpdateCompleted;

        public DatabaseInfo GetDatabaseInfo()
        {
            var info = new DatabaseInfo();
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string assemblyDir = Path.GetDirectoryName(typeof(DatabaseUpdateService).Assembly.Location) ?? "";
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            string[] candidates = new[]
            {
                Path.Combine(appData, "HearthstoneDeckTracker", "Plugins", "HSCompsPlugin", "comps_vector_db.json"),
                Path.Combine(assemblyDir, "comps_vector_db.json"),
                Path.Combine(baseDir, "comps_vector_db.json"),
                Path.Combine(baseDir, "Plugins", "HSCompsPlugin", "comps_vector_db.json")
            };

            string foundPath = candidates.FirstOrDefault(File.Exists);
            if (!string.IsNullOrEmpty(foundPath) && File.Exists(foundPath))
            {
                var fi = new FileInfo(foundPath);
                info.Exists = true;
                info.FilePath = foundPath;
                info.LastModified = fi.LastWriteTime;
                info.FileSizeKb = Math.Round(fi.Length / 1024.0, 1);
                info.BoardCount = VectorSearchEngine.Instance.BoardsCount;

                if (info.BoardCount == 0)
                {
                    try
                    {
                        using (var stream = File.OpenRead(foundPath))
                        using (var reader = new StreamReader(stream, Encoding.UTF8))
                        using (var jsonReader = new Newtonsoft.Json.JsonTextReader(reader))
                        {
                            while (jsonReader.Read())
                            {
                                if (jsonReader.TokenType == Newtonsoft.Json.JsonToken.PropertyName &&
                                    string.Equals(jsonReader.Value?.ToString(), "boards", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (jsonReader.Read() && jsonReader.TokenType == Newtonsoft.Json.JsonToken.StartArray)
                                    {
                                        int count = 0;
                                        while (jsonReader.Read() && jsonReader.TokenType != Newtonsoft.Json.JsonToken.EndArray)
                                        {
                                            if (jsonReader.TokenType == Newtonsoft.Json.JsonToken.StartObject)
                                            {
                                                count++;
                                                jsonReader.Skip();
                                            }
                                        }
                                        info.BoardCount = count;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            else
            {
                info.Exists = false;
                info.BoardCount = VectorSearchEngine.Instance.BoardsCount;
            }

            return info;
        }

        public string FindPythonExecutable()
        {
            // 1. Проверка python в PATH
            if (CheckCommandAvailable("python", "--version"))
                return "python";

            // 2. Проверка лаунчера py в Windows
            if (CheckCommandAvailable("py", "-3 --version"))
                return "py";

            // 3. Проверка стандартных каталогов установки Python в Windows
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string programsPython = Path.Combine(localAppData, "Programs", "Python");
            if (Directory.Exists(programsPython))
            {
                foreach (var dir in Directory.GetDirectories(programsPython, "Python3*").OrderByDescending(d => d))
                {
                    string candidate = Path.Combine(dir, "python.exe");
                    if (File.Exists(candidate))
                        return candidate;
                }
            }

            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            foreach (var dir in Directory.GetDirectories(programFiles, "Python3*").OrderByDescending(d => d))
            {
                string candidate = Path.Combine(dir, "python.exe");
                if (File.Exists(candidate))
                    return candidate;
            }

            string systemDrive = Path.GetPathRoot(Environment.SystemDirectory);
            if (Directory.Exists(systemDrive))
            {
                foreach (var dir in Directory.GetDirectories(systemDrive, "Python3*").OrderByDescending(d => d))
                {
                    string candidate = Path.Combine(dir, "python.exe");
                    if (File.Exists(candidate))
                        return candidate;
                }
            }

            return null;
        }

        private bool CheckCommandAvailable(string command, string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var p = Process.Start(psi))
                {
                    if (p != null && p.WaitForExit(2000))
                    {
                        return p.ExitCode == 0;
                    }
                }
            }
            catch { }
            return false;
        }

        public string FindScriptPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string assemblyDir = Path.GetDirectoryName(typeof(DatabaseUpdateService).Assembly.Location) ?? "";
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            var candidates = new List<string>
            {
                Path.Combine(assemblyDir, "tools", "build_vector_db.py"),
                Path.Combine(appData, "HearthstoneDeckTracker", "Plugins", "HSCompsPlugin", "tools", "build_vector_db.py"),
                Path.Combine(baseDir, "tools", "build_vector_db.py"),
                Path.Combine(assemblyDir, "build_vector_db.py"),
                Path.Combine(baseDir, "build_vector_db.py")
            };

            // Проверка родительских каталогов сборки (режим разработки / сборки из исходников)
            string current = assemblyDir;
            for (int i = 0; i < 4; i++)
            {
                if (string.IsNullOrEmpty(current)) break;
                candidates.Add(Path.Combine(current, "tools", "build_vector_db.py"));
                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }

            return candidates.FirstOrDefault(File.Exists);
        }

        public string GetWorkingDirectory(string scriptPath)
        {
            string scriptDir = Path.GetDirectoryName(scriptPath) ?? "";
            var parentDir = Directory.GetParent(scriptDir)?.FullName;

            if (!string.IsNullOrEmpty(parentDir) && File.Exists(Path.Combine(parentDir, "bg_cards_official.json")))
            {
                return parentDir;
            }

            if (File.Exists(Path.Combine(scriptDir, "bg_cards_official.json")))
            {
                return scriptDir;
            }

            string appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "HearthstoneDeckTracker", "Plugins", "HSCompsPlugin");
            if (Directory.Exists(appDataDir) && File.Exists(Path.Combine(appDataDir, "bg_cards_official.json")))
            {
                return appDataDir;
            }

            return !string.IsNullOrEmpty(parentDir) ? parentDir : scriptDir;
        }

        public async Task<UpdateResult> RunUpdateAsync(Action<string> logCallback = null)
        {
            if (IsUpdating)
            {
                return new UpdateResult { Success = false, Message = "Обновление уже выполняется." };
            }

            IsUpdating = true;
            var result = new UpdateResult();

            void Log(string message)
            {
                if (string.IsNullOrWhiteSpace(message)) return;
                result.Logs.Add(message);
                logCallback?.Invoke(message);
                LogReceived?.Invoke(message);
            }

            try
            {
                Log("🔍 Поиск Python и скрипта обновления...");
                string pythonPath = FindPythonExecutable();
                if (string.IsNullOrEmpty(pythonPath))
                {
                    string err = "Python 3 не найден в системе (PATH). Установите Python 3 с официального сайта python.org.";
                    Log($"❌ Ошибка: {err}");
                    result.Success = false;
                    result.Message = err;
                    UpdateCompleted?.Invoke(false, err);
                    return result;
                }

                string scriptPath = FindScriptPath();
                if (string.IsNullOrEmpty(scriptPath))
                {
                    string err = "Скрипт tools/build_vector_db.py не найден в папке плагина.";
                    Log($"❌ Ошибка: {err}");
                    result.Success = false;
                    result.Message = err;
                    UpdateCompleted?.Invoke(false, err);
                    return result;
                }

                string workingDir = GetWorkingDirectory(scriptPath);
                Log($"▶ Запуск скрипта: {Path.GetFileName(scriptPath)}");
                Log($"   Интерпретатор: {pythonPath}");
                Log($"   Рабочая папка: {workingDir}");

                var psi = new ProcessStartInfo
                {
                    FileName = pythonPath,
                    Arguments = $"-u \"{scriptPath}\"",
                    WorkingDirectory = workingDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                int exitCode = -1;
                await Task.Run(() =>
                {
                    using (var process = new Process { StartInfo = psi })
                    {
                        process.OutputDataReceived += (s, e) =>
                        {
                            if (!string.IsNullOrWhiteSpace(e.Data)) Log(e.Data);
                        };
                        process.ErrorDataReceived += (s, e) =>
                        {
                            if (!string.IsNullOrWhiteSpace(e.Data)) Log($"[Stderr] {e.Data}");
                        };

                        process.Start();
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();
                        process.WaitForExit();
                        exitCode = process.ExitCode;
                    }
                });

                if (exitCode != 0)
                {
                    string err = $"Скрипт завершился с ошибкой (код {exitCode}).";
                    Log($"❌ {err}");
                    result.Success = false;
                    result.Message = err;
                    UpdateCompleted?.Invoke(false, err);
                    return result;
                }

                Log("💾 Синхронизация обновленной базы данных...");
                string generatedDb = Path.Combine(workingDir, "comps_vector_db.json");
                if (File.Exists(generatedDb))
                {
                    string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    string appDataDb = Path.Combine(appData, "HearthstoneDeckTracker", "Plugins", "HSCompsPlugin", "comps_vector_db.json");
                    if (!string.Equals(generatedDb, appDataDb, StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            string dir = Path.GetDirectoryName(appDataDb);
                            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                            File.Copy(generatedDb, appDataDb, true);
                            Log($"   Скопировано в HDT Plugins: {appDataDb}");
                        }
                        catch (Exception ex)
                        {
                            Log($"   [!] Предупреждение копирования: {ex.Message}");
                        }
                    }

                    string assemblyDir = Path.GetDirectoryName(typeof(DatabaseUpdateService).Assembly.Location) ?? "";
                    string assemblyDb = Path.Combine(assemblyDir, "comps_vector_db.json");
                    if (!string.Equals(generatedDb, assemblyDb, StringComparison.OrdinalIgnoreCase) && Directory.Exists(assemblyDir))
                    {
                        try
                        {
                            File.Copy(generatedDb, assemblyDb, true);
                        }
                        catch { }
                    }
                }

                Log("⚡ Мгновенная перезагрузка базы данных в памяти советника...");
                VectorSearchEngine.Instance.ReloadDatabase();

                int count = VectorSearchEngine.Instance.BoardsCount;
                result.Success = true;
                result.BoardCount = count;
                result.Message = $"База успешно обновлена из Firestone API! Загружено {count} эталонных победных столов.";
                Log($"✅ {result.Message}");

                UpdateCompleted?.Invoke(true, result.Message);
                return result;
            }
            catch (Exception ex)
            {
                string err = $"Непредвиденное исключение при обновлении: {ex.Message}";
                Log($"❌ {err}");
                result.Success = false;
                result.Message = err;
                UpdateCompleted?.Invoke(false, err);
                return result;
            }
            finally
            {
                IsUpdating = false;
            }
        }
    }
}
