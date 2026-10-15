using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HSCompsPlugin.Models;
using Newtonsoft.Json;

namespace HSCompsPlugin.Services
{
    public class VectorDatabaseContainer
    {
        [JsonProperty("version")]
        public int Version { get; set; }

        [JsonProperty("patchBuild")]
        public int PatchBuild { get; set; }

        [JsonProperty("totalArchetypes")]
        public int TotalArchetypes { get; set; }

        [JsonProperty("boards")]
        public List<WinningBoard> Boards { get; set; } = new List<WinningBoard>();
    }

    public class PlayerGameStateSnapshot
    {
        public int Turn { get; set; } = 1;
        public string HeroCardId { get; set; }
        public int TavernTier { get; set; } = 1;
        public int HeroHealth { get; set; } = 40;
        public List<string> AvailableTribes { get; set; } = new List<string>();
        public List<string> BannedTribes { get; set; } = new List<string>();

        public List<BoardSlot> BoardMinions { get; set; } = new List<BoardSlot>();
        public List<BoardSlot> HandMinions { get; set; } = new List<BoardSlot>();
        public List<string> ActiveTrinkets { get; set; } = new List<string>();

        public string LockedArchetypeId { get; set; }
    }

    public class VectorSearchEngine
    {
        private static VectorSearchEngine _instance;
        public static VectorSearchEngine Instance => _instance ?? (_instance = new VectorSearchEngine());

        private volatile List<WinningBoard> _canonicalBoards = new List<WinningBoard>();
        private volatile Dictionary<int, int> _cardArchetypeCounts = new Dictionary<int, int>();
        private volatile int _totalDistinctArchetypes = 20;
        private readonly object _loadLock = new object();
        public volatile bool IsLoaded;
        public int BoardsCount => _canonicalBoards.Count;
        public IReadOnlyList<WinningBoard> AllBoards => _canonicalBoards;

        public static readonly string[] AllCanonicalTribes = new[]
        {
            "Механизм", "Зверь", "Мурлок", "Нежить", "Демон",
            "Элементаль", "Пират", "Дракон", "Свинобраз", "Абберация"
        };

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

            if (string.IsNullOrEmpty(jsonPath))
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string assemblyDir = Path.GetDirectoryName(typeof(VectorSearchEngine).Assembly.Location) ?? "";

                string[] candidatePaths = new[]
                {
                    Path.Combine(assemblyDir, "comps_vector_db.json"),
                    Path.Combine(baseDir, "comps_vector_db.json"),
                    Path.Combine(baseDir, "Plugins", "HSCompsPlugin", "comps_vector_db.json"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HearthstoneDeckTracker", "Plugins", "HSCompsPlugin", "comps_vector_db.json")
                };

                jsonPath = candidatePaths.FirstOrDefault(File.Exists);
            }

            if (string.IsNullOrEmpty(jsonPath) || !File.Exists(jsonPath))
            {
                throw new FileNotFoundException($"Файл векторной базы столов не найден. Путь: {jsonPath}");
            }

            string jsonContent = File.ReadAllText(jsonPath, Encoding.UTF8);
            var container = JsonConvert.DeserializeObject<VectorDatabaseContainer>(jsonContent);

            if (container?.Boards == null)
            {
                throw new InvalidDataException("Не удалось распарсить базу comps_vector_db.json");
            }

            var loadedBoards = new List<WinningBoard>(container.Boards.Count);

            var cardService = CardDatabaseService.Instance;

            foreach (var board in container.Boards)
            {
                var minionDbfIds = new List<int>(board.Minions.Count);
                TribeFlags minionTribesMask = TribeFlags.None;

                foreach (var minion in board.Minions)
                {
                    var (officialCard, isGolden) = cardService.ResolveCard(minion.CardId, minion.DbfId);
                    minion.Card = officialCard;
                    minion.IsGolden = isGolden || minion.IsGolden;
                    int dbf = minion.DbfId > 0 ? minion.DbfId : (officialCard?.Id ?? 0);
                    minion.DbfId = dbf;
                    minionDbfIds.Add(dbf);

                    if (officialCard != null)
                    {
                        minionTribesMask |= officialCard.TribeMask;
                    }
                }

                board.PrimaryTribeFlag = TribeFlagsHelper.StringToFlag(board.PrimaryTribe);
                board.RequiredTribesMask = TribeFlagsHelper.ListToMask(board.RequiredTribes);
                board.MinionTribesMask = minionTribesMask;
                board.MinionDbfIds = minionDbfIds.ToArray();

                // Пре-проверка аномалий и валидности стола (вычисляется 1 раз при загрузке)
                board.IsMalformedBoard = CheckIfBoardMalformed(board);

                loadedBoards.Add(board);
            }

            // Расчет IDF-специфичности карт по архетипам (для отделения ядра сборки от общих тех-карт вроде Лироя/Драккари)
            var archSetsByDbf = new Dictionary<int, HashSet<string>>();
            var allArchs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in loadedBoards)
            {
                if (b.IsMalformedBoard || string.IsNullOrEmpty(b.Archetype)) continue;
                allArchs.Add(b.Archetype);
                if (b.Minions == null) continue;
                foreach (var m in b.Minions)
                {
                    int dbf = m.DbfId > 0 ? m.DbfId : (m.Card?.Id ?? 0);
                    if (dbf <= 0) continue;
                    if (!archSetsByDbf.TryGetValue(dbf, out var set))
                    {
                        set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        archSetsByDbf[dbf] = set;
                    }
                    set.Add(b.Archetype);
                }
            }

            var countsMap = new Dictionary<int, int>(archSetsByDbf.Count);
            foreach (var kvp in archSetsByDbf)
            {
                countsMap[kvp.Key] = kvp.Value.Count;
            }

            _totalDistinctArchetypes = Math.Max(1, allArchs.Count);
            _cardArchetypeCounts = countsMap;

            // Предрасчет весов существ, нормализованных героев и тринкетов (1 раз при загрузке базы)
            foreach (var board in loadedBoards)
            {
                board.CanonicalPrimaryTribe = CanonicalizeTribe(board.PrimaryTribe);
                board.NormalizedHeroCardId = NormalizeHeroId(board.HeroCardId);

                int mCount = board.Minions?.Count ?? 0;
                var weights = new float[mCount];
                float maxPossible = 0f;
                for (int i = 0; i < mCount; i++)
                {
                    var m = board.Minions[i];
                    int tier = m.Tier > 0 ? m.Tier : (m.Card?.Tier ?? 4);
                    float specMult = GetMinionSpecificityMultiplier(m, board);
                    float w = (tier >= 5 ? 2.5f : (tier >= 3 ? 1.5f : 1.0f)) * specMult;
                    weights[i] = w;
                    maxPossible += w;
                }
                board.MinionWeights = weights;
                board.NormDivisor = Math.Max(6.5f, maxPossible * 0.85f);

                var tSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                TribeFlags tMask = TribeFlags.None;
                if (board.Trinkets != null)
                {
                    foreach (var trId in board.Trinkets)
                    {
                        if (!string.IsNullOrWhiteSpace(trId))
                        {
                            tSet.Add(trId.Trim());
                            var (trCard, _) = cardService.ResolveCard(trId);
                            if (trCard != null)
                            {
                                tMask |= trCard.TribeMask;
                            }
                        }
                    }
                }
                board.TrinketSet = tSet;
                board.TrinketTribesMask = tMask;
            }

            _canonicalBoards = loadedBoards;
            IsLoaded = true;
            }
        }

        private float GetMinionSpecificityMultiplier(BoardSlot slot, WinningBoard targetBoard)
        {
            if (slot == null) return 1.0f;
            int dbf = slot.DbfId > 0 ? slot.DbfId : (slot.Card?.Id ?? 0);
            var card = slot.Card;
            TribeFlags cardMask = card?.TribeMask ?? TribeFlags.None;
            TribeFlags boardMask = targetBoard?.PrimaryTribeFlag ?? TribeFlags.None;

            int archCount = 1;
            if (dbf > 0 && _cardArchetypeCounts.TryGetValue(dbf, out int cnt))
            {
                archCount = cnt;
            }

            float ubiquityRatio = (float)archCount / Math.Max(1, _totalDistinctArchetypes);

            // Универсальные нейтральные тех-карты (Лирой, Бранн, Драккари, Балинда, Син'дорай), присутствующие в >= 35% всех архетипов
            if (cardMask == TribeFlags.None && ubiquityRatio >= 0.35f)
            {
                return 0.50f;
            }

            // Профильные существа основного племени сборки или узкоспециализированное ядро (1-3 архетипа)
            bool matchesPrimaryTribe = boardMask != TribeFlags.None && boardMask != TribeFlags.All && (cardMask & boardMask) != TribeFlags.None;
            if (matchesPrimaryTribe && archCount <= 4)
            {
                return 1.25f;
            }
            if (archCount <= 2)
            {
                return 1.20f;
            }
            if (ubiquityRatio >= 0.45f)
            {
                return 0.65f;
            }

            return 1.0f;
        }

        private int GetMissingCardPriorityOrder(BoardSlot slot, WinningBoard targetBoard)
        {
            if (slot == null) return 0;
            int dbf = slot.DbfId > 0 ? slot.DbfId : (slot.Card?.Id ?? 0);
            var card = slot.Card;
            TribeFlags cardMask = card?.TribeMask ?? TribeFlags.None;
            TribeFlags boardMask = targetBoard?.PrimaryTribeFlag ?? TribeFlags.None;

            int archCount = 1;
            if (dbf > 0 && _cardArchetypeCounts.TryGetValue(dbf, out int cnt))
            {
                archCount = cnt;
            }
            float ubiquityRatio = (float)archCount / Math.Max(1, _totalDistinctArchetypes);

            // Категория 0 (в самый конец списка поиска): универсальные нейтральные тех-карты (Лирой и т.п.)
            if (cardMask == TribeFlags.None && ubiquityRatio >= 0.35f)
            {
                return 0;
            }

            // Категория 3 (первый приоритет поиска): профильное ядро основного племени сборки
            bool matchesPrimary = boardMask != TribeFlags.None && boardMask != TribeFlags.All && (cardMask & boardMask) != TribeFlags.None;
            if (matchesPrimary)
            {
                return 3;
            }

            // Категория 2: узкоспециализированные карты этого архетипа
            if (archCount <= 4)
            {
                return 2;
            }

            // Категория 1: остальные вспомогательные карты
            return 1;
        }

        private static float CalculateTavernReachabilityBonus(WinningBoard targetBoard, PlayerGameStateSnapshot state)
        {
            if (targetBoard?.Minions == null || state == null) return 0f;
            if (state.Turn < 2 || state.Turn > 9 || state.TavernTier <= 0 || state.TavernTier >= 6) return 0f;

            int maxReachableTier = state.TavernTier + 1;
            int reachableCount = 0;
            int highTierUnreachable = 0;

            foreach (var m in targetBoard.Minions)
            {
                int tier = m.Tier > 0 ? m.Tier : (m.Card?.Tier ?? 4);
                if (tier <= maxReachableTier)
                    reachableCount++;
                else if (tier >= state.TavernTier + 2)
                    highTierUnreachable++;
            }

            // Мягкая корректировка в пределах [-0.03 .. +0.04], чтобы на 3-4 таверне не завышать сборки из пяти карт 6 тира
            float bonus = (reachableCount - 3.5f) * 0.01f - (highTierUnreachable * 0.008f);
            return Math.Max(-0.03f, Math.Min(0.04f, bonus));
        }

        private static float CalculateTrinketSynergyBonus(WinningBoard targetBoard, PlayerGameStateSnapshot state)
        {
            if (targetBoard == null || state?.ActiveTrinkets == null || state.ActiveTrinkets.Count == 0)
                return 0f;

            float bonus = 0f;
            var cardService = CardDatabaseService.Instance;

            foreach (var rawTrinket in state.ActiveTrinkets)
            {
                if (string.IsNullOrWhiteSpace(rawTrinket)) continue;
                string cleanTrinket = rawTrinket.Trim();

                if (targetBoard.TrinketSet != null && targetBoard.TrinketSet.Contains(cleanTrinket))
                {
                    bonus += 0.08f;
                    continue;
                }

                var (trCard, _) = cardService.ResolveCard(cleanTrinket);
                if (trCard != null && trCard.TribeMask != TribeFlags.None && trCard.TribeMask != TribeFlags.All)
                {
                    if (targetBoard.PrimaryTribeFlag != TribeFlags.None &&
                        targetBoard.PrimaryTribeFlag != TribeFlags.All &&
                        (trCard.TribeMask & targetBoard.PrimaryTribeFlag) != TribeFlags.None)
                    {
                        bonus += 0.05f;
                    }
                }
            }

            return Math.Min(0.12f, bonus);
        }

        private static float CalculateSurvivalTempoAdjustment(WinningBoard targetBoard, PlayerGameStateSnapshot state, int ownedCount)
        {
            if (targetBoard?.Minions == null || state == null) return 0f;
            if (state.Turn < 4 || state.HeroHealth <= 0 || state.HeroHealth > 15) return 0f;

            int highTierCount = 0;
            foreach (var m in targetBoard.Minions)
            {
                if (m != null && m.Tier >= 5) highTierCount++;
            }

            float adjustment = 0f;
            bool isCriticalHp = state.HeroHealth <= 10;

            if (ownedCount >= 3)
            {
                adjustment += isCriticalHp ? 0.05f : 0.03f;
            }
            else if (state.TavernTier <= 4 && ownedCount <= 1 && highTierCount >= 4)
            {
                adjustment -= isCriticalHp ? 0.06f : 0.04f;
            }

            if (targetBoard.AveragePlacement > 0 && targetBoard.AveragePlacement <= 2.15)
            {
                adjustment += 0.02f;
            }

            return adjustment;
        }

        private static bool CheckIfBoardMalformed(WinningBoard board)
        {
            if (board.Minions == null || board.Minions.Count < 6) return true;

            // Подсчет дубликатов карт в столе
            var dbfCounts = new Dictionary<int, int>();
            foreach (var minion in board.Minions)
            {
                int id = minion.DbfId > 0 ? minion.DbfId : (minion.Card?.Id ?? 0);
                if (id > 0)
                {
                    dbfCounts[id] = dbfCounts.TryGetValue(id, out int cnt) ? cnt + 1 : 1;
                }
            }

            // Запрет >= 3 одинаковых карт
            if (dbfCounts.Values.Any(c => c >= 3)) return true;

            // Запрет больше 1 пары дубликатов на весь стол
            int pairsCount = dbfCounts.Values.Count(c => c == 2);
            if (pairsCount > 1) return true;

            // Запрет дубликатов существ 1-2 тира
            foreach (var kvp in dbfCounts)
            {
                if (kvp.Value == 2)
                {
                    var mCard = board.Minions.FirstOrDefault(m => (m.DbfId > 0 ? m.DbfId : (m.Card?.Id ?? 0)) == kvp.Key)?.Card;
                    if (mCard != null && mCard.Tier <= 2) return true;
                }
            }

            // Проверка целостности и синергии племенного состава
            string primary = CanonicalizeTribe(board.PrimaryTribe);
            if (primary != "Все")
            {
                int primaryTribeCount = 0;
                int neutralCount = 0;

                foreach (var minion in board.Minions)
                {
                    var card = minion?.Card ?? CardDatabaseService.Instance.GetCard(minion.DbfId, minion.CardId);
                    if (card != null)
                    {
                        if (card.TribesList != null && card.TribesList.Any(t => TribesMatch(t, primary)))
                        {
                            primaryTribeCount++;
                        }
                        else if (string.IsNullOrEmpty(card.TribeRu) || card.TribeRu == "Без типа")
                        {
                            neutralCount++;
                        }
                    }
                }

                if (primaryTribeCount < 3) return true;
                if (neutralCount > 3) return true;
            }

            return false;
        }

        #region Канонизация и сопоставление племен

        public static string CanonicalizeTribe(string tribe)
        {
            if (string.IsNullOrWhiteSpace(tribe)) return "Все";
            string lower = tribe.Trim().ToLowerInvariant();

            if (lower.StartsWith("мех") || lower.Contains("mech")) return "Механизм";
            if (lower.StartsWith("звер") || lower.Contains("beast")) return "Зверь";
            if (lower.StartsWith("мурл") || lower.Contains("murloc")) return "Мурлок";
            if (lower.StartsWith("неж") || lower.Contains("undead")) return "Нежить";
            if (lower.StartsWith("дем") || lower.Contains("demon")) return "Демон";
            if (lower.StartsWith("элем") || lower.Contains("elemental")) return "Элементаль";
            if (lower.StartsWith("наг") || lower.Contains("naga")) return "Нага";
            if (lower.StartsWith("пир") || lower.Contains("pirate")) return "Пират";
            if (lower.StartsWith("драк") || lower.Contains("dragon")) return "Дракон";
            if (lower.StartsWith("свин") || lower.Contains("quilboar") || lower.Contains("boar")) return "Свинобраз";
            if (lower.StartsWith("аббер") || lower.Contains("aberr")) return "Абберация";
            if (lower.StartsWith("все") || lower.StartsWith("all") || lower.Contains("menagerie")) return "Все";

            return "Все";
        }

        public static bool TribesMatch(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            string canA = CanonicalizeTribe(a);
            string canB = CanonicalizeTribe(b);
            return string.Equals(canA, canB, StringComparison.OrdinalIgnoreCase);
        }

        public static string NormalizeHeroId(string heroCardId)
        {
            if (string.IsNullOrWhiteSpace(heroCardId)) return string.Empty;
            string clean = heroCardId.Trim();
            int skinIdx = clean.IndexOf("_SKIN_", StringComparison.OrdinalIgnoreCase);
            if (skinIdx > 0) clean = clean.Substring(0, skinIdx);
            int buddyIdx = clean.IndexOf("_Buddy", StringComparison.OrdinalIgnoreCase);
            if (buddyIdx > 0) clean = clean.Substring(0, buddyIdx);
            return clean;
        }

        public static bool HeroesMatch(string heroA, string heroB)
        {
            if (string.IsNullOrWhiteSpace(heroA) || string.IsNullOrWhiteSpace(heroB)) return false;
            string normA = NormalizeHeroId(heroA);
            string normB = NormalizeHeroId(heroB);
            return string.Equals(normA, normB, StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region Сопоставление карт (Card Matching)

        public static int GetCanonicalCardId(BoardSlot slot)
        {
            if (slot == null) return 0;
            if (slot.Card != null && slot.Card.Id > 0) return slot.Card.Id;
            if (slot.DbfId > 0)
            {
                var resolved = CardDatabaseService.Instance.ResolveCard(slot.CardId, slot.DbfId);
                if (resolved.card != null && resolved.card.Id > 0)
                {
                    slot.Card = resolved.card;
                    return resolved.card.Id;
                }
                return slot.DbfId;
            }
            if (!string.IsNullOrEmpty(slot.CardId))
            {
                var resolved = CardDatabaseService.Instance.ResolveCard(slot.CardId);
                if (resolved.card != null && resolved.card.Id > 0)
                {
                    slot.Card = resolved.card;
                    return resolved.card.Id;
                }
            }
            return 0;
        }

        public static bool CardsMatch(BoardSlot target, BoardSlot candidate)
        {
            if (target == null || candidate == null) return false;

            int targetId = GetCanonicalCardId(target);
            int candId = GetCanonicalCardId(candidate);
            if (targetId > 0 && candId > 0 && targetId == candId) return true;

            if (target.DbfId > 0 && candidate.DbfId > 0 && target.DbfId == candidate.DbfId) return true;

            if (!string.IsNullOrEmpty(target.CardId) && !string.IsNullOrEmpty(candidate.CardId))
            {
                string tClean = target.CardId.EndsWith("_G", StringComparison.OrdinalIgnoreCase)
                    ? target.CardId.Substring(0, target.CardId.Length - 2) : target.CardId;
                string cClean = candidate.CardId.EndsWith("_G", StringComparison.OrdinalIgnoreCase)
                    ? candidate.CardId.Substring(0, candidate.CardId.Length - 2) : candidate.CardId;
                if (string.Equals(tClean, cClean, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        #endregion


        #region Строгая валидация доступности в лобби

        public static bool IsCardAvailableInLobby(OfficialCard card, List<string> availableTribes, List<string> bannedTribes)
        {
            if (card == null) return false;
            if (card.TribeMask == TribeFlags.None || card.TribeMask == TribeFlags.All)
                return true;

            if (availableTribes != null && availableTribes.Count >= 3 && availableTribes.Count <= 7)
            {
                TribeFlags availMask = TribeFlagsHelper.ListToMask(availableTribes);
                if (availMask != TribeFlags.None)
                {
                    // Двухплеменное существо доступно в лобби, если присутствует хотя бы одно из его племён
                    return (card.TribeMask & availMask) != TribeFlags.None;
                }
            }

            TribeFlags bannedMask = TribeFlagsHelper.ListToMask(bannedTribes);
            if (bannedMask != TribeFlags.None)
            {
                // Карта недоступна только если ВСЕ её племена находятся в списке забаненных
                return (card.TribeMask & ~bannedMask) != TribeFlags.None;
            }

            return true;
        }

        public static bool IsBoardAllowedInLobby(WinningBoard board, List<string> availableTribes, List<string> bannedTribes)
        {
            if (board == null || board.IsMalformedBoard) return false;
            if (board.Minions == null || board.Minions.Count < 6) return false;

            TribeFlags bannedMask = TribeFlagsHelper.ListToMask(bannedTribes);
            TribeFlags availMask = (availableTribes != null && availableTribes.Count >= 3 && availableTribes.Count <= 7)
                ? TribeFlagsHelper.ListToMask(availableTribes)
                : TribeFlags.None;

            return IsBoardAllowedInLobby(board, availMask, bannedMask);
        }

        public static bool IsBoardAllowedInLobby(WinningBoard board, TribeFlags availableMask, TribeFlags bannedMask)
        {
            if (board == null || board.IsMalformedBoard) return false;
            if (board.Minions == null || board.Minions.Count < 6) return false;

            // 1. Проверка основного племени сборки
            if (board.PrimaryTribeFlag != TribeFlags.All && board.PrimaryTribeFlag != TribeFlags.None)
            {
                if (availableMask != TribeFlags.None && (board.PrimaryTribeFlag & availableMask) == TribeFlags.None)
                    return false;

                if (bannedMask != TribeFlags.None && (board.PrimaryTribeFlag & bannedMask) != TribeFlags.None)
                    return false;
            }

            // 2. Проверка каждого существа с учетом правил двухплеменных карт (достаточно 1 доступного племени)
            if (availableMask != TribeFlags.None)
            {
                foreach (var minion in board.Minions)
                {
                    var card = minion?.Card;
                    if (card != null && card.TribeMask != TribeFlags.None && card.TribeMask != TribeFlags.All)
                    {
                        if ((card.TribeMask & availableMask) == TribeFlags.None)
                            return false;
                    }
                }
            }
            else if (bannedMask != TribeFlags.None)
            {
                foreach (var minion in board.Minions)
                {
                    var card = minion?.Card;
                    if (card != null && card.TribeMask != TribeFlags.None && card.TribeMask != TribeFlags.All)
                    {
                        if ((card.TribeMask & ~bannedMask) == TribeFlags.None)
                            return false;
                    }
                }
            }

            return true;
        }

        #endregion

        #region Однократный бенчмарк героя (Вкладка 1 / Слот 1/3)

        public CompRecommendation FindHeroTop1Benchmark(string heroCardId, List<string> availableTribes, List<string> bannedTribes)
        {
            if (!IsLoaded) LoadDatabase();
            if (_canonicalBoards.Count == 0) return null;

            var allowedBoards = _canonicalBoards.Where(b => IsBoardAllowedInLobby(b, availableTribes, bannedTribes)).ToList();
            if (allowedBoards.Count == 0) allowedBoards = _canonicalBoards.Where(b => b.Minions != null && b.Minions.Count >= 6).ToList();
            if (allowedBoards.Count == 0) allowedBoards = _canonicalBoards;

            WinningBoard bestBoard = null;

            // 1. Поиск лучшего исторического стола для данного Героя
            if (!string.IsNullOrEmpty(heroCardId))
            {
                var heroBoards = allowedBoards.Where(b =>
                    !string.IsNullOrEmpty(b.HeroCardId) && HeroesMatch(b.HeroCardId, heroCardId)
                ).ToList();


                if (heroBoards.Count > 0)
                {
                    bestBoard = heroBoards.OrderByDescending(b => b.Minions != null && b.Minions.Count >= 7)
                                          .ThenBy(b => b.AveragePlacement)
                                          .ThenByDescending(b => b.Top1Rate)
                                          .ThenByDescending(b => b.PlayerRank)
                                          .FirstOrDefault();
                }
            }

            // 2. Если на героя нет столов в базе для этого лобби — берем абсолютный топ лобби
            if (bestBoard == null)
            {
                bestBoard = allowedBoards.OrderByDescending(b => b.Minions != null && b.Minions.Count >= 7)
                                        .ThenBy(b => b.AveragePlacement)
                                        .ThenByDescending(b => b.Top1Rate)
                                        .ThenByDescending(b => b.PlayerRank)
                                        .FirstOrDefault();
            }

            if (bestBoard == null) return null;

            var dummyState = new PlayerGameStateSnapshot
            {
                HeroCardId = heroCardId,
                AvailableTribes = availableTribes ?? new List<string>(),
                BannedTribes = bannedTribes ?? new List<string>()
            };

            return EvaluateBoardMatch(bestBoard, dummyState, new Dictionary<string, int>());
        }

        public Dictionary<string, CompRecommendation> FindHeroTribeBenchmarks(string heroCardId, List<string> availableTribes, List<string> bannedTribes)
        {
            if (!IsLoaded) LoadDatabase();
            var result = new Dictionary<string, CompRecommendation>(StringComparer.OrdinalIgnoreCase);
            if (availableTribes == null || availableTribes.Count == 0) return result;

            var allowedBoards = _canonicalBoards.Where(b => IsBoardAllowedInLobby(b, availableTribes, bannedTribes)).ToList();
            if (allowedBoards.Count == 0) allowedBoards = _canonicalBoards.Where(b => b.Minions != null && b.Minions.Count >= 6).ToList();
            if (allowedBoards.Count == 0) allowedBoards = _canonicalBoards;

            var dummyState = new PlayerGameStateSnapshot
            {
                HeroCardId = heroCardId,
                AvailableTribes = availableTribes,
                BannedTribes = bannedTribes ?? new List<string>()
            };

            foreach (var tribe in availableTribes)
            {
                string canonTribe = CanonicalizeTribe(tribe);
                var tribeBoards = allowedBoards.Where(b => TribesMatch(b.PrimaryTribe, canonTribe)).ToList();
                if (tribeBoards.Count == 0) continue;

                WinningBoard bestTribeBoard = null;

                // Приоритет столам конкретного героя
                if (!string.IsNullOrEmpty(heroCardId))
                {
                    var heroTribeBoards = tribeBoards.Where(b =>
                        !string.IsNullOrEmpty(b.HeroCardId) && HeroesMatch(b.HeroCardId, heroCardId)
                    ).ToList();


                    if (heroTribeBoards.Count > 0)
                    {
                        bestTribeBoard = heroTribeBoards.OrderByDescending(b => b.Minions != null && b.Minions.Count >= 7)
                                                        .ThenBy(b => b.AveragePlacement)
                                                        .ThenByDescending(b => b.Top1Rate)
                                                        .FirstOrDefault();
                    }
                }

                if (bestTribeBoard == null)
                {
                    bestTribeBoard = tribeBoards.OrderByDescending(b => b.Minions != null && b.Minions.Count >= 7)
                                                .ThenBy(b => b.AveragePlacement)
                                                .ThenByDescending(b => b.Top1Rate)
                                                .FirstOrDefault();
                }

                if (bestTribeBoard != null)
                {
                    var rec = EvaluateBoardMatch(bestTribeBoard, dummyState, new Dictionary<string, int>());
                    result[canonTribe] = rec;
                }
            }

            return result;
        }

        #endregion

        #region Динамические рекомендации (Вкладки 2 и 3 / Слоты 2/3 и 3/3)

        public WinningBoard GetBoardById(string boardId)
        {
            if (!IsLoaded) LoadDatabase();
            if (string.IsNullOrEmpty(boardId)) return null;

            string cleanId = boardId.Replace("comp_", "");
            return _canonicalBoards.FirstOrDefault(b =>
                string.Equals(b.Id, boardId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(b.Archetype, boardId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(b.Archetype, cleanId, StringComparison.OrdinalIgnoreCase));
        }

        public Dictionary<string, int> GetPlayerTribeCounts(PlayerGameStateSnapshot state)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (state == null) return counts;

            int amalgamCount = 0;
            var allMinions = (state.BoardMinions ?? new List<BoardSlot>()).Concat(state.HandMinions ?? new List<BoardSlot>());
            foreach (var m in allMinions)
            {
                if (m?.Card?.TribesList != null)
                {
                    foreach (var tribe in m.Card.TribesList)
                    {
                        if (string.IsNullOrEmpty(tribe) || tribe == "Без типа") continue;
                        string canon = CanonicalizeTribe(tribe);
                        if (canon == "Все")
                        {
                            amalgamCount++;
                            continue;
                        }
                        if (!counts.ContainsKey(canon)) counts[canon] = 0;
                        counts[canon]++;
                    }
                }
            }

            // Существа с типом «Все» (Слияние / Куча-мала / Безводное чудовище) поддерживают каждую активную расу игрока
            if (amalgamCount > 0)
            {
                var keys = counts.Keys.ToList();
                if (keys.Count > 0)
                {
                    foreach (var k in keys)
                    {
                        counts[k] += amalgamCount;
                    }
                }
                else if (state.AvailableTribes != null)
                {
                    foreach (var avail in state.AvailableTribes)
                    {
                        string canon = CanonicalizeTribe(avail);
                        if (canon != "Все") counts[canon] = amalgamCount;
                    }
                }
            }

            return counts;
        }

        public CompRecommendation EvaluateBoardById(string boardId, PlayerGameStateSnapshot state, bool isLocked = false)
        {
            if (!IsLoaded) LoadDatabase();
            var board = GetBoardById(boardId);
            if (board == null) return null;

            var tribeCounts = GetPlayerTribeCounts(state);
            var rec = EvaluateBoardMatch(board, state, tribeCounts);
            if (rec != null)
            {
                rec.IsLocked = isLocked;
            }
            return rec;
        }

        private struct PlayerMinionItem
        {
            public int DbfId;
            public bool IsGolden;
        }

        private static (List<PlayerMinionItem> board, List<PlayerMinionItem> hand) ExtractPlayerMinionItems(PlayerGameStateSnapshot state)
        {
            var bList = new List<PlayerMinionItem>(state?.BoardMinions?.Count ?? 0);
            if (state?.BoardMinions != null)
            {
                foreach (var m in state.BoardMinions)
                {
                    if (m != null)
                    {
                        int id = m.DbfId > 0 ? m.DbfId : GetCanonicalCardId(m);
                        bList.Add(new PlayerMinionItem { DbfId = id, IsGolden = m.IsGolden });
                    }
                }
            }

            var hList = new List<PlayerMinionItem>(state?.HandMinions?.Count ?? 0);
            if (state?.HandMinions != null)
            {
                foreach (var m in state.HandMinions)
                {
                    if (m != null)
                    {
                        int id = m.DbfId > 0 ? m.DbfId : GetCanonicalCardId(m);
                        hList.Add(new PlayerMinionItem { DbfId = id, IsGolden = m.IsGolden });
                    }
                }
            }

            return (bList, hList);
        }

        private float QuickScoreBoard(WinningBoard targetBoard,
                                      List<PlayerMinionItem> playerBoard,
                                      List<PlayerMinionItem> playerHand,
                                      Dictionary<string, int> playerTribeCounts,
                                      PlayerGameStateSnapshot state,
                                      string normalizedStateHeroId = null)
        {
            if (targetBoard?.Minions == null || targetBoard.Minions.Count == 0) return 0f;

            // Zero-allocation битовые маски вместо клонирования списков на каждом из 1287 столов
            int usedBoardMask = 0;
            int usedHandMask = 0;
            int matchedTargetMask = 0;
            int goldenMatchedTargetMask = 0;
            int ownedCount = 0;

            float exactMatchesScore = 0f;
            var targetMinions = targetBoard.Minions;
            var cachedWeights = targetBoard.MinionWeights;
            var cachedDbfs = targetBoard.MinionDbfIds;
            int count = targetMinions.Count;

            int pBoardCount = playerBoard.Count;
            int pHandCount = playerHand.Count;

            // 1. Точное сопоставление (золотая к золотой, обычная к обычной)
            for (int i = 0; i < count; i++)
            {
                var tm = targetMinions[i];
                int dbf = (cachedDbfs != null && i < cachedDbfs.Length && cachedDbfs[i] > 0)
                    ? cachedDbfs[i]
                    : (tm.DbfId > 0 ? tm.DbfId : (tm.Card?.Id ?? 0));
                bool targetGold = tm.IsGolden;
                float weight = (cachedWeights != null && i < cachedWeights.Length)
                    ? cachedWeights[i]
                    : ((tm.Tier >= 5 ? 2.5f : (tm.Tier >= 3 ? 1.5f : 1.0f)) * GetMinionSpecificityMultiplier(tm, targetBoard));

                bool found = false;
                for (int bIdx = 0; bIdx < pBoardCount; bIdx++)
                {
                    if ((usedBoardMask & (1 << bIdx)) == 0 && playerBoard[bIdx].DbfId == dbf && playerBoard[bIdx].IsGolden == targetGold)
                    {
                        usedBoardMask |= (1 << bIdx);
                        matchedTargetMask |= (1 << i);
                        if (playerBoard[bIdx].IsGolden) goldenMatchedTargetMask |= (1 << i);
                        ownedCount++;
                        exactMatchesScore += weight * 1.0f;
                        found = true;
                        break;
                    }
                }
                if (found) continue;

                for (int hIdx = 0; hIdx < pHandCount; hIdx++)
                {
                    if ((usedHandMask & (1 << hIdx)) == 0 && playerHand[hIdx].DbfId == dbf && playerHand[hIdx].IsGolden == targetGold)
                    {
                        usedHandMask |= (1 << hIdx);
                        matchedTargetMask |= (1 << i);
                        if (playerHand[hIdx].IsGolden) goldenMatchedTargetMask |= (1 << i);
                        ownedCount++;
                        exactMatchesScore += weight * 0.75f;
                        break;
                    }
                }
            }

            // 2. Гибкое сопоставление (любая версия)
            for (int i = 0; i < count; i++)
            {
                if ((matchedTargetMask & (1 << i)) != 0) continue;
                var tm = targetMinions[i];
                int dbf = (cachedDbfs != null && i < cachedDbfs.Length && cachedDbfs[i] > 0)
                    ? cachedDbfs[i]
                    : (tm.DbfId > 0 ? tm.DbfId : (tm.Card?.Id ?? 0));
                float weight = (cachedWeights != null && i < cachedWeights.Length)
                    ? cachedWeights[i]
                    : ((tm.Tier >= 5 ? 2.5f : (tm.Tier >= 3 ? 1.5f : 1.0f)) * GetMinionSpecificityMultiplier(tm, targetBoard));

                bool found = false;
                for (int bIdx = 0; bIdx < pBoardCount; bIdx++)
                {
                    if ((usedBoardMask & (1 << bIdx)) == 0 && playerBoard[bIdx].DbfId == dbf)
                    {
                        usedBoardMask |= (1 << bIdx);
                        matchedTargetMask |= (1 << i);
                        if (playerBoard[bIdx].IsGolden) goldenMatchedTargetMask |= (1 << i);
                        ownedCount++;
                        exactMatchesScore += weight * 1.0f;
                        found = true;
                        break;
                    }
                }
                if (found) continue;

                for (int hIdx = 0; hIdx < pHandCount; hIdx++)
                {
                    if ((usedHandMask & (1 << hIdx)) == 0 && playerHand[hIdx].DbfId == dbf)
                    {
                        usedHandMask |= (1 << hIdx);
                        matchedTargetMask |= (1 << i);
                        if (playerHand[hIdx].IsGolden) goldenMatchedTargetMask |= (1 << i);
                        ownedCount++;
                        exactMatchesScore += weight * 0.75f;
                        break;
                    }
                }
            }

            // 3. Детектор пар и прогресса к триплету (Pair / Triple Detection)
            for (int i = 0; i < count; i++)
            {
                if ((matchedTargetMask & (1 << i)) == 0 || (goldenMatchedTargetMask & (1 << i)) != 0) continue;
                var tm = targetMinions[i];
                int dbf = (cachedDbfs != null && i < cachedDbfs.Length && cachedDbfs[i] > 0)
                    ? cachedDbfs[i]
                    : (tm.DbfId > 0 ? tm.DbfId : (tm.Card?.Id ?? 0));
                if (dbf <= 0) continue;

                float weight = (cachedWeights != null && i < cachedWeights.Length)
                    ? cachedWeights[i]
                    : ((tm.Tier >= 5 ? 2.5f : (tm.Tier >= 3 ? 1.5f : 1.0f)) * GetMinionSpecificityMultiplier(tm, targetBoard));

                bool pairFound = false;
                for (int bIdx = 0; bIdx < pBoardCount; bIdx++)
                {
                    if ((usedBoardMask & (1 << bIdx)) == 0 && !playerBoard[bIdx].IsGolden && playerBoard[bIdx].DbfId == dbf)
                    {
                        usedBoardMask |= (1 << bIdx);
                        exactMatchesScore += weight * (tm.IsGolden ? 0.35f : 0.20f);
                        pairFound = true;
                        break;
                    }
                }
                if (pairFound) continue;

                for (int hIdx = 0; hIdx < pHandCount; hIdx++)
                {
                    if ((usedHandMask & (1 << hIdx)) == 0 && !playerHand[hIdx].IsGolden && playerHand[hIdx].DbfId == dbf)
                    {
                        usedHandMask |= (1 << hIdx);
                        exactMatchesScore += weight * (tm.IsGolden ? 0.30f : 0.15f);
                        break;
                    }
                }
            }

            float tribeScore = 0f;
            string primaryTribe = !string.IsNullOrEmpty(targetBoard.CanonicalPrimaryTribe)
                ? targetBoard.CanonicalPrimaryTribe
                : CanonicalizeTribe(targetBoard.PrimaryTribe);

            if (primaryTribe == "Все")
            {
                tribeScore = Math.Min(0.75f, playerTribeCounts.Count * 0.18f);
            }
            else if (playerTribeCounts.TryGetValue(primaryTribe, out int cVal) && cVal > 0)
            {
                tribeScore = Math.Min(0.85f, 0.15f + cVal * 0.20f);
            }

            float normDivisor = targetBoard.NormDivisor > 0f ? targetBoard.NormDivisor : 6.5f;
            float normalizedCardScore = Math.Min(1.0f, exactMatchesScore / normDivisor);

            float heroBonus = 0f;
            string stateHeroNorm = normalizedStateHeroId ?? NormalizeHeroId(state?.HeroCardId);
            if (!string.IsNullOrEmpty(stateHeroNorm) && !string.IsNullOrEmpty(targetBoard.NormalizedHeroCardId))
            {
                if (string.Equals(stateHeroNorm, targetBoard.NormalizedHeroCardId, StringComparison.OrdinalIgnoreCase))
                {
                    heroBonus = 0.35f;
                }
            }

            float turnProgress = Math.Min(1.0f, Math.Max(0.0f, (float)((state?.Turn ?? 1) - 1) / 8.0f));
            float tribeWeight = 0.60f - 0.30f * turnProgress;
            float cardWeight = 0.25f + 0.50f * turnProgress;
            float metaBonus = (float)(targetBoard.Top1Rate * 0.08);

            float tavernBonus = CalculateTavernReachabilityBonus(targetBoard, state);
            float trinketBonus = CalculateTrinketSynergyBonus(targetBoard, state);
            float survivalBonus = CalculateSurvivalTempoAdjustment(targetBoard, state, ownedCount);

            float finalScore = 0.05f
                             + (tribeScore * tribeWeight)
                             + (normalizedCardScore * cardWeight)
                             + heroBonus
                             + metaBonus
                             + tavernBonus
                             + trinketBonus
                             + survivalBonus;

            return Math.Max(0.05f, Math.Min(0.98f, finalScore));
        }

        public List<CompRecommendation> FindDynamicAlternatives(PlayerGameStateSnapshot state, HashSet<string> excludeBoardIds, int count = 2)
        {
            if (!IsLoaded) LoadDatabase();
            if (_canonicalBoards.Count == 0) return new List<CompRecommendation>();

            if (state == null) state = new PlayerGameStateSnapshot();
            if (excludeBoardIds == null) excludeBoardIds = new HashSet<string>();

            TribeFlags bannedMask = TribeFlagsHelper.ListToMask(state.BannedTribes);
            TribeFlags availMask = (state.AvailableTribes != null && state.AvailableTribes.Count >= 3 && state.AvailableTribes.Count <= 7)
                ? TribeFlagsHelper.ListToMask(state.AvailableTribes)
                : TribeFlags.None;

            // 1. Исключаем забаненные столы, ID столов и архетипы из excludeBoardIds мгновенно через маски
            var allowedBoards = _canonicalBoards.Where(b =>
                !excludeBoardIds.Contains(b.Id) &&
                (string.IsNullOrEmpty(b.Archetype) || !excludeBoardIds.Contains(b.Archetype)) &&
                IsBoardAllowedInLobby(b, availMask, bannedMask)
            ).ToList();

            if (allowedBoards.Count == 0)
            {
                allowedBoards = _canonicalBoards.Where(b =>
                    !excludeBoardIds.Contains(b.Id) &&
                    IsBoardAllowedInLobby(b, availMask, bannedMask)
                ).ToList();
            }

            if (allowedBoards.Count == 0) return new List<CompRecommendation>();

            // 2. Подсчет племен на столе и в руке
            var playerTribeCounts = GetPlayerTribeCounts(state);
            var (pBoard, pHand) = ExtractPlayerMinionItems(state);
            string normHero = NormalizeHeroId(state.HeroCardId);

            // 3. Быстрая оценка и сортировка без аллокаций тяжелых UI моделей
            var sorted = allowedBoards.Select(b => new
            {
                Board = b,
                Score = QuickScoreBoard(b, pBoard, pHand, playerTribeCounts, state, normHero)
            })
            .OrderByDescending(r => r.Board.Minions != null && r.Board.Minions.Count >= 7)
            .ThenByDescending(r => r.Score)
            .ThenBy(r => r.Board.AveragePlacement)
            .ToList();

            // 4. Исключение повторений одинаковых архетипов и создание детальных рекомендаций ТОЛЬКО для победителей
            var distinctResults = new List<CompRecommendation>();
            var seenArchetypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in sorted)
            {
                if (seenArchetypes.Contains(item.Board.Archetype))
                    continue;

                seenArchetypes.Add(item.Board.Archetype);
                var rec = EvaluateBoardMatch(item.Board, state, playerTribeCounts);
                distinctResults.Add(rec);
                if (distinctResults.Count >= count)
                    break;
            }

            return distinctResults;
        }

        public CompRecommendation FindDynamicCompForTribe(string tribeName, PlayerGameStateSnapshot state, HashSet<string> excludeBoardIds = null)
        {
            if (!IsLoaded) LoadDatabase();
            if (_canonicalBoards.Count == 0 || string.IsNullOrEmpty(tribeName)) return null;

            if (state == null) state = new PlayerGameStateSnapshot();
            if (excludeBoardIds == null) excludeBoardIds = new HashSet<string>();

            string canonTribe = CanonicalizeTribe(tribeName);
            TribeFlags targetFlag = TribeFlagsHelper.StringToFlag(tribeName);

            TribeFlags bannedMask = TribeFlagsHelper.ListToMask(state.BannedTribes);
            TribeFlags availMask = (state.AvailableTribes != null && state.AvailableTribes.Count >= 3 && state.AvailableTribes.Count <= 7)
                ? TribeFlagsHelper.ListToMask(state.AvailableTribes)
                : TribeFlags.None;

            var matchingBoards = _canonicalBoards.Where(b =>
                !excludeBoardIds.Contains(b.Id) &&
                (b.PrimaryTribeFlag == targetFlag || TribesMatch(b.PrimaryTribe, canonTribe)) &&
                IsBoardAllowedInLobby(b, availMask, bannedMask)
            ).ToList();

            if (matchingBoards.Count == 0) return null;

            var playerTribeCounts = GetPlayerTribeCounts(state);
            var (pBoard, pHand) = ExtractPlayerMinionItems(state);

            var bestItem = matchingBoards.Select(b => new
            {
                Board = b,
                Score = QuickScoreBoard(b, pBoard, pHand, playerTribeCounts, state)
            })
            .OrderByDescending(r => r.Board.Minions != null && r.Board.Minions.Count >= 7)
            .ThenByDescending(r => r.Score)
            .ThenBy(r => r.Board.AveragePlacement)
            .FirstOrDefault();

            if (bestItem == null) return null;
            return EvaluateBoardMatch(bestItem.Board, state, playerTribeCounts);
        }

        #endregion

        #region Главное меню / Универсальный поиск столов (FindBestComps)

        public List<CompRecommendation> FindBestComps(PlayerGameStateSnapshot state, int topN = 3)
        {
            if (!IsLoaded) LoadDatabase();
            if (_canonicalBoards.Count == 0) return new List<CompRecommendation>();

            if (state == null) state = new PlayerGameStateSnapshot();

            TribeFlags bannedMask = TribeFlagsHelper.ListToMask(state.BannedTribes);
            TribeFlags availMask = (state.AvailableTribes != null && state.AvailableTribes.Count >= 3 && state.AvailableTribes.Count <= 7)
                ? TribeFlagsHelper.ListToMask(state.AvailableTribes)
                : TribeFlags.None;

            var allowedBoards = _canonicalBoards.Where(b => IsBoardAllowedInLobby(b, availMask, bannedMask)).ToList();
            if (allowedBoards.Count == 0) allowedBoards = _canonicalBoards.Where(b => b.Minions != null && b.Minions.Count >= 6).ToList();
            if (allowedBoards.Count == 0) allowedBoards = _canonicalBoards;

            var playerTribeCounts = GetPlayerTribeCounts(state);
            var (pBoard, pHand) = ExtractPlayerMinionItems(state);

            var sorted = allowedBoards.Select(b => new
            {
                Board = b,
                Score = QuickScoreBoard(b, pBoard, pHand, playerTribeCounts, state)
            })
            .OrderByDescending(r => r.Board.Minions != null && r.Board.Minions.Count >= 7)
            .ThenByDescending(r => r.Score)
            .ThenBy(r => r.Board.AveragePlacement)
            .ToList();

            // Обработка зафиксированного билда
            WinningBoard lockedBoard = null;
            if (!string.IsNullOrEmpty(state.LockedArchetypeId))
            {
                string cleanLock = state.LockedArchetypeId.Replace("comp_", "");
                lockedBoard = _canonicalBoards.FirstOrDefault(b =>
                    string.Equals(b.Id, state.LockedArchetypeId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(b.Archetype, state.LockedArchetypeId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(b.Archetype, cleanLock, StringComparison.OrdinalIgnoreCase));
            }

            var result = new List<CompRecommendation>();
            var seenArch = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (lockedBoard != null)
            {
                var lockedRec = EvaluateBoardMatch(lockedBoard, state, playerTribeCounts);
                lockedRec.IsLocked = true;
                result.Add(lockedRec);
                seenArch.Add(lockedBoard.Archetype);
            }

            foreach (var r in sorted)
            {
                if (seenArch.Contains(r.Board.Archetype)) continue;
                seenArch.Add(r.Board.Archetype);

                var rec = EvaluateBoardMatch(r.Board, state, playerTribeCounts);
                result.Add(rec);
                if (result.Count >= topN) break;
            }

            return result;
        }

        #endregion

        #region Оценка соответствия стола (EvaluateBoardMatch)

        public CompRecommendation EvaluateBoardMatch(WinningBoard targetBoard, PlayerGameStateSnapshot state, Dictionary<string, int> playerTribeCounts)
        {
            var availableBoard = (state?.BoardMinions ?? new List<BoardSlot>()).Where(m => m != null).ToList();
            var availableHand = (state?.HandMinions ?? new List<BoardSlot>()).Where(m => m != null).ToList();

            var displaySlots = new List<BoardSlot>();
            var missingPriority = new List<BoardSlot>();

            // 1. Создаем копии слотов целевого стола
            var targetSlots = new List<BoardSlot>();
            foreach (var targetMinion in (targetBoard?.Minions ?? new List<BoardSlot>()))
            {
                if (targetMinion == null) continue;
                var slotCopy = new BoardSlot
                {
                    CardId = targetMinion.CardId,
                    DbfId = targetMinion.DbfId,
                    Card = targetMinion.Card,
                    IsGolden = targetMinion.IsGolden,
                    Attack = targetMinion.Attack,
                    Health = targetMinion.Health,
                    HasTaunt = targetMinion.HasTaunt,
                    HasDivineShield = targetMinion.HasDivineShield,
                    HasReborn = targetMinion.HasReborn,
                    HasPoisonous = targetMinion.HasPoisonous,
                    ZonePosition = targetMinion.ZonePosition,
                    Status = SlotStatus.NeedToFind
                };

                // Гарантируем наличие метаданных карты
                if (slotCopy.Card == null)
                {
                    var resolved = CardDatabaseService.Instance.ResolveCard(slotCopy.CardId, slotCopy.DbfId);
                    if (resolved.card != null)
                    {
                        slotCopy.Card = resolved.card;
                    }
                }

                targetSlots.Add(slotCopy);
            }

            var matchedSlotIndices = new HashSet<int>();

            // 2. Шаг 1: Точное сопоставление (золотая к золотой, обычная к обычной) с изъятием из пула игрока
            for (int i = 0; i < targetSlots.Count; i++)
            {
                var slotCopy = targetSlots[i];
                bool targetIsGold = slotCopy.IsGolden;

                // Проверка на столе игрока
                var boardMatch = availableBoard.FirstOrDefault(c => CardsMatch(slotCopy, c) && c.IsGolden == targetIsGold);
                if (boardMatch != null)
                {
                    slotCopy.Status = SlotStatus.OnBoard;
                    if (boardMatch.IsGolden) slotCopy.IsGolden = true;
                    availableBoard.Remove(boardMatch);
                    matchedSlotIndices.Add(i);
                    continue;
                }

                // Проверка в руке игрока
                var handMatch = availableHand.FirstOrDefault(c => CardsMatch(slotCopy, c) && c.IsGolden == targetIsGold);
                if (handMatch != null)
                {
                    slotCopy.Status = SlotStatus.InHand;
                    if (handMatch.IsGolden) slotCopy.IsGolden = true;
                    availableHand.Remove(handMatch);
                    matchedSlotIndices.Add(i);
                    continue;
                }

            }

            // 3. Шаг 2: Гибкое сопоставление оставшихся слотов (любая версия карты) с изъятием из пула
            for (int i = 0; i < targetSlots.Count; i++)
            {
                if (matchedSlotIndices.Contains(i)) continue;

                var slotCopy = targetSlots[i];

                // Проверка на столе
                var boardMatch = availableBoard.FirstOrDefault(c => CardsMatch(slotCopy, c));
                if (boardMatch != null)
                {
                    slotCopy.Status = SlotStatus.OnBoard;
                    if (boardMatch.IsGolden) slotCopy.IsGolden = true;
                    availableBoard.Remove(boardMatch);
                    matchedSlotIndices.Add(i);
                    continue;
                }

                // Проверка в руке
                var handMatch = availableHand.FirstOrDefault(c => CardsMatch(slotCopy, c));
                if (handMatch != null)
                {
                    slotCopy.Status = SlotStatus.InHand;
                    if (handMatch.IsGolden) slotCopy.IsGolden = true;
                    availableHand.Remove(handMatch);
                    matchedSlotIndices.Add(i);
                    continue;
                }


                // Карты нет в пуле игрока — остается статус NeedToFind
                slotCopy.Status = SlotStatus.NeedToFind;
            }

            // 4. Подсчет очков (включая детектор пар к триплету) и формирование списков
            float exactMatchesScore = 0f;
            int ownedCount = 0;

            foreach (var slotCopy in targetSlots)
            {
                int tier = slotCopy.Tier > 0 ? slotCopy.Tier : 4;
                float specMult = GetMinionSpecificityMultiplier(slotCopy, targetBoard);
                float minionTierWeight = (tier >= 5 ? 2.5f : (tier >= 3 ? 1.5f : 1.0f)) * specMult;

                switch (slotCopy.Status)
                {
                    case SlotStatus.OnBoard:
                        exactMatchesScore += minionTierWeight * 1.0f;
                        ownedCount++;
                        break;
                    case SlotStatus.InHand:
                        exactMatchesScore += minionTierWeight * 0.75f;
                        ownedCount++;
                        break;
                    default: // NeedToFind
                        if (IsCardAvailableInLobby(slotCopy.Card, state?.AvailableTribes, state?.BannedTribes))
                        {
                            missingPriority.Add(slotCopy);
                        }
                        break;
                }

                // Если слот закрыт обычной картой, а в пуле осталась вторая копия (пара к триплету) — начисляем бонус прогресса
                if ((slotCopy.Status == SlotStatus.OnBoard || slotCopy.Status == SlotStatus.InHand) && !slotCopy.IsGolden)
                {
                    var pairBoard = availableBoard.FirstOrDefault(c => !c.IsGolden && CardsMatch(slotCopy, c));
                    if (pairBoard != null)
                    {
                        availableBoard.Remove(pairBoard);
                        exactMatchesScore += minionTierWeight * 0.30f;
                    }
                    else
                    {
                        var pairHand = availableHand.FirstOrDefault(c => !c.IsGolden && CardsMatch(slotCopy, c));
                        if (pairHand != null)
                        {
                            availableHand.Remove(pairHand);
                            exactMatchesScore += minionTierWeight * 0.25f;
                        }
                    }
                }

                displaySlots.Add(slotCopy);
            }

            // Племенная ориентация
            float tribeScore = 0f;
            string primaryTribe = CanonicalizeTribe(targetBoard.PrimaryTribe);

            if (primaryTribe == "Все")
            {
                tribeScore = Math.Min(0.75f, playerTribeCounts.Count * 0.18f);
            }
            else if (playerTribeCounts.TryGetValue(primaryTribe, out int count) && count > 0)
            {
                tribeScore = Math.Min(0.85f, 0.15f + count * 0.20f);
            }

            float maxPossibleScore = 0f;
            foreach (var s in targetSlots)
            {
                int t = s.Tier > 0 ? s.Tier : 4;
                float specMult = GetMinionSpecificityMultiplier(s, targetBoard);
                maxPossibleScore += (t >= 5 ? 2.5f : (t >= 3 ? 1.5f : 1.0f)) * specMult;
            }
            float normDivisor = Math.Max(6.5f, maxPossibleScore * 0.85f);
            float normalizedCardScore = Math.Min(1.0f, exactMatchesScore / normDivisor);

            // Бонус героя
            float heroBonus = 0f;
            if (!string.IsNullOrEmpty(state.HeroCardId) && !string.IsNullOrEmpty(targetBoard.HeroCardId))
            {
                if (HeroesMatch(state.HeroCardId, targetBoard.HeroCardId))
                {
                    heroBonus = 0.35f;
                }
            }


            float turnProgress = Math.Min(1.0f, Math.Max(0.0f, (float)(state.Turn - 1) / 8.0f));
            float tribeWeight = 0.60f - 0.30f * turnProgress;
            float cardWeight = 0.25f + 0.50f * turnProgress;

            float metaBonus = (float)(targetBoard.Top1Rate * 0.08);

            float tavernBonus = CalculateTavernReachabilityBonus(targetBoard, state);
            float trinketBonus = CalculateTrinketSynergyBonus(targetBoard, state);
            float survivalBonus = CalculateSurvivalTempoAdjustment(targetBoard, state, ownedCount);

            float finalScore = 0.05f
                             + (tribeScore * tribeWeight)
                             + (normalizedCardScore * cardWeight)
                             + heroBonus
                             + metaBonus
                             + tavernBonus
                             + trinketBonus
                             + survivalBonus;

            finalScore = Math.Max(0.05f, Math.Min(0.98f, finalScore));

            missingPriority = missingPriority
                .OrderByDescending(m => GetMissingCardPriorityOrder(m, targetBoard))
                .ThenByDescending(m => m.Tier)
                .ToList();

            return new CompRecommendation
            {
                Board = targetBoard,
                SimilarityScore = finalScore,
                DisplaySlots = displaySlots,
                OwnedCount = ownedCount,
                MissingCount = missingPriority.Count,
                MissingPriorityCards = missingPriority,
                IsLocked = false
            };
        }

        #endregion
    }
}
