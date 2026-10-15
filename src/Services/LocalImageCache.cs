using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HearthDb;
using HSCompsPlugin.Models;

namespace HSCompsPlugin.Services
{
    public class LocalImageCache
    {
        private static LocalImageCache _instance;
        public static LocalImageCache Instance => _instance ?? (_instance = new LocalImageCache());

        private readonly string _cacheDirectory;
        private readonly string _assemblyDirectory;
        private readonly string _primaryFullCardsDir;
        private readonly List<string> _assetRootDirectories = new List<string>();
        private readonly ConcurrentDictionary<string, BitmapSource> _memoryCache = new ConcurrentDictionary<string, BitmapSource>();
        private readonly ConcurrentQueue<string> _cacheKeyOrder = new ConcurrentQueue<string>();
        private const int MaxMemoryCacheEntries = 650;
        private static readonly HttpClient _httpClient;
        private readonly ConcurrentDictionary<string, bool> _activeDownloads = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, bool> _failedUrls = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, bool> _fileExistenceCache = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, string> _dbfToCardId = new Dictionary<int, string>();

        static LocalImageCache()
        {
            try
            {
                ServicePointManager.DefaultConnectionLimit = 64;
                ServicePointManager.Expect100Continue = false;
                ServicePointManager.UseNagleAlgorithm = false;
            }
            catch { }

            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(12)
            };
            try
            {
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            }
            catch { }
        }

        private bool CachedFileExists(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return _fileExistenceCache.GetOrAdd(path, File.Exists);
        }

        public event Action<string> ImageDownloaded;

        private static bool IsSyntheticNumericBgId(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return true;
            if (char.IsDigit(cardId[0])) return true;
            if (cardId.StartsWith("BG_", StringComparison.OrdinalIgnoreCase) && cardId.Length > 3 && char.IsDigit(cardId[3]))
                return true;
            return false;
        }

        private void PutMemoryCache(string key, BitmapSource bmp)
        {
            if (bmp == null || string.IsNullOrEmpty(key)) return;
            bool isNewKey = !_memoryCache.ContainsKey(key);
            _memoryCache[key] = bmp;
            if (isNewKey)
            {
                _cacheKeyOrder.Enqueue(key);
            }

            if (_memoryCache.Count > MaxMemoryCacheEntries)
            {
                for (int i = 0; i < 30 && _cacheKeyOrder.TryDequeue(out string oldKey); i++)
                {
                    _memoryCache.TryRemove(oldKey, out _);
                }
            }
        }

        private class FrameDef
        {
            public double Cx { get; set; }
            public double Cy { get; set; }
            public double Rx { get; set; }
            public double Ry { get; set; }
            public Rect? ExtraBottomRect { get; set; }
            public double Zoom { get; set; } = 1.15;
        }

        private static readonly Dictionary<string, FrameDef> _frameDefs = new Dictionary<string, FrameDef>(StringComparer.OrdinalIgnoreCase)
        {
            { "frame_need_to_find", new FrameDef { Cx = 106.5, Cy = 131.0, Rx = 69.5, Ry = 88.0, Zoom = 1.02 } },
            { "frame_not_in_hand_yet", new FrameDef { Cx = 106.5, Cy = 131.0, Rx = 69.5, Ry = 88.0, Zoom = 1.02 } },
            { "frame_already_have", new FrameDef { Cx = 135.0, Cy = 169.0, Rx = 87.0, Ry = 127.0, Zoom = 1.02 } },
            { "frame_on_table", new FrameDef { Cx = 134.0, Cy = 152.0, Rx = 66.0, Ry = 99.0, Zoom = 1.02 } },
            { "frame_in_hand", new FrameDef { Cx = 142.0, Cy = 164.0, Rx = 60.0, Ry = 103.0, Zoom = 1.02 } },
            { "frame_1_green_hand", new FrameDef { Cx = 134.0, Cy = 152.0, Rx = 66.0, Ry = 99.0, Zoom = 1.02 } },
            { "frame_2_gold_dragon", new FrameDef { Cx = 142.0, Cy = 164.0, Rx = 60.0, Ry = 103.0, Zoom = 1.02 } },
            { "frame_3_star_priority", new FrameDef { Cx = 106.5, Cy = 131.0, Rx = 69.5, Ry = 88.0, Zoom = 1.02 } },
            { "frame_4_silver_board", new FrameDef { Cx = 135.0, Cy = 169.0, Rx = 87.0, Ry = 127.0, Zoom = 1.02 } },
            { "frame_5_fire_legendary", new FrameDef { Cx = 106.5, Cy = 131.0, Rx = 69.5, Ry = 88.0, Zoom = 1.02 } },
            { "frame_board", new FrameDef { Cx = 134.0, Cy = 152.0, Rx = 66.0, Ry = 99.0, Zoom = 1.02 } },
            { "frame_hand", new FrameDef { Cx = 142.0, Cy = 164.0, Rx = 60.0, Ry = 103.0, Zoom = 1.02 } },
            { "frame_missing", new FrameDef { Cx = 106.5, Cy = 131.0, Rx = 69.5, Ry = 88.0, Zoom = 1.02 } }
        };

        public LocalImageCache()
        {
            _assemblyDirectory = Path.GetDirectoryName(typeof(LocalImageCache).Assembly.Location) ?? AppDomain.CurrentDomain.BaseDirectory;
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string hdtPluginDir = Path.Combine(appData, "HearthstoneDeckTracker", "Plugins", "HSCompsPlugin");
            _cacheDirectory = Path.Combine(hdtPluginDir, "Cache", "Images");
            _primaryFullCardsDir = Path.Combine(hdtPluginDir, "assets", "cards", "full");

            try
            {
                if (!Directory.Exists(_cacheDirectory))
                {
                    Directory.CreateDirectory(_cacheDirectory);
                }
                if (!Directory.Exists(_primaryFullCardsDir))
                {
                    Directory.CreateDirectory(_primaryFullCardsDir);
                }
            }
            catch
            {
                _cacheDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Cache", "Images");
                _primaryFullCardsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "cards", "full");
                try { Directory.CreateDirectory(_cacheDirectory); } catch { }
                try { Directory.CreateDirectory(_primaryFullCardsDir); } catch { }
            }

            var rootCandidates = new List<string>
            {
                _assemblyDirectory,
                hdtPluginDir,
                AppDomain.CurrentDomain.BaseDirectory,
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins", "HSCompsPlugin"),
                @"E:\Coding\Projects\HSCompsPlugin",
                @"D:\Coding\Projects\HSCompsPlugin",
                @"C:\Coding\Projects\HSCompsPlugin"
            };

            try
            {
                string cur = _assemblyDirectory;
                for (int i = 0; i < 4 && !string.IsNullOrEmpty(cur); i++)
                {
                    rootCandidates.Add(cur);
                    cur = Directory.GetParent(cur)?.FullName;
                }
            }
            catch { }

            foreach (var r in rootCandidates)
            {
                if (!string.IsNullOrEmpty(r) && !_assetRootDirectories.Contains(r, StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        if (Directory.Exists(r))
                        {
                            _assetRootDirectories.Add(r);
                        }
                    }
                    catch { }
                }
            }

            try
            {
                foreach (var kvp in Cards.All)
                {
                    if (kvp.Value != null && kvp.Value.DbfId > 0 && !_dbfToCardId.ContainsKey(kvp.Value.DbfId))
                    {
                        _dbfToCardId[kvp.Value.DbfId] = kvp.Key;
                    }
                }
            }
            catch { }
        }

        public bool HasOfficialCardRender(int dbfId, string cardId = null)
        {
            var bmp = GetFullCardBitmap(dbfId, cardId);
            if (bmp == null) return false;
            return bmp.PixelHeight >= bmp.PixelWidth * 1.25;
        }

        public BitmapSource GetFullCardBitmap(int dbfId, string cardId = null, string remoteUrl = null)
        {
            string cleanId = !string.IsNullOrEmpty(cardId) && cardId.EndsWith("_G", StringComparison.OrdinalIgnoreCase)
                ? cardId.Substring(0, cardId.Length - 2)
                : cardId;

            if (IsSyntheticNumericBgId(cleanId) && dbfId > 0 && _dbfToCardId.TryGetValue(dbfId, out var mappedId))
            {
                cleanId = mappedId.EndsWith("_G", StringComparison.OrdinalIgnoreCase)
                    ? mappedId.Substring(0, mappedId.Length - 2)
                    : mappedId;
            }

            string key = $"full_{dbfId}_{cardId}";
            if (_memoryCache.TryGetValue(key, out var cached))
                return cached;

            string normKey = $"norm_{dbfId}_{cleanId}";
            if (_memoryCache.TryGetValue(normKey, out var cachedNorm))
                return cachedNorm;

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var candidatePaths = new List<string>();

            foreach (var root in _assetRootDirectories)
            {
                if (dbfId > 0)
                {
                    candidatePaths.Add(Path.Combine(root, "assets", "cards", "full", $"{dbfId}.png"));
                }
                if (!string.IsNullOrEmpty(cardId))
                {
                    candidatePaths.Add(Path.Combine(root, "assets", "cards", "full", $"{cardId}.png"));
                }
                if (!string.IsNullOrEmpty(cleanId))
                {
                    candidatePaths.Add(Path.Combine(root, "assets", "cards", "full", $"{cleanId}.png"));
                }
            }

            if (dbfId > 0)
            {
                candidatePaths.Add(Path.Combine(_cacheDirectory, $"{dbfId}_full.png"));
                candidatePaths.Add(Path.Combine(_cacheDirectory, $"{dbfId}.png"));
            }

            if (!string.IsNullOrEmpty(cardId))
            {
                candidatePaths.Add(Path.Combine(appData, "HearthstoneDeckTracker", "Images", "CardImages", $"{cardId}.png"));
                candidatePaths.Add(Path.Combine(_cacheDirectory, $"{cardId}.png"));
            }
            if (!string.IsNullOrEmpty(cleanId))
            {
                candidatePaths.Add(Path.Combine(appData, "HearthstoneDeckTracker", "Images", "CardImages", $"{cleanId}.png"));
                candidatePaths.Add(Path.Combine(_cacheDirectory, $"{cleanId}.png"));
            }

            foreach (var p in candidatePaths)
            {
                if (CachedFileExists(p))
                {
                    var bmp = LoadBitmapFromFile(p, decodePixelWidth: 450);
                    if (bmp != null && bmp.PixelHeight >= bmp.PixelWidth * 1.25)
                    {
                        PutMemoryCache(key, bmp);
                        PutMemoryCache(normKey, bmp);
                        return bmp;
                    }
                }
            }

            if (string.IsNullOrEmpty(remoteUrl))
            {
                var offCard = CardDatabaseService.Instance.GetCard(dbfId, cleanId) ?? CardDatabaseService.Instance.GetCard(dbfId, cardId);
                if (offCard != null && !string.IsNullOrEmpty(offCard.ImageRu))
                {
                    remoteUrl = offCard.ImageRu;
                }
            }

            if (!string.IsNullOrEmpty(remoteUrl) && !remoteUrl.Contains("/orig/"))
            {
                var saveAliases = new List<string>();
                if (!string.IsNullOrEmpty(_primaryFullCardsDir))
                {
                    if (!string.IsNullOrEmpty(cleanId) && !IsSyntheticNumericBgId(cleanId))
                        saveAliases.Add(Path.Combine(_primaryFullCardsDir, $"{cleanId}.png"));
                    if (dbfId > 0)
                        saveAliases.Add(Path.Combine(_primaryFullCardsDir, $"{dbfId}.png"));
                }

                var bmpAsync = GetImage(remoteUrl, decodePixelWidth: 450, saveAliases: saveAliases);
                if (bmpAsync != null && bmpAsync.PixelHeight >= bmpAsync.PixelWidth * 1.25)
                {
                    PutMemoryCache(key, bmpAsync);
                    PutMemoryCache(normKey, bmpAsync);
                    return bmpAsync;
                }
            }

            return null;
        }

        public BitmapSource GetNormalCardBitmap(int dbfId, string cardId = null, string remoteUrl = null)
        {
            string cleanId = cardId;
            if (!string.IsNullOrEmpty(cleanId) && cleanId.EndsWith("_G", StringComparison.OrdinalIgnoreCase))
            {
                cleanId = cleanId.Substring(0, cleanId.Length - 2);
            }

            int normalDbf = dbfId;
            try
            {
                if (!string.IsNullOrEmpty(cleanId) && Cards.All.TryGetValue(cleanId, out var dbCard) && dbCard != null && dbCard.DbfId > 0)
                {
                    normalDbf = dbCard.DbfId;
                }
            }
            catch { }

            string key = $"norm_{normalDbf}_{cleanId}";
            if (_memoryCache.TryGetValue(key, out var cached))
                return cached;

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var candidates = new List<string>();

            foreach (var root in _assetRootDirectories)
            {
                if (normalDbf > 0)
                {
                    candidates.Add(Path.Combine(root, "assets", "cards", "full", $"{normalDbf}.png"));
                }
                if (dbfId > 0 && dbfId != normalDbf)
                {
                    candidates.Add(Path.Combine(root, "assets", "cards", "full", $"{dbfId}.png"));
                }
                if (!string.IsNullOrEmpty(cleanId))
                {
                    candidates.Add(Path.Combine(root, "assets", "cards", "full", $"{cleanId}.png"));
                }
            }

            if (!string.IsNullOrEmpty(cleanId))
            {
                candidates.Add(Path.Combine(appData, "HearthstoneDeckTracker", "Images", "CardImages", $"{cleanId}.png"));
                candidates.Add(Path.Combine(_cacheDirectory, $"{cleanId}.png"));
            }
            if (normalDbf > 0)
            {
                candidates.Add(Path.Combine(_cacheDirectory, $"{normalDbf}.png"));
            }

            foreach (var p in candidates)
            {
                if (CachedFileExists(p))
                {
                    var bmp = LoadBitmapFromFile(p, decodePixelWidth: 450);
                    if (bmp != null && bmp.PixelHeight >= bmp.PixelWidth * 1.25)
                    {
                        PutMemoryCache(key, bmp);
                        return bmp;
                    }
                }
            }

            // Приоритет #1: официальный рендер Полей Сражений с CloudFront (offCard.ImageRu — 100% покрытие без 404)
            var fallback = GetFullCardBitmap(normalDbf, cleanId, remoteUrl);
            if (fallback != null)
            {
                PutMemoryCache(key, fallback);
                return fallback;
            }

            // Приоритет #2: если ImageRu отсутствует, пробуем hearthstonejson
            if (!string.IsNullOrEmpty(cleanId) && !IsSyntheticNumericBgId(cleanId))
            {
                var offCard = CardDatabaseService.Instance.GetCard(normalDbf, cleanId);
                if (offCard == null || string.IsNullOrEmpty(offCard.ImageRu))
                {
                    string remote = $"https://art.hearthstonejson.com/v1/bgs/latest/ruRU/512x/{cleanId}.png";
                    var bmp = GetImage(remote, decodePixelWidth: 450);
                    if (bmp != null)
                    {
                        PutMemoryCache(key, bmp);
                        return bmp;
                    }
                }
            }

            return null;
        }

        public BitmapSource GetGoldenCardBitmap(int goldenDbfId, string goldenCardId, string baseCardId)
        {
            string cleanId = baseCardId;
            if (IsSyntheticNumericBgId(cleanId))
            {
                cleanId = null;
            }
            if (string.IsNullOrEmpty(cleanId) && !string.IsNullOrEmpty(goldenCardId) && !IsSyntheticNumericBgId(goldenCardId))
            {
                cleanId = goldenCardId.EndsWith("_G", StringComparison.OrdinalIgnoreCase)
                    ? goldenCardId.Substring(0, goldenCardId.Length - 2)
                    : goldenCardId;
            }
            if (string.IsNullOrEmpty(cleanId) && goldenDbfId > 0 && _dbfToCardId.TryGetValue(goldenDbfId, out var mappedGoldId))
            {
                cleanId = mappedGoldId.EndsWith("_G", StringComparison.OrdinalIgnoreCase)
                    ? mappedGoldId.Substring(0, mappedGoldId.Length - 2)
                    : mappedGoldId;
            }

            int gDbf = goldenDbfId;
            string gCardId = !string.IsNullOrEmpty(goldenCardId) ? goldenCardId : $"{cleanId}_G";

            try
            {
                if (gDbf <= 0 && !string.IsNullOrEmpty(cleanId) && Cards.All.TryGetValue($"{cleanId}_G", out var ghCard) && ghCard != null)
                {
                    gDbf = ghCard.DbfId;
                }
            }
            catch { }

            string key = $"gold_{gDbf}_{gCardId}_{cleanId}";
            if (_memoryCache.TryGetValue(key, out var cached))
                return cached;

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var candidates = new List<string>();

            foreach (var root in _assetRootDirectories)
            {
                if (!string.IsNullOrEmpty(cleanId))
                {
                    candidates.Add(Path.Combine(root, "assets", "cards", "full", $"{cleanId}_G.png"));
                    candidates.Add(Path.Combine(root, "assets", "cards", "full", $"{cleanId}_triple.png"));
                    candidates.Add(Path.Combine(root, "assets", "cards", "full", $"{cleanId}_G_triple.png"));
                }
                if (!string.IsNullOrEmpty(goldenCardId))
                {
                    candidates.Add(Path.Combine(root, "assets", "cards", "full", $"{goldenCardId}.png"));
                }
                if (gDbf > 0)
                {
                    candidates.Add(Path.Combine(root, "assets", "cards", "full", $"{gDbf}.png"));
                }
            }

            if (!string.IsNullOrEmpty(cleanId))
            {
                candidates.Add(Path.Combine(appData, "HearthstoneDeckTracker", "Images", "CardImages", $"{cleanId}_G_triple.png"));
                candidates.Add(Path.Combine(appData, "HearthstoneDeckTracker", "Images", "CardImages", $"{cleanId}_triple.png"));
                candidates.Add(Path.Combine(appData, "HearthstoneDeckTracker", "Images", "CardImages", $"{cleanId}_G.png"));
                candidates.Add(Path.Combine(_cacheDirectory, $"{cleanId}_G.png"));
            }

            foreach (var p in candidates)
            {
                if (CachedFileExists(p))
                {
                    var bmp = LoadBitmapFromFile(p, decodePixelWidth: 450);
                    if (bmp != null && bmp.PixelHeight >= bmp.PixelWidth * 1.25)
                    {
                        PutMemoryCache(key, bmp);
                        return bmp;
                    }
                }
            }

            // Одиночная последовательная фоновая докачка золотой карты без создания 3 параллельных 404-запросов
            if (!string.IsNullOrEmpty(cleanId) && !IsSyntheticNumericBgId(cleanId))
            {
                TriggerGoldenCardDownloadSequential(cleanId, gDbf);
            }

            return null;
        }

        private void TriggerGoldenCardDownloadSequential(string cleanId, int gDbf)
        {
            string downloadKey = $"gold_seq_{cleanId}";
            if (_failedUrls.ContainsKey(downloadKey) || !_activeDownloads.TryAdd(downloadKey, true))
                return;

            Task.Run(async () =>
            {
                try
                {
                    var remoteUrls = new List<string>();
                    if (cleanId.StartsWith("TB_BaconUps_", StringComparison.OrdinalIgnoreCase))
                    {
                        remoteUrls.Add($"https://art.hearthstonejson.com/v1/bgs/latest/ruRU/512x/{cleanId}_triple.png");
                        remoteUrls.Add($"https://art.hearthstonejson.com/v1/render/latest/ruRU/512x/{cleanId}.png");
                    }
                    else
                    {
                        remoteUrls.Add($"https://art.hearthstonejson.com/v1/bgs/latest/ruRU/512x/{cleanId}_G_triple.png");
                        remoteUrls.Add($"https://art.hearthstonejson.com/v1/bgs/latest/ruRU/512x/{cleanId}_triple.png");
                        remoteUrls.Add($"https://art.hearthstonejson.com/v1/render/latest/ruRU/512x/{cleanId}_G.png");
                    }

                    foreach (var url in remoteUrls)
                    {
                        if (_failedUrls.ContainsKey(url)) continue;
                        try
                        {
                            var response = await _httpClient.GetAsync(url).ConfigureAwait(false);
                            if (response.IsSuccessStatusCode)
                            {
                                var data = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                                if (data != null && data.Length > 5000)
                                {
                                    string targetGold = Path.Combine(_primaryFullCardsDir, $"{cleanId}_G.png");
                                    File.WriteAllBytes(targetGold, data);
                                    _fileExistenceCache[targetGold] = true;

                                    if (gDbf > 0)
                                    {
                                        string targetDbf = Path.Combine(_primaryFullCardsDir, $"{gDbf}.png");
                                        if (!File.Exists(targetDbf))
                                        {
                                            File.WriteAllBytes(targetDbf, data);
                                            _fileExistenceCache[targetDbf] = true;
                                        }
                                    }

                                    ImageDownloaded?.Invoke(url);
                                    return;
                                }
                            }
                            else
                            {
                                _failedUrls[url] = true;
                            }
                        }
                        catch
                        {
                            _failedUrls[url] = true;
                        }
                    }

                    _failedUrls[downloadKey] = true;
                }
                catch { }
                finally
                {
                    _activeDownloads.TryRemove(downloadKey, out _);
                }
            });
        }

        public BitmapSource GetCropCardBitmap(int dbfId, string cardId = null, string remoteUrl = null)
        {
            return GetMinionArtBitmap(dbfId, cardId, remoteUrl);
        }

        private BitmapSource CropPortraitFromFullCard(BitmapSource fullBmp)
        {
            if (fullBmp == null || fullBmp.PixelWidth <= 100 || fullBmp.PixelHeight <= 100)
                return null;

            try
            {
                int cx = (int)(fullBmp.PixelWidth * 0.50);
                int cy = (int)(fullBmp.PixelHeight * 0.320);
                int rx = (int)(fullBmp.PixelWidth * 0.235);
                int ry = (int)(fullBmp.PixelHeight * 0.215);

                int px = Math.Max(0, cx - rx);
                int py = Math.Max(0, cy - ry);
                int pw = Math.Min(fullBmp.PixelWidth - px, rx * 2);
                int ph = Math.Min(fullBmp.PixelHeight - py, ry * 2);

                var cropped = new CroppedBitmap(fullBmp, new Int32Rect(px, py, pw, ph));
                cropped.Freeze();
                return cropped;
            }
            catch
            {
                return fullBmp;
            }
        }

        public BitmapSource GetMinionArtBitmap(int dbfId, string cardId = null, string remoteUrl = null)
        {
            string effectiveCardId = cardId;
            if (IsSyntheticNumericBgId(effectiveCardId))
            {
                if (dbfId > 0 && _dbfToCardId.TryGetValue(dbfId, out var mappedId))
                {
                    effectiveCardId = mappedId;
                }
            }

            string cleanCardId = !string.IsNullOrEmpty(effectiveCardId) && effectiveCardId.EndsWith("_G", StringComparison.OrdinalIgnoreCase)
                ? effectiveCardId.Substring(0, effectiveCardId.Length - 2)
                : effectiveCardId;

            string key = $"art_{dbfId}_{effectiveCardId}_{cleanCardId}";
            if (_memoryCache.TryGetValue(key, out var cached))
                return cached;

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            // 1. Проверяем локальные чистые вертикальные вырезки портретов (assets/cards/crop)
            var cropCandidates = new List<string>();
            foreach (var root in _assetRootDirectories)
            {
                if (!string.IsNullOrEmpty(cleanCardId))
                {
                    cropCandidates.Add(Path.Combine(root, "assets", "cards", "crop", $"{cleanCardId}.png"));
                }
                if (dbfId > 0)
                {
                    cropCandidates.Add(Path.Combine(root, "assets", "cards", "crop", $"{dbfId}.png"));
                }
            }

            foreach (var p in cropCandidates)
            {
                if (CachedFileExists(p))
                {
                    var bmp = LoadBitmapFromFile(p, decodePixelWidth: 260);
                    if (bmp != null && bmp.PixelHeight >= bmp.PixelWidth * 0.75)
                    {
                        PutMemoryCache(key, bmp);
                        return bmp;
                    }
                }
            }

            // 2. Проверяем кэш чистых портретов HearthstoneDeckTracker (.jpg и .png)
            if (!string.IsNullOrEmpty(effectiveCardId))
            {
                var idList = (cleanCardId != effectiveCardId)
                    ? new[] { effectiveCardId, cleanCardId }
                    : new[] { effectiveCardId };

                foreach (var id in idList)
                {
                    var portraitJpg = Path.Combine(appData, "HearthstoneDeckTracker", "Images", "CardPortraits", $"{id}.jpg");
                    if (CachedFileExists(portraitJpg))
                    {
                        var bmp = LoadBitmapFromFile(portraitJpg, decodePixelWidth: 260);
                        if (bmp != null)
                        {
                            PutMemoryCache(key, bmp);
                            return bmp;
                        }
                    }

                    var portraitPng = Path.Combine(appData, "HearthstoneDeckTracker", "Images", "CardPortraits", $"{id}.png");
                    if (CachedFileExists(portraitPng))
                    {
                        var bmp = LoadBitmapFromFile(portraitPng, decodePixelWidth: 260);
                        if (bmp != null)
                        {
                            PutMemoryCache(key, bmp);
                            return bmp;
                        }
                    }
                }
            }

            // 3. Мгновенная вырезка овального портрета из полной карты (если полная карта уже в памяти или на диске)
            var fullFromCacheOrDisk = GetNormalCardBitmap(dbfId, cleanCardId);
            if (fullFromCacheOrDisk != null)
            {
                var cropped = CropPortraitFromFullCard(fullFromCacheOrDisk);
                if (cropped != null)
                {
                    PutMemoryCache(key, cropped);
                    return cropped;
                }
            }

            // 4. Резерв: если передан прямой URL кропа
            if (!string.IsNullOrEmpty(remoteUrl))
            {
                var bmp = GetImage(remoteUrl, decodePixelWidth: 260);
                if (bmp != null)
                {
                    PutMemoryCache(key, bmp);
                    return bmp;
                }
            }

            return null;
        }

        public BitmapSource GetRingBitmap(SlotStatus status)
        {
            string ringFile = "ring_bob.png";
            switch (status)
            {
                case SlotStatus.OnBoard:
                    ringFile = "ring_board.png";
                    break;
                case SlotStatus.InHand:
                    ringFile = "ring_hand.png";
                    break;
                case SlotStatus.NeedToFind:
                default:
                    ringFile = "ring_bob.png";
                    break;
            }

            string key = $"ring_{ringFile}";
            if (_memoryCache.TryGetValue(key, out var cached))
                return cached;

            foreach (var root in _assetRootDirectories)
            {
                string p = Path.Combine(root, "assets", "ui", ringFile);
                if (CachedFileExists(p))
                {
                    var bmp = LoadBitmapFromFile(p);
                    if (bmp != null)
                    {
                        PutMemoryCache(key, bmp);
                        return bmp;
                    }
                }
            }

            return null;
        }

        public BitmapSource GetFrameBitmap(string frameName)
        {
            if (string.IsNullOrEmpty(frameName)) return null;
            string key = $"frame_{frameName}";
            if (_memoryCache.TryGetValue(key, out var cached))
                return cached;

            string fileName = frameName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? frameName : $"{frameName}.png";

            foreach (var root in _assetRootDirectories)
            {
                string p1 = Path.Combine(root, "assets", "ui", "frames", fileName);
                if (CachedFileExists(p1))
                {
                    var bmp = LoadBitmapFromFile(p1);
                    if (bmp != null)
                    {
                        PutMemoryCache(key, bmp);
                        return bmp;
                    }
                }

                string p2 = Path.Combine(root, "assets", "ui", fileName);
                if (CachedFileExists(p2))
                {
                    var bmp = LoadBitmapFromFile(p2);
                    if (bmp != null)
                    {
                        PutMemoryCache(key, bmp);
                        return bmp;
                    }
                }
            }

            return null;
        }

        public BitmapSource GetMaskBitmap(string frameName)
        {
            if (string.IsNullOrEmpty(frameName)) return null;
            string key = $"mask_{frameName}";
            if (_memoryCache.TryGetValue(key, out var cached))
                return cached;

            string fileName = frameName.StartsWith("mask_", StringComparison.OrdinalIgnoreCase)
                ? frameName
                : $"mask_{frameName}";
            if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                fileName += ".png";

            foreach (var root in _assetRootDirectories)
            {
                string p1 = Path.Combine(root, "assets", "ui", "frames", fileName);
                if (CachedFileExists(p1))
                {
                    var bmp = LoadBitmapFromFile(p1);
                    if (bmp != null)
                    {
                        PutMemoryCache(key, bmp);
                        return bmp;
                    }
                }

                string p2 = Path.Combine(root, "assets", "ui", fileName);
                if (CachedFileExists(p2))
                {
                    var bmp = LoadBitmapFromFile(p2);
                    if (bmp != null)
                    {
                        PutMemoryCache(key, bmp);
                        return bmp;
                    }
                }
            }

            return null;
        }

        public BitmapSource GetFramedMinionBitmap(int dbfId, string cardId, string frameName, SlotStatus status = SlotStatus.NeedToFind, string remoteUrl = null)
        {
            string effectiveCardId = cardId;
            if (IsSyntheticNumericBgId(effectiveCardId))
            {
                if (dbfId > 0 && _dbfToCardId.TryGetValue(dbfId, out var mappedId))
                {
                    effectiveCardId = mappedId;
                }
            }

            string key = $"framed_{dbfId}_{effectiveCardId}_{frameName}_{status}";
            if (_memoryCache.TryGetValue(key, out var cached))
                return cached;

            var frameBmp = GetFrameBitmap(frameName);
            var artBmp = GetMinionArtBitmap(dbfId, effectiveCardId, remoteUrl);

            if (frameBmp == null)
            {
                return artBmp ?? GetFullCardBitmap(dbfId, effectiveCardId, remoteUrl);
            }

            if (artBmp == null)
            {
                return frameBmp;
            }

            if (!_frameDefs.TryGetValue(frameName, out var def))
            {
                def = _frameDefs["frame_need_to_find"];
            }

            int fw = frameBmp.PixelWidth;
            int fh = frameBmp.PixelHeight;

            double targetH = def.Ry * 2.0 * def.Zoom;
            double scale = targetH / artBmp.PixelHeight;
            double drawW = artBmp.PixelWidth * scale;
            double drawH = artBmp.PixelHeight * scale;

            double targetW = def.Rx * 2.0 * def.Zoom;
            if (drawW < targetW)
            {
                scale = targetW / artBmp.PixelWidth;
                drawW = artBmp.PixelWidth * scale;
                drawH = artBmp.PixelHeight * scale;
            }

            double drawX = def.Cx - (drawW / 2.0);
            double drawY = def.Cy - (drawH / 2.0);

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.PushClip(new EllipseGeometry(new Point(def.Cx, def.Cy), def.Rx, def.Ry));
                dc.DrawImage(artBmp, new Rect(drawX, drawY, drawW, drawH));
                dc.Pop();

                dc.DrawImage(frameBmp, new Rect(0, 0, fw, fh));
            }

            var rtb = new RenderTargetBitmap(fw, fh, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();

            PutMemoryCache(key, rtb);
            return rtb;
        }

        public BitmapSource GetImage(string url, int decodePixelWidth = 0, IEnumerable<string> saveAliases = null)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            if (_failedUrls.ContainsKey(url)) return null;

            string key = decodePixelWidth > 0 ? $"{url}_{decodePixelWidth}" : url;
            if (_memoryCache.TryGetValue(key, out var cachedImage))
                return cachedImage;

            string localFilePath = GetLocalFilePath(url);

            if (CachedFileExists(localFilePath))
            {
                try
                {
                    var bmp = LoadBitmapFromFile(localFilePath, decodePixelWidth);
                    if (bmp != null)
                    {
                        PutMemoryCache(key, bmp);
                        return bmp;
                    }
                }
                catch { }
            }

            TriggerBackgroundDownload(url, localFilePath, saveAliases);
            return null;
        }

        private void TriggerBackgroundDownload(string url, string localFilePath, IEnumerable<string> saveAliases = null)
        {
            if (_failedUrls.ContainsKey(url)) return;

            if (_activeDownloads.TryAdd(url, true))
            {
                var aliasesList = saveAliases != null ? new List<string>(saveAliases) : null;
                Task.Run(async () =>
                {
                    try
                    {
                        var response = await _httpClient.GetAsync(url).ConfigureAwait(false);
                        if (response.IsSuccessStatusCode)
                        {
                            var data = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                            if (data != null && data.Length > 2000)
                            {
                                string dir = Path.GetDirectoryName(localFilePath);
                                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                                {
                                    Directory.CreateDirectory(dir);
                                }

                                File.WriteAllBytes(localFilePath, data);
                                _fileExistenceCache[localFilePath] = true;

                                if (aliasesList != null)
                                {
                                    foreach (var aliasPath in aliasesList)
                                    {
                                        if (string.IsNullOrEmpty(aliasPath)) continue;
                                        try
                                        {
                                            string aliasDir = Path.GetDirectoryName(aliasPath);
                                            if (!string.IsNullOrEmpty(aliasDir) && !Directory.Exists(aliasDir))
                                                Directory.CreateDirectory(aliasDir);
                                            if (!File.Exists(aliasPath))
                                                File.WriteAllBytes(aliasPath, data);
                                            _fileExistenceCache[aliasPath] = true;
                                        }
                                        catch { }
                                    }
                                }

                                ImageDownloaded?.Invoke(url);
                            }
                        }
                        else if ((int)response.StatusCode == 404)
                        {
                            _failedUrls[url] = true;
                        }
                    }
                    catch { }
                    finally
                    {
                        _activeDownloads.TryRemove(url, out _);
                    }
                });
            }
        }

        /// <summary>
        /// Параллельная предзагрузка отсутствующих карт для активных рекомендованных столов перед отрисовкой UI,
        /// чтобы все 7 карт появлялись мгновенно с первого кадра без задержки в несколько ходов.
        /// </summary>
        public void EnsureCompCardsLoaded(IEnumerable<CompRecommendation> comps, int timeoutMs = 950)
        {
            if (comps == null) return;
            try
            {
                var tasks = new List<Task>();
                var seenDbfs = new HashSet<int>();

                foreach (var comp in comps)
                {
                    if (comp == null) continue;
                    var allSlots = new List<BoardSlot>();
                    if (comp.DisplaySlots != null) allSlots.AddRange(comp.DisplaySlots);
                    if (comp.MissingPriorityCards != null) allSlots.AddRange(comp.MissingPriorityCards);

                    foreach (var slot in allSlots)
                    {
                        if (slot == null) continue;
                        int dbf = slot.DbfId > 0 ? slot.DbfId : (slot.Card?.Id ?? 0);
                        if (dbf <= 0 || !seenDbfs.Add(dbf)) continue;

                        string cleanId = !string.IsNullOrEmpty(slot.CardId) && slot.CardId.EndsWith("_G", StringComparison.OrdinalIgnoreCase)
                            ? slot.CardId.Substring(0, slot.CardId.Length - 2)
                            : slot.CardId;

                        bool alreadyOnDisk = false;
                        foreach (var root in _assetRootDirectories)
                        {
                            if (CachedFileExists(Path.Combine(root, "assets", "cards", "full", $"{dbf}.png")) ||
                                (!string.IsNullOrEmpty(cleanId) && CachedFileExists(Path.Combine(root, "assets", "cards", "full", $"{cleanId}.png"))))
                            {
                                alreadyOnDisk = true;
                                break;
                            }
                        }
                        if (alreadyOnDisk) continue;

                        var offCard = slot.Card ?? CardDatabaseService.Instance.GetCard(dbf, cleanId);
                        string remoteUrl = !string.IsNullOrEmpty(slot.FullImageUrl) ? slot.FullImageUrl : offCard?.ImageRu;
                        if (string.IsNullOrEmpty(remoteUrl) || _failedUrls.ContainsKey(remoteUrl)) continue;

                        string localCachePath = GetLocalFilePath(remoteUrl);
                        if (CachedFileExists(localCachePath)) continue;

                        string targetDbfPath = Path.Combine(_primaryFullCardsDir, $"{dbf}.png");
                        string targetCardPath = (!string.IsNullOrEmpty(cleanId) && !IsSyntheticNumericBgId(cleanId))
                            ? Path.Combine(_primaryFullCardsDir, $"{cleanId}.png")
                            : null;

                        tasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                var resp = await _httpClient.GetAsync(remoteUrl).ConfigureAwait(false);
                                if (resp.IsSuccessStatusCode)
                                {
                                    var bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                                    if (bytes != null && bytes.Length > 2000)
                                    {
                                        File.WriteAllBytes(localCachePath, bytes);
                                        _fileExistenceCache[localCachePath] = true;

                                        try
                                        {
                                            File.WriteAllBytes(targetDbfPath, bytes);
                                            _fileExistenceCache[targetDbfPath] = true;
                                        }
                                        catch { }

                                        if (!string.IsNullOrEmpty(targetCardPath))
                                        {
                                            try
                                            {
                                                File.WriteAllBytes(targetCardPath, bytes);
                                                _fileExistenceCache[targetCardPath] = true;
                                            }
                                            catch { }
                                        }
                                    }
                                }
                            }
                            catch { }
                        }));
                    }
                }

                if (tasks.Count > 0)
                {
                    Task.WaitAll(tasks.ToArray(), millisecondsTimeout: timeoutMs);
                }
            }
            catch { }
        }

        private BitmapImage LoadBitmapFromFile(string filePath, int decodePixelWidth = 0)
        {
            try
            {
                if (!File.Exists(filePath)) return null;

                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    if (decodePixelWidth > 0)
                    {
                        bitmap.DecodePixelWidth = decodePixelWidth;
                    }
                    bitmap.StreamSource = fs;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
            }
            catch
            {
                return null;
            }
        }

        private string GetLocalFilePath(string url)
        {
            using (var md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(url));
                string hexHash = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                return Path.Combine(_cacheDirectory, $"{hexHash}.png");
            }
        }

        public (int fileCount, double sizeMb) GetDiskCacheStats()
        {
            try
            {
                if (!Directory.Exists(_cacheDirectory)) return (0, 0.0);
                var files = Directory.GetFiles(_cacheDirectory, "*.*", SearchOption.AllDirectories);
                long totalBytes = 0;
                foreach (var f in files)
                {
                    try
                    {
                        totalBytes += new FileInfo(f).Length;
                    }
                    catch { }
                }
                return (files.Length, Math.Round(totalBytes / (1024.0 * 1024.0), 2));
            }
            catch
            {
                return (0, 0.0);
            }
        }

        public (int deletedFiles, double freedMb) ClearCache()
        {
            int deleted = 0;
            long freedBytes = 0;
            try
            {
                _memoryCache.Clear();
                while (_cacheKeyOrder.TryDequeue(out _)) { }

                if (Directory.Exists(_cacheDirectory))
                {
                    var files = Directory.GetFiles(_cacheDirectory, "*.*", SearchOption.AllDirectories);
                    foreach (var f in files)
                    {
                        try
                        {
                            long len = new FileInfo(f).Length;
                            File.Delete(f);
                            deleted++;
                            freedBytes += len;
                        }
                        catch { }
                    }
                }

                _fileExistenceCache.Clear();
                _failedUrls.Clear();
            }
            catch { }
            return (deleted, Math.Round(freedBytes / (1024.0 * 1024.0), 2));
        }

        public void PrewarmLobbyCardsAsync(IEnumerable<string> availableTribes = null)
        {
            var tribesSnapshot = availableTribes != null ? new List<string>(availableTribes) : null;
            Task.Run(async () =>
            {
                try
                {
                    GetFrameBitmap("frame_need_to_find");
                    GetFrameBitmap("frame_not_in_hand_yet");
                    GetFrameBitmap("frame_already_have");
                    GetFrameBitmap("frame_on_table");
                    GetFrameBitmap("frame_in_hand");
                    GetRingBitmap(SlotStatus.OnBoard);
                    GetRingBitmap(SlotStatus.InHand);
                    GetRingBitmap(SlotStatus.NeedToFind);

                    if (!VectorSearchEngine.Instance.IsLoaded) return;
                    var allBoards = VectorSearchEngine.Instance.AllBoards;
                    if (allBoards == null || allBoards.Count == 0) return;

                    TribeFlags availMask = (tribesSnapshot != null && tribesSnapshot.Count >= 3)
                        ? TribeFlagsHelper.ListToMask(tribesSnapshot)
                        : TribeFlags.None;

                    var seenDbfs = new HashSet<int>();
                    var orderedSlots = new List<BoardSlot>();

                    // Сначала карты активных племен лобби, затем все остальные (всего 162 уникальных существа в базе)
                    foreach (var board in allBoards)
                    {
                        if (board?.Minions == null) continue;
                        if (availMask != TribeFlags.None &&
                            board.PrimaryTribeFlag != TribeFlags.None &&
                            board.PrimaryTribeFlag != TribeFlags.All &&
                            (board.PrimaryTribeFlag & availMask) == TribeFlags.None)
                        {
                            continue;
                        }

                        foreach (var slot in board.Minions)
                        {
                            if (slot == null) continue;
                            int dbf = slot.DbfId > 0 ? slot.DbfId : (slot.Card?.Id ?? 0);
                            if (dbf > 0 && seenDbfs.Add(dbf))
                            {
                                orderedSlots.Add(slot);
                            }
                        }
                    }

                    foreach (var board in allBoards)
                    {
                        if (board?.Minions == null) continue;
                        foreach (var slot in board.Minions)
                        {
                            if (slot == null) continue;
                            int dbf = slot.DbfId > 0 ? slot.DbfId : (slot.Card?.Id ?? 0);
                            if (dbf > 0 && seenDbfs.Add(dbf))
                            {
                                orderedSlots.Add(slot);
                            }
                        }
                    }

                    using (var sem = new SemaphoreSlim(16))
                    {
                        var downloadTasks = new List<Task>();
                        foreach (var slot in orderedSlots)
                        {
                            int dbf = slot.DbfId > 0 ? slot.DbfId : (slot.Card?.Id ?? 0);
                            string cleanId = !string.IsNullOrEmpty(slot.CardId) && slot.CardId.EndsWith("_G", StringComparison.OrdinalIgnoreCase)
                                ? slot.CardId.Substring(0, slot.CardId.Length - 2)
                                : slot.CardId;

                            // Если уже есть на диске — просто прогреваем в памяти для первых 60 карт лобби
                            bool existsLocally = false;
                            foreach (var root in _assetRootDirectories)
                            {
                                if (CachedFileExists(Path.Combine(root, "assets", "cards", "full", $"{dbf}.png")) ||
                                    (!string.IsNullOrEmpty(cleanId) && CachedFileExists(Path.Combine(root, "assets", "cards", "full", $"{cleanId}.png"))))
                                {
                                    existsLocally = true;
                                    break;
                                }
                            }

                            if (existsLocally)
                            {
                                GetNormalCardBitmap(dbf, cleanId, slot.FullImageUrl);
                                GetMinionArtBitmap(dbf, slot.CardId, slot.CropImageUrl);
                                continue;
                            }

                            var offCard = slot.Card ?? CardDatabaseService.Instance.GetCard(dbf, cleanId);
                            string url = !string.IsNullOrEmpty(slot.FullImageUrl) ? slot.FullImageUrl : offCard?.ImageRu;
                            if (string.IsNullOrEmpty(url) || _failedUrls.ContainsKey(url)) continue;

                            downloadTasks.Add(Task.Run(async () =>
                            {
                                await sem.WaitAsync().ConfigureAwait(false);
                                try
                                {
                                    string targetDbf = Path.Combine(_primaryFullCardsDir, $"{dbf}.png");
                                    string targetClean = (!string.IsNullOrEmpty(cleanId) && !IsSyntheticNumericBgId(cleanId))
                                        ? Path.Combine(_primaryFullCardsDir, $"{cleanId}.png")
                                        : null;

                                    if (CachedFileExists(targetDbf)) return;

                                    var resp = await _httpClient.GetAsync(url).ConfigureAwait(false);
                                    if (resp.IsSuccessStatusCode)
                                    {
                                        var bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                                        if (bytes != null && bytes.Length > 2000)
                                        {
                                            File.WriteAllBytes(targetDbf, bytes);
                                            _fileExistenceCache[targetDbf] = true;
                                            if (!string.IsNullOrEmpty(targetClean))
                                            {
                                                File.WriteAllBytes(targetClean, bytes);
                                                _fileExistenceCache[targetClean] = true;
                                            }
                                            ImageDownloaded?.Invoke(url);
                                        }
                                    }
                                }
                                catch { }
                                finally
                                {
                                    sem.Release();
                                }
                            }));
                        }

                        if (downloadTasks.Count > 0)
                        {
                            await Task.WhenAll(downloadTasks).ConfigureAwait(false);
                        }
                    }
                }
                catch { }
            });
        }
    }
}
