using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
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
        private readonly ConcurrentDictionary<string, BitmapSource> _memoryCache = new ConcurrentDictionary<string, BitmapSource>();
        private readonly ConcurrentQueue<string> _cacheKeyOrder = new ConcurrentQueue<string>();
        private const int MaxMemoryCacheEntries = 450;
        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly ConcurrentDictionary<string, bool> _activeDownloads = new ConcurrentDictionary<string, bool>();
        private readonly Dictionary<int, string> _dbfToCardId = new Dictionary<int, string>();

        private void PutMemoryCache(string key, BitmapSource bmp)
        {
            if (bmp == null || string.IsNullOrEmpty(key)) return;
            _memoryCache[key] = bmp;
            _cacheKeyOrder.Enqueue(key);

            if (_memoryCache.Count > MaxMemoryCacheEntries)
            {
                for (int i = 0; i < 25 && _cacheKeyOrder.TryDequeue(out string oldKey); i++)
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
            { "frame_frozen", new FrameDef { Cx = 140.0, Cy = 161.0, Rx = 56.0, Ry = 100.0, Zoom = 1.02 } },
            // Обратная совместимость для старых идентификаторов
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
            _cacheDirectory = Path.Combine(appData, "HearthstoneDeckTracker", "Plugins", "HSCompsPlugin", "Cache", "Images");

            try
            {
                if (!Directory.Exists(_cacheDirectory))
                {
                    Directory.CreateDirectory(_cacheDirectory);
                }
            }
            catch
            {
                _cacheDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Cache", "Images");
                Directory.CreateDirectory(_cacheDirectory);
            }

            // Маппинг DbfId -> CardId из официальной базы HearthDb
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
            string key = $"full_{dbfId}_{cardId}";
            if (_memoryCache.TryGetValue(key, out var cached))
                return cached;

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            if (!string.IsNullOrEmpty(cardId) && cardId.EndsWith("_G", StringComparison.OrdinalIgnoreCase))
            {
                string clean = cardId.Substring(0, cardId.Length - 2);
                var goldBmp = GetGoldenCardBitmap(dbfId, cardId, clean);
                if (goldBmp != null)
                {
                    _memoryCache[key] = goldBmp;
                    return goldBmp;
                }
            }

            var candidatePaths = new List<string>
            {
                Path.Combine(_assemblyDirectory, "assets", "cards", "full", $"{dbfId}.png"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "cards", "full", $"{dbfId}.png"),
                Path.Combine(appData, "HearthstoneDeckTracker", "Plugins", "HSCompsPlugin", "assets", "cards", "full", $"{dbfId}.png"),
                Path.Combine(_cacheDirectory, $"{dbfId}_full.png")
            };

            if (!string.IsNullOrEmpty(cardId))
            {
                string cleanId = cardId.EndsWith("_G", StringComparison.OrdinalIgnoreCase)
                    ? cardId.Substring(0, cardId.Length - 2)
                    : cardId;

                candidatePaths.Add(Path.Combine(appData, "HearthstoneDeckTracker", "Images", "CardImages", $"{cardId}.png"));
                candidatePaths.Add(Path.Combine(appData, "HearthstoneDeckTracker", "Images", "CardImages", $"{cleanId}.png"));
                candidatePaths.Add(Path.Combine(appData, "HearthstoneDeckTracker", "Images", "CardImages", $"{cardId}_triple.png"));
                candidatePaths.Add(Path.Combine(appData, "HearthstoneDeckTracker", "Images", "CardImages", $"{cleanId}_G_triple.png"));
                candidatePaths.Add(Path.Combine(appData, "HearthstoneDeckTracker", "Images", "CardImages", $"{cleanId}_triple.png"));
                candidatePaths.Add(Path.Combine(_assemblyDirectory, "assets", "cards", "full", $"{cardId}.png"));
                candidatePaths.Add(Path.Combine(_assemblyDirectory, "assets", "cards", "full", $"{cleanId}.png"));
                candidatePaths.Add(Path.Combine(appData, "HearthstoneDeckTracker", "Plugins", "HSCompsPlugin", "assets", "cards", "full", $"{cardId}.png"));
                candidatePaths.Add(Path.Combine(appData, "HearthstoneDeckTracker", "Plugins", "HSCompsPlugin", "assets", "cards", "full", $"{cleanId}.png"));
                candidatePaths.Add(Path.Combine(_cacheDirectory, $"{cardId}.png"));
            }

            foreach (var p in candidatePaths)
            {
                if (File.Exists(p))
                {
                    var bmp = LoadBitmapFromFile(p, decodePixelWidth: 450);
                    if (bmp != null && bmp.PixelHeight >= bmp.PixelWidth * 1.25)
                    {
                        PutMemoryCache(key, bmp);
                        return bmp;
                    }
                }
            }

            if (string.IsNullOrEmpty(remoteUrl))
            {
                var offCard = CardDatabaseService.Instance.GetCard(dbfId, cardId);
                if (offCard != null && !string.IsNullOrEmpty(offCard.ImageRu))
                {
                    remoteUrl = offCard.ImageRu;
                }
            }

            if (!string.IsNullOrEmpty(remoteUrl) && !remoteUrl.Contains("/orig/"))
            {
                string localFilePath = GetLocalFilePath(remoteUrl);
                if (!File.Exists(localFilePath))
                {
                    try
                    {
                        var task = _httpClient.GetAsync(remoteUrl);
                        if (task.Wait(1800))
                        {
                            var response = task.Result;
                            if (response.IsSuccessStatusCode)
                            {
                                var data = response.Content.ReadAsByteArrayAsync().Result;
                                string dir = Path.GetDirectoryName(localFilePath);
                                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                                {
                                    Directory.CreateDirectory(dir);
                                }
                                File.WriteAllBytes(localFilePath, data);
                            }
                        }
                    }
                    catch { }
                }

                if (File.Exists(localFilePath))
                {
                    var bmp = LoadBitmapFromFile(localFilePath, decodePixelWidth: 450);
                    if (bmp != null && bmp.PixelHeight >= bmp.PixelWidth * 1.25)
                    {
                        PutMemoryCache(key, bmp);
                        return bmp;
                    }
                }

                var bmpAsync = GetImage(remoteUrl, decodePixelWidth: 450);
                if (bmpAsync != null && bmpAsync.PixelHeight >= bmpAsync.PixelWidth * 1.25)
                {
                    PutMemoryCache(key, bmpAsync);
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
                if (!string.IsNullOrEmpty(cleanId) && Cards.All.TryGetValue(cleanId, out var dbCard) && dbCard != null)
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

            if (normalDbf > 0)
            {
                candidates.Add(Path.Combine(_assemblyDirectory, "assets", "cards", "full", $"{normalDbf}.png"));
                candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "cards", "full", $"{normalDbf}.png"));
                candidates.Add(Path.Combine(appData, "HearthstoneDeckTracker", "Plugins", "HSCompsPlugin", "assets", "cards", "full", $"{normalDbf}.png"));
            }

            if (!string.IsNullOrEmpty(cleanId))
            {
                candidates.Add(Path.Combine(_assemblyDirectory, "assets", "cards", "full", $"{cleanId}.png"));
                candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "cards", "full", $"{cleanId}.png"));
                candidates.Add(Path.Combine(appData, "HearthstoneDeckTracker", "Plugins", "HSCompsPlugin", "assets", "cards", "full", $"{cleanId}.png"));
                candidates.Add(Path.Combine(appData, "HearthstoneDeckTracker", "Images", "CardImages", $"{cleanId}.png"));
                candidates.Add(Path.Combine(_cacheDirectory, $"{cleanId}.png"));
            }

            foreach (var p in candidates)
            {
                if (File.Exists(p))
                {
                    var bmp = LoadBitmapFromFile(p, decodePixelWidth: 450);
                    if (bmp != null && bmp.PixelHeight >= bmp.PixelWidth * 1.25)
                    {
                        PutMemoryCache(key, bmp);
                        return bmp;
                    }
                }
            }

            // Fallback: загрузка официального рендера с hearthstonejson
            if (!string.IsNullOrEmpty(cleanId))
            {
                string remote = $"https://art.hearthstonejson.com/v1/render/latest/ruRU/512x/{cleanId}.png";
                var bmp = GetImage(remote, decodePixelWidth: 450);
                if (bmp != null)
                {
                    PutMemoryCache(key, bmp);
                    return bmp;
                }
            }

            var fallback = GetFullCardBitmap(normalDbf, cleanId, remoteUrl);
            if (fallback != null)
            {
                PutMemoryCache(key, fallback);
                return fallback;
            }

            return null;
        }

        public BitmapSource GetGoldenCardBitmap(int goldenDbfId, string goldenCardId, string baseCardId)
        {
            string cleanId = baseCardId;
            if (string.IsNullOrEmpty(cleanId) && !string.IsNullOrEmpty(goldenCardId))
            {
                cleanId = goldenCardId.EndsWith("_G", StringComparison.OrdinalIgnoreCase)
                    ? goldenCardId.Substring(0, goldenCardId.Length - 2)
                    : goldenCardId;
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

            if (!string.IsNullOrEmpty(cleanId))
            {
                candidates.Add(Path.Combine(_assemblyDirectory, "assets", "cards", "full", $"{cleanId}_G.png"));
                candidates.Add(Path.Combine(_assemblyDirectory, "assets", "cards", "full", $"{cleanId}_triple.png"));
                candidates.Add(Path.Combine(_assemblyDirectory, "assets", "cards", "full", $"{cleanId}_G_triple.png"));
                candidates.Add(Path.Combine(appData, "HearthstoneDeckTracker", "Plugins", "HSCompsPlugin", "assets", "cards", "full", $"{cleanId}_G.png"));
                candidates.Add(Path.Combine(appData, "HearthstoneDeckTracker", "Plugins", "HSCompsPlugin", "assets", "cards", "full", $"{cleanId}_triple.png"));
                candidates.Add(Path.Combine(appData, "HearthstoneDeckTracker", "Images", "CardImages", $"{cleanId}_G_triple.png"));
                candidates.Add(Path.Combine(appData, "HearthstoneDeckTracker", "Images", "CardImages", $"{cleanId}_triple.png"));
                candidates.Add(Path.Combine(appData, "HearthstoneDeckTracker", "Images", "CardImages", $"{cleanId}_G.png"));
                candidates.Add(Path.Combine(_cacheDirectory, $"{cleanId}_G.png"));
            }

            if (gDbf > 0)
            {
                candidates.Add(Path.Combine(_assemblyDirectory, "assets", "cards", "full", $"{gDbf}.png"));
                candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "cards", "full", $"{gDbf}.png"));
                candidates.Add(Path.Combine(appData, "HearthstoneDeckTracker", "Plugins", "HSCompsPlugin", "assets", "cards", "full", $"{gDbf}.png"));
            }

            foreach (var p in candidates)
            {
                if (File.Exists(p))
                {
                    var bmp = LoadBitmapFromFile(p, decodePixelWidth: 450);
                    if (bmp != null && bmp.PixelHeight >= bmp.PixelWidth * 1.25)
                    {
                        PutMemoryCache(key, bmp);
                        return bmp;
                    }
                }
            }

            // Fallback: загрузка золотого рендера с hearthstonejson
            if (!string.IsNullOrEmpty(cleanId))
            {
                string remote = $"https://art.hearthstonejson.com/v1/render/latest/ruRU/512x/{cleanId}_G.png";
                var bmp = GetImage(remote, decodePixelWidth: 450);
                if (bmp != null)
                {
                    PutMemoryCache(key, bmp);
                    return bmp;
                }
            }

            return null;
        }

        public BitmapSource GetCropCardBitmap(int dbfId, string cardId = null, string remoteUrl = null)
        {
            return GetMinionArtBitmap(dbfId, cardId, remoteUrl);
        }

        public BitmapSource GetMinionArtBitmap(int dbfId, string cardId = null, string remoteUrl = null)
        {
            string effectiveCardId = cardId;
            if (string.IsNullOrEmpty(effectiveCardId) || !effectiveCardId.StartsWith("BG", StringComparison.OrdinalIgnoreCase))
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

            // 1. Проверяем локальные чистые вырезки портретов (assets/cards/crop)
            // Приоритет чистому коду карты (например BG36_102) над числовым dbfId,
            // и отсекаем горизонтальные плашки колод (где высота < 0.75 ширины).
            var cropCandidates = new List<string>();
            if (!string.IsNullOrEmpty(cleanCardId))
            {
                cropCandidates.Add(Path.Combine(_assemblyDirectory, "assets", "cards", "crop", $"{cleanCardId}.png"));
                cropCandidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "cards", "crop", $"{cleanCardId}.png"));
                cropCandidates.Add(Path.Combine(appData, "HearthstoneDeckTracker", "Plugins", "HSCompsPlugin", "assets", "cards", "crop", $"{cleanCardId}.png"));
            }
            if (dbfId > 0)
            {
                cropCandidates.Add(Path.Combine(_assemblyDirectory, "assets", "cards", "crop", $"{dbfId}.png"));
                cropCandidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "cards", "crop", $"{dbfId}.png"));
                cropCandidates.Add(Path.Combine(appData, "HearthstoneDeckTracker", "Plugins", "HSCompsPlugin", "assets", "cards", "crop", $"{dbfId}.png"));
            }

            foreach (var p in cropCandidates)
            {
                if (File.Exists(p))
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
                    if (File.Exists(portraitJpg))
                    {
                        var bmp = LoadBitmapFromFile(portraitJpg, decodePixelWidth: 260);
                        if (bmp != null)
                        {
                            PutMemoryCache(key, bmp);
                            return bmp;
                        }
                    }

                    var portraitPng = Path.Combine(appData, "HearthstoneDeckTracker", "Images", "CardPortraits", $"{id}.png");
                    if (File.Exists(portraitPng))
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

            // 3. Вырезаем портрет из полной карты (CardImages или assets/cards/full)
            // Истинный центр овала портрета в картах Hearthstone: X = 0.50 * W, Y = 0.320 * H
            // Радиусы овала портрета: Rx = 0.235 * W, Ry = 0.215 * H
            var fullCandidates = new List<string>();
            if (!string.IsNullOrEmpty(effectiveCardId))
            {
                fullCandidates.Add(Path.Combine(appData, "HearthstoneDeckTracker", "Images", "CardImages", $"{effectiveCardId}.png"));
                if (cleanCardId != effectiveCardId)
                {
                    fullCandidates.Add(Path.Combine(appData, "HearthstoneDeckTracker", "Images", "CardImages", $"{cleanCardId}.png"));
                }
                fullCandidates.Add(Path.Combine(_assemblyDirectory, "assets", "cards", "full", $"{cleanCardId}.png"));
                fullCandidates.Add(Path.Combine(appData, "HearthstoneDeckTracker", "Plugins", "HSCompsPlugin", "assets", "cards", "full", $"{cleanCardId}.png"));
            }
            if (dbfId > 0)
            {
                fullCandidates.Add(Path.Combine(_assemblyDirectory, "assets", "cards", "full", $"{dbfId}.png"));
                fullCandidates.Add(Path.Combine(appData, "HearthstoneDeckTracker", "Plugins", "HSCompsPlugin", "assets", "cards", "full", $"{dbfId}.png"));
            }

            foreach (var p in fullCandidates)
            {
                if (File.Exists(p))
                {
                    var fullBmp = LoadBitmapFromFile(p, decodePixelWidth: 360);
                    if (fullBmp != null && fullBmp.PixelWidth > 100 && fullBmp.PixelHeight > 100)
                    {
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
                            PutMemoryCache(key, cropped);
                            return cropped;
                        }
                        catch
                        {
                            PutMemoryCache(key, fullBmp);
                            return fullBmp;
                        }
                    }
                }
            }

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

        public BitmapSource GetFrameBitmap(string frameName)
        {
            if (string.IsNullOrEmpty(frameName)) return null;
            string key = $"frame_{frameName}";
            if (_memoryCache.TryGetValue(key, out var cached))
                return cached;

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string fileName = frameName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? frameName : $"{frameName}.png";

            var candidates = new[]
            {
                Path.Combine(_assemblyDirectory, "assets", "ui", "frames", fileName),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "ui", "frames", fileName),
                Path.Combine(appData, "HearthstoneDeckTracker", "Plugins", "HSCompsPlugin", "assets", "ui", "frames", fileName),
                Path.Combine(_assemblyDirectory, "assets", "ui", fileName),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "ui", fileName),
                Path.Combine(appData, "HearthstoneDeckTracker", "Plugins", "HSCompsPlugin", "assets", "ui", fileName)
            };

            foreach (var p in candidates)
            {
                if (File.Exists(p))
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

        public BitmapSource GetMaskBitmap(string frameName)
        {
            if (string.IsNullOrEmpty(frameName)) return null;
            string key = $"mask_{frameName}";
            if (_memoryCache.TryGetValue(key, out var cached))
                return cached;

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string fileName = frameName.StartsWith("mask_", StringComparison.OrdinalIgnoreCase)
                ? frameName
                : $"mask_{frameName}";
            if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                fileName += ".png";

            var candidates = new[]
            {
                Path.Combine(_assemblyDirectory, "assets", "ui", "frames", fileName),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "ui", "frames", fileName),
                Path.Combine(appData, "HearthstoneDeckTracker", "Plugins", "HSCompsPlugin", "assets", "ui", "frames", fileName),
                Path.Combine(_assemblyDirectory, "assets", "ui", fileName),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "ui", fileName),
                Path.Combine(appData, "HearthstoneDeckTracker", "Plugins", "HSCompsPlugin", "assets", "ui", fileName)
            };

            foreach (var p in candidates)
            {
                if (File.Exists(p))
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

        public BitmapSource GetFramedMinionBitmap(int dbfId, string cardId, string frameName, SlotStatus status = SlotStatus.NeedToFind, string remoteUrl = null)
        {
            string effectiveCardId = cardId;
            if (string.IsNullOrEmpty(effectiveCardId) || !effectiveCardId.StartsWith("BG", StringComparison.OrdinalIgnoreCase))
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

            // Точное масштабирование арта: покрывает всю высоту овала (с микро-припуском 2% против швов)
            // и центрируется строго по геометрическому центру овала (Cx, Cy).
            double targetH = def.Ry * 2.0 * def.Zoom;
            double scale = targetH / artBmp.PixelHeight;
            double drawW = artBmp.PixelWidth * scale;
            double drawH = artBmp.PixelHeight * scale;

            // Если арт по ширине меньше овала (например, вертикальная вырезка), масштабируем по ширине:
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
                // Идеально гладкий математический векторный клип овала WPF
                dc.PushClip(new EllipseGeometry(new Point(def.Cx, def.Cy), def.Rx, def.Ry));
                dc.DrawImage(artBmp, new Rect(drawX, drawY, drawW, drawH));
                dc.Pop();

                // Отрисовка рамки поверх арта (перекрывает края овала и нижний прицел)
                dc.DrawImage(frameBmp, new Rect(0, 0, fw, fh));
            }

            var rtb = new RenderTargetBitmap(fw, fh, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();

            PutMemoryCache(key, rtb);
            return rtb;
        }

        public BitmapSource GetImage(string url, int decodePixelWidth = 0)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            string key = decodePixelWidth > 0 ? $"{url}_{decodePixelWidth}" : url;
            if (_memoryCache.TryGetValue(key, out var cachedImage))
                return cachedImage;

            string localFilePath = GetLocalFilePath(url);

            if (File.Exists(localFilePath))
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

            TriggerBackgroundDownload(url, localFilePath);
            return null;
        }

        private void TriggerBackgroundDownload(string url, string localFilePath)
        {
            if (_activeDownloads.TryAdd(url, true))
            {
                Task.Run(async () =>
                {
                    try
                    {
                        var response = await _httpClient.GetAsync(url).ConfigureAwait(false);
                        if (response.IsSuccessStatusCode)
                        {
                            var data = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                            string dir = Path.GetDirectoryName(localFilePath);
                            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            {
                                Directory.CreateDirectory(dir);
                            }

                            File.WriteAllBytes(localFilePath, data);
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
    }
}
