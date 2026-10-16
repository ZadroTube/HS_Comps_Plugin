using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HSCompsPlugin.Models;
using Newtonsoft.Json;

namespace HSCompsPlugin.Services
{
    public class HeroMetaEntry
    {
        [JsonProperty("CardId")]
        public string CardId { get; set; }

        [JsonProperty("DbfId")]
        public int DbfId { get; set; }

        [JsonProperty("NameRu")]
        public string NameRu { get; set; }

        [JsonProperty("Tier")]
        public string Tier { get; set; }

        [JsonProperty("PickRate")]
        public int PickRate { get; set; }

        [JsonProperty("WinRate")]
        public double WinRate { get; set; }

        [JsonProperty("AvgPlacement")]
        public double AvgPlacement { get; set; }

        [JsonProperty("TribeWeights")]
        public Dictionary<string, double> TribeWeights { get; set; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    }

    public class TrinketMetaEntry
    {
        [JsonProperty("CardId")]
        public string CardId { get; set; }

        [JsonProperty("DbfId")]
        public int DbfId { get; set; }

        [JsonProperty("NameRu")]
        public string NameRu { get; set; }

        [JsonProperty("TribeRu")]
        public string TribeRu { get; set; }

        [JsonProperty("Tier")]
        public string Tier { get; set; }

        [JsonProperty("PickRate")]
        public int PickRate { get; set; }

        [JsonProperty("WinRate")]
        public double WinRate { get; set; }

        [JsonProperty("AvgPlacement")]
        public double AvgPlacement { get; set; }
    }

    public class MetaStatsDatabaseContainer
    {
        [JsonProperty("version")]
        public int Version { get; set; }

        [JsonProperty("heroesCount")]
        public int HeroesCount { get; set; }

        [JsonProperty("trinketsCount")]
        public int TrinketsCount { get; set; }

        [JsonProperty("heroes")]
        public Dictionary<string, HeroMetaEntry> Heroes { get; set; } = new Dictionary<string, HeroMetaEntry>(StringComparer.OrdinalIgnoreCase);

        [JsonProperty("trinkets")]
        public Dictionary<string, TrinketMetaEntry> Trinkets { get; set; } = new Dictionary<string, TrinketMetaEntry>(StringComparer.OrdinalIgnoreCase);
    }

    public class EvaluatedSelectionScore
    {
        public string CardId { get; set; }
        public int DbfId { get; set; }
        public string NameRu { get; set; }
        public string Tier { get; set; } = "B";
        public int PickRate { get; set; } = 45;
        public double WinRate { get; set; } = 54.0;
        public double AvgPlacement { get; set; } = 4.25;
        public bool HasSynergy { get; set; }
        public bool IsBestChoice { get; set; }
        public string SynergyNote { get; set; }
        public int SlotIndex { get; set; }
    }

    public class MetaStatsService
    {
        private static MetaStatsService _instance;
        public static MetaStatsService Instance => _instance ?? (_instance = new MetaStatsService());

        private readonly object _loadLock = new object();
        private volatile Dictionary<string, HeroMetaEntry> _heroesByCardId = new Dictionary<string, HeroMetaEntry>(StringComparer.OrdinalIgnoreCase);
        private volatile Dictionary<int, HeroMetaEntry> _heroesByDbfId = new Dictionary<int, HeroMetaEntry>();
        private volatile Dictionary<string, TrinketMetaEntry> _trinketsByCardId = new Dictionary<string, TrinketMetaEntry>(StringComparer.OrdinalIgnoreCase);
        private volatile Dictionary<int, TrinketMetaEntry> _trinketsByDbfId = new Dictionary<int, TrinketMetaEntry>();

        private static readonly Regex GreaterTrinketSuffixRegex = new Regex(@"(?:t\d*|_G)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Каноническая таблица истинных расовых синергий героев Battlegrounds (исключает любые ложные привязки из случайных выборок)
        private static readonly Dictionary<string, Dictionary<string, double>> CanonicalHeroTribes =
            new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase)
            {
                // 1. Узкопрофильные расовые герои (100% привязка к расе)
                ["TB_BaconShop_HERO_18"] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Пират"] = 1.0 },
                ["BG26_HERO_101"]        = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Пират"] = 1.0 },
                ["TB_BaconShop_HERO_53"] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Дракон"] = 1.0 },
                ["TB_BaconShop_HERO_56"] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Дракон"] = 1.0 },
                ["TB_BaconShop_HERO_17"] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Механизм"] = 1.0 },
                ["BG22_HERO_200"]        = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Механизм"] = 1.0 },
                ["BG31_HERO_801"]        = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Механизм"] = 1.0 },
                ["BG31_HERO_802"]        = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Механизм"] = 1.0 },
                ["TB_BaconShop_HERO_55"] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Мурлок"] = 1.0 },
                ["TB_BaconShop_HERO_78"] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Элементаль"] = 1.0 },
                ["BG20_HERO_103"]        = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Свинобраз"] = 1.0 },
                ["TB_BaconShop_HERO_37"] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Демон"] = 1.0 },
                ["TB_BaconShop_HERO_25"] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Демон"] = 1.0 },
                ["BG25_HERO_100"]        = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Нежить"] = 1.0 },
                ["TB_BaconShop_HERO_702"]= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Нежить"] = 1.0 },
                ["BG22_HERO_007"]        = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Нага"] = 1.0 },
                ["BG23_HERO_304"]        = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Нага"] = 1.0 },

                // 2. Двухрасовые / механические профильные герои
                ["TB_BaconShop_HERO_93"] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Зверь"] = 0.5, ["Нежить"] = 0.5 },
                ["TB_BaconShop_HERO_95"] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Зверь"] = 0.5, ["Нежить"] = 0.5 },
                ["BG22_HERO_305"]        = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Зверь"] = 0.5, ["Нежить"] = 0.5 },
                ["BG25_HERO_103"]        = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Зверь"] = 0.5, ["Нежить"] = 0.5 },
                ["TB_BaconShop_HERO_22"] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Нежить"] = 0.5, ["Зверь"] = 0.5 },
                ["BG23_HERO_201"]        = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Зверь"] = 0.5, ["Нежить"] = 0.5 },
                ["TB_BaconShop_HERO_52"] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Нежить"] = 0.5, ["Зверь"] = 0.5 },
                ["BG20_HERO_282"]        = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Нежить"] = 0.5, ["Дракон"] = 0.5 },
                ["BG21_HERO_030"]        = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Механизм"] = 0.5, ["Мурлок"] = 0.5 },
                ["TB_BaconShop_HERO_23"] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Мурлок"] = 0.5, ["Элементаль"] = 0.5 },
                ["TB_BaconShop_HERO_43"] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Мурлок"] = 0.5, ["Дракон"] = 0.5 },
                ["TB_BaconShop_HERO_10"] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Пират"] = 0.5, ["Элементаль"] = 0.5 },
                ["TB_BaconShop_HERO_49"] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Элементаль"] = 0.5, ["Пират"] = 0.5 },
                ["TB_BaconShop_HERO_01"] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Пират"] = 0.34, ["Элементаль"] = 0.33, ["Мурлок"] = 0.33 },
                ["BG20_HERO_102"]        = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Демон"] = 0.34, ["Механизм"] = 0.33, ["Элементаль"] = 0.33 },

                // 3. Герои Зверинца (мультирасовые)
                ["TB_BaconShop_HERO_14"] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Зверинец"] = 1.0 },
                ["TB_BaconShop_HERO_33"] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Зверинец"] = 1.0 }
            };

        public volatile bool IsLoaded;

        public void ReloadDatabase(string jsonPath = null)
        {
            lock (_loadLock)
            {
                IsLoaded = false;
                LoadDatabase(jsonPath);
            }
        }

        public void LoadDatabase(string jsonPath = null)
        {
            if (IsLoaded) return;
            lock (_loadLock)
            {
                if (IsLoaded) return;

                try
                {
                    if (string.IsNullOrEmpty(jsonPath))
                    {
                        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                        string assemblyDir = Path.GetDirectoryName(typeof(MetaStatsService).Assembly.Location) ?? "";
                        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

                        string[] candidates = new[]
                        {
                            Path.Combine(assemblyDir, "meta_stats_db.json"),
                            Path.Combine(appData, "HearthstoneDeckTracker", "Plugins", "HSCompsPlugin", "meta_stats_db.json"),
                            Path.Combine(baseDir, "meta_stats_db.json"),
                            Path.Combine(baseDir, "Plugins", "HSCompsPlugin", "meta_stats_db.json"),
                            @"E:\Coding\Projects\HSCompsPlugin\meta_stats_db.json"
                        };

                        jsonPath = candidates.FirstOrDefault(File.Exists);
                    }

                    if (!string.IsNullOrEmpty(jsonPath) && File.Exists(jsonPath))
                    {
                        string content = File.ReadAllText(jsonPath, Encoding.UTF8);
                        var container = JsonConvert.DeserializeObject<MetaStatsDatabaseContainer>(content);
                        if (container != null)
                        {
                            var hByCard = new Dictionary<string, HeroMetaEntry>(StringComparer.OrdinalIgnoreCase);
                            var hByDbf = new Dictionary<int, HeroMetaEntry>();
                            if (container.Heroes != null)
                            {
                                foreach (var kvp in container.Heroes)
                                {
                                    if (kvp.Value == null) continue;
                                    string key = !string.IsNullOrEmpty(kvp.Value.CardId) ? kvp.Value.CardId : kvp.Key;
                                    hByCard[key] = kvp.Value;
                                    if (kvp.Value.DbfId > 0) hByDbf[kvp.Value.DbfId] = kvp.Value;
                                }
                            }

                            var tByCard = new Dictionary<string, TrinketMetaEntry>(StringComparer.OrdinalIgnoreCase);
                            var tByDbf = new Dictionary<int, TrinketMetaEntry>();
                            if (container.Trinkets != null)
                            {
                                foreach (var kvp in container.Trinkets)
                                {
                                    if (kvp.Value == null) continue;
                                    string key = !string.IsNullOrEmpty(kvp.Value.CardId) ? kvp.Value.CardId : kvp.Key;
                                    if (key.IndexOf("MagicItem", StringComparison.OrdinalIgnoreCase) < 0 &&
                                        key.IndexOf("Trinket", StringComparison.OrdinalIgnoreCase) < 0)
                                        continue;
                                    tByCard[key] = kvp.Value;
                                    if (kvp.Value.DbfId > 0) tByDbf[kvp.Value.DbfId] = kvp.Value;
                                }
                            }

                            _heroesByCardId = hByCard;
                            _heroesByDbfId = hByDbf;
                            _trinketsByCardId = tByCard;
                            _trinketsByDbfId = tByDbf;
                            IsLoaded = true;
                        }
                    }
                }
                catch { }
            }
        }

        public static string NormalizeHeroCardId(string cardId)
        {
            if (string.IsNullOrWhiteSpace(cardId)) return string.Empty;
            string clean = cardId.Trim();
            int skinIdx = clean.IndexOf("_SKIN_", StringComparison.OrdinalIgnoreCase);
            if (skinIdx > 0)
            {
                clean = clean.Substring(0, skinIdx);
            }
            if (clean.EndsWith("_G", StringComparison.OrdinalIgnoreCase))
            {
                clean = clean.Substring(0, clean.Length - 2);
            }
            return clean;
        }

        public static string NormalizeTrinketCardId(string cardId)
        {
            if (string.IsNullOrWhiteSpace(cardId)) return string.Empty;
            string clean = cardId.Trim();
            return GreaterTrinketSuffixRegex.Replace(clean, "");
        }

        private static string FormatTribeDisplayName(string tribe)
        {
            if (string.IsNullOrWhiteSpace(tribe)) return string.Empty;
            string trimmed = tribe.Trim();
            if (string.Equals(trimmed, "Абберация", StringComparison.OrdinalIgnoreCase))
                return "Нага";
            return trimmed;
        }

        private static bool IsTribeInLobby(string targetTribe, IList<string> lobbyTribes)
        {
            if (string.IsNullOrWhiteSpace(targetTribe) || lobbyTribes == null || lobbyTribes.Count == 0)
                return false;

            if (lobbyTribes.Any(t => string.Equals(t, targetTribe, StringComparison.OrdinalIgnoreCase)))
                return true;

            // Совместимость наименований Нага <-> Абберация
            if (string.Equals(targetTribe, "Нага", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(targetTribe, "Абберация", StringComparison.OrdinalIgnoreCase))
            {
                return lobbyTribes.Any(t =>
                    string.Equals(t, "Нага", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(t, "Абберация", StringComparison.OrdinalIgnoreCase));
            }

            return false;
        }

        public bool IsHero(string cardId, int dbfId = 0)
        {
            EnsureLoaded();
            if (dbfId > 0 && _heroesByDbfId.ContainsKey(dbfId)) return true;
            string norm = NormalizeHeroCardId(cardId);
            if (!string.IsNullOrEmpty(norm) && _heroesByCardId.ContainsKey(norm)) return true;
            return GameStateReader.IsHeroCardId(cardId, dbfId);
        }

        public bool IsTrinket(string cardId, int dbfId = 0)
        {
            EnsureLoaded();
            if (dbfId > 0 && _trinketsByDbfId.ContainsKey(dbfId)) return true;
            if (!string.IsNullOrEmpty(cardId))
            {
                if (_trinketsByCardId.ContainsKey(cardId)) return true;
                string baseId = NormalizeTrinketCardId(cardId);
                if (!string.IsNullOrEmpty(baseId) && _trinketsByCardId.ContainsKey(baseId)) return true;
            }
            return GameStateReader.IsTrinketCardId(cardId, dbfId);
        }

        private void EnsureLoaded()
        {
            if (!IsLoaded) LoadDatabase();
        }

        private static string ComputeTierFromWinRate(double winRate)
        {
            if (winRate >= 61.0) return "S";
            if (winRate >= 56.8) return "A";
            if (winRate >= 52.0) return "B";
            if (winRate >= 47.2) return "C";
            return "D";
        }

        public EvaluatedSelectionScore EvaluateHero(string cardId, int dbfId, IEnumerable<string> availableTribes, int slotIndex = 0)
        {
            EnsureLoaded();
            string normId = NormalizeHeroCardId(cardId);

            HeroMetaEntry entry = null;
            if (!string.IsNullOrEmpty(normId)) _heroesByCardId.TryGetValue(normId, out entry);
            if (entry == null && dbfId > 0) _heroesByDbfId.TryGetValue(dbfId, out entry);

            if (entry == null)
            {
                var off = CardDatabaseService.Instance.GetCard(dbfId, normId);
                return new EvaluatedSelectionScore
                {
                    CardId = cardId,
                    DbfId = dbfId > 0 ? dbfId : (off?.Id ?? 0),
                    NameRu = off?.NameRu ?? normId,
                    Tier = "B",
                    PickRate = 46,
                    WinRate = 54.5,
                    AvgPlacement = 4.28,
                    SlotIndex = slotIndex
                };
            }

            string heroCardKey = !string.IsNullOrEmpty(entry.CardId) ? NormalizeHeroCardId(entry.CardId) : normId;
            double winRate = entry.WinRate;
            int pickRate = entry.PickRate;
            double avgPlace = entry.AvgPlacement;
            string tier = entry.Tier ?? ComputeTierFromWinRate(winRate);
            bool hasSynergy = false;
            string synergyNote = null;

            // Используем строго каноническую таблицу синергий героев (игнорируя шумные веса из старых JSON)
            if (CanonicalHeroTribes.TryGetValue(heroCardKey, out var canonicalWeights) && canonicalWeights != null && canonicalWeights.Count > 0)
            {
                if (canonicalWeights.ContainsKey("Зверинец"))
                {
                    hasSynergy = true;
                    synergyNote = "Синергия: Зверинец";
                    winRate = Math.Max(35.0, Math.Min(73.5, Math.Round(winRate + 1.5, 1)));
                    pickRate = Math.Max(6, Math.Min(96, pickRate + 4));
                }
                else if (canonicalWeights.Count == 1)
                {
                    var singleKvp = canonicalWeights.First();
                    string singleTribe = FormatTribeDisplayName(singleKvp.Key);
                    var tribesList = availableTribes?.Where(t => !string.IsNullOrEmpty(t)).ToList();

                    // Узкорасовый герой предлагается сервером Blizzard только тогда, когда его раса активна в лобби
                    bool confirmedAbsent = tribesList != null && tribesList.Count >= 5 && !IsTribeInLobby(singleKvp.Key, tribesList);
                    if (!confirmedAbsent)
                    {
                        hasSynergy = true;
                        synergyNote = $"Синергия: {singleTribe}";
                        winRate = Math.Max(35.0, Math.Min(73.5, Math.Round(winRate + 4.8, 1)));
                        pickRate = Math.Max(6, Math.Min(96, pickRate + 10));
                    }
                    else
                    {
                        winRate = Math.Max(35.0, Math.Min(73.5, Math.Round(winRate - 5.0, 1)));
                        pickRate = Math.Max(6, Math.Min(96, pickRate - 10));
                    }
                }
                else
                {
                    var tribesList = availableTribes?.Where(t => !string.IsNullOrEmpty(t)).ToList();
                    if (tribesList != null && tribesList.Count >= 3)
                    {
                        double lobbySum = 0.0;
                        var matchedTribes = new List<string>();

                        foreach (var kvp in canonicalWeights.OrderByDescending(k => k.Value))
                        {
                            if (IsTribeInLobby(kvp.Key, tribesList))
                            {
                                lobbySum += kvp.Value;
                                string disp = FormatTribeDisplayName(kvp.Key);
                                if (!matchedTribes.Contains(disp, StringComparer.OrdinalIgnoreCase))
                                    matchedTribes.Add(disp);
                            }
                        }

                        if (matchedTribes.Count > 0)
                        {
                            hasSynergy = true;
                            synergyNote = $"Синергия: {string.Join(" / ", matchedTribes.Take(2))}";
                            double bonusFactor = Math.Max(0.15, lobbySum - 0.25);
                            winRate = Math.Max(35.0, Math.Min(73.5, Math.Round(winRate + bonusFactor * 7.5, 1)));
                            pickRate = Math.Max(6, Math.Min(96, pickRate + (int)Math.Round(bonusFactor * 16.0)));
                        }
                        else
                        {
                            winRate = Math.Max(35.0, Math.Min(73.5, Math.Round(winRate - 3.5, 1)));
                            pickRate = Math.Max(6, Math.Min(96, pickRate - 7));
                        }
                    }
                    else
                    {
                        var topTribes = canonicalWeights.Keys.Select(FormatTribeDisplayName).Distinct().Take(2).ToList();
                        if (topTribes.Count > 0)
                        {
                            hasSynergy = true;
                            synergyNote = $"Синергия: {string.Join(" / ", topTribes)}";
                        }
                    }
                }

                avgPlace = Math.Max(2.65, Math.Min(6.20, Math.Round(8.0 - (winRate / 100.0) * 6.8, 2)));
                tier = ComputeTierFromWinRate(winRate);
            }

            return new EvaluatedSelectionScore
            {
                CardId = cardId,
                DbfId = entry.DbfId > 0 ? entry.DbfId : dbfId,
                NameRu = entry.NameRu ?? normId,
                Tier = tier,
                PickRate = pickRate,
                WinRate = winRate,
                AvgPlacement = avgPlace,
                HasSynergy = hasSynergy,
                SynergyNote = synergyNote,
                SlotIndex = slotIndex
            };
        }

        public EvaluatedSelectionScore EvaluateTrinket(string cardId, int dbfId, PlayerGameStateSnapshot liveState, IEnumerable<CompRecommendation> activeRecs, int slotIndex = 0)
        {
            EnsureLoaded();
            string cleanId = !string.IsNullOrEmpty(cardId) ? cardId.Trim() : string.Empty;
            string baseId = NormalizeTrinketCardId(cleanId);

            TrinketMetaEntry entry = null;
            if (!string.IsNullOrEmpty(cleanId)) _trinketsByCardId.TryGetValue(cleanId, out entry);
            if (entry == null && dbfId > 0) _trinketsByDbfId.TryGetValue(dbfId, out entry);
            if (entry == null && !string.IsNullOrEmpty(baseId)) _trinketsByCardId.TryGetValue(baseId, out entry);

            if (entry == null)
            {
                var off = CardDatabaseService.Instance.GetCard(dbfId, cleanId) ?? CardDatabaseService.Instance.GetCard(0, baseId);
                return new EvaluatedSelectionScore
                {
                    CardId = cleanId,
                    DbfId = dbfId > 0 ? dbfId : (off?.Id ?? 0),
                    NameRu = off?.NameRu ?? cleanId,
                    Tier = "B",
                    PickRate = 38,
                    WinRate = 54.0,
                    AvgPlacement = 4.32,
                    SlotIndex = slotIndex
                };
            }

            double winRate = entry.WinRate;
            int pickRate = entry.PickRate;
            string tier = entry.Tier ?? ComputeTierFromWinRate(winRate);
            bool hasSynergy = false;
            string synergyNote = null;

            // 1. Подсчет существ на текущем столе и в руке игрока по племенам
            var boardTribeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (liveState != null)
            {
                var allMinions = new List<BoardSlot>();
                if (liveState.BoardMinions != null) allMinions.AddRange(liveState.BoardMinions);
                if (liveState.HandMinions != null) allMinions.AddRange(liveState.HandMinions);

                foreach (var m in allMinions)
                {
                    string t = m?.TribeRu;
                    if (string.IsNullOrEmpty(t) || t == "Существо" || t == "Без типа") continue;
                    if (string.Equals(t, "Все", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var k in boardTribeCounts.Keys.ToList())
                            boardTribeCounts[k] += 1;
                        continue;
                    }
                    foreach (var part in t.Split(new[] { '/', ',' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string tr = FormatTribeDisplayName(part.Trim());
                        if (!string.IsNullOrEmpty(tr))
                        {
                            boardTribeCounts[tr] = boardTribeCounts.TryGetValue(tr, out int c) ? c + 1 : 1;
                        }
                    }
                }
            }

            int matchingBoardMinions = 0;
            bool isTribeSpecific = !string.IsNullOrEmpty(entry.TribeRu) &&
                                   !string.Equals(entry.TribeRu, "Все", StringComparison.OrdinalIgnoreCase) &&
                                   !string.Equals(entry.TribeRu, "Без типа", StringComparison.OrdinalIgnoreCase);

            var trinketTribes = new List<string>();
            if (isTribeSpecific)
            {
                foreach (var part in entry.TribeRu.Split(new[] { '/', ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string tr = FormatTribeDisplayName(part.Trim());
                    if (!string.IsNullOrEmpty(tr))
                    {
                        trinketTribes.Add(tr);
                        if (boardTribeCounts.TryGetValue(tr, out int count) && count > matchingBoardMinions)
                        {
                            matchingBoardMinions = count;
                        }
                    }
                }
            }

            // 2. Проверка прямой синергии с рекомендованными сборками (с защитой от непрофильного статического бенчмарка)
            bool matchesActiveComp = false;
            string matchedCompTribe = null;
            if (activeRecs != null)
            {
                foreach (var rec in activeRecs)
                {
                    if (rec?.Board == null) continue;
                    bool inCompTrinkets =
                        (rec.Board.TrinketSet != null && (rec.Board.TrinketSet.Contains(cleanId) || (!string.IsNullOrEmpty(baseId) && rec.Board.TrinketSet.Contains(baseId)))) ||
                        (rec.Board.Trinkets != null && (rec.Board.Trinkets.Contains(cleanId, StringComparer.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(baseId) && rec.Board.Trinkets.Contains(baseId, StringComparer.OrdinalIgnoreCase))));

                    if (inCompTrinkets)
                    {
                        string compTribe = FormatTribeDisplayName(rec.Board.PrimaryTribe);
                        bool playerHasCompTribe = !isTribeSpecific || rec.IsLocked || matchingBoardMinions >= 2 || boardTribeCounts.Count == 0;
                        if (playerHasCompTribe)
                        {
                            matchesActiveComp = true;
                            matchedCompTribe = compTribe;
                            break;
                        }
                    }
                }
            }

            if (matchesActiveComp)
            {
                winRate += 4.2;
                pickRate += 11;
                hasSynergy = true;
                synergyNote = !string.IsNullOrEmpty(matchedCompTribe) && matchedCompTribe != "Все"
                    ? $"Ядро: {matchedCompTribe}"
                    : "Ядро вашей сборки";
            }
            else if (matchingBoardMinions >= 2)
            {
                double bonus = Math.Min(3.8, matchingBoardMinions * 1.15);
                winRate += bonus;
                pickRate += Math.Min(10, matchingBoardMinions * 3);
                hasSynergy = true;
                string dispTribe = string.Join(" / ", trinketTribes.Take(2));
                synergyNote = $"Под ваш стол ({dispTribe})";
            }
            else if (isTribeSpecific && matchingBoardMinions == 0 && liveState != null && liveState.Turn >= 6 && boardTribeCounts.Count > 0)
            {
                // Если тринкет узкорасовый, а у игрока на 6+ ходу нет существ этой расы — снижаем приоритет
                winRate -= 3.2;
                pickRate -= 8;
            }

            winRate = Math.Max(36.0, Math.Min(73.5, Math.Round(winRate, 1)));
            pickRate = Math.Max(5, Math.Min(95, pickRate));
            double avgPlace = Math.Max(2.65, Math.Min(6.20, Math.Round(8.0 - (winRate / 100.0) * 6.8, 2)));
            tier = ComputeTierFromWinRate(winRate);

            return new EvaluatedSelectionScore
            {
                CardId = cleanId,
                DbfId = entry.DbfId > 0 ? entry.DbfId : dbfId,
                NameRu = entry.NameRu ?? cleanId,
                Tier = tier,
                PickRate = pickRate,
                WinRate = winRate,
                AvgPlacement = avgPlace,
                HasSynergy = hasSynergy,
                SynergyNote = synergyNote,
                SlotIndex = slotIndex
            };
        }

        public static void MarkBestChoice(IList<EvaluatedSelectionScore> scores)
        {
            if (scores == null || scores.Count == 0) return;
            foreach (var s in scores)
            {
                if (s != null) s.IsBestChoice = false;
            }

            var best = scores
                .Where(s => s != null)
                .OrderByDescending(s => s.WinRate + (s.HasSynergy ? 1.2 : 0.0) + s.PickRate * 0.05)
                .FirstOrDefault();

            if (best != null)
            {
                best.IsBestChoice = true;
            }
        }
    }
}
