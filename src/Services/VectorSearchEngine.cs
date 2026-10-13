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
        public List<string> AvailableTribes { get; set; } = new List<string>();
        public List<string> BannedTribes { get; set; } = new List<string>();

        public List<BoardSlot> BoardMinions { get; set; } = new List<BoardSlot>();
        public List<BoardSlot> HandMinions { get; set; } = new List<BoardSlot>();
        public List<BoardSlot> FrozenTavernMinions { get; set; } = new List<BoardSlot>();
        public List<string> ActiveTrinkets { get; set; } = new List<string>();

        public string LockedArchetypeId { get; set; }
    }

    public class VectorSearchEngine
    {
        private static VectorSearchEngine _instance;
        public static VectorSearchEngine Instance => _instance ?? (_instance = new VectorSearchEngine());

        private readonly List<WinningBoard> _canonicalBoards = new List<WinningBoard>();
        public bool IsLoaded { get; private set; }
        public int BoardsCount => _canonicalBoards.Count;
        public IReadOnlyList<WinningBoard> AllBoards => _canonicalBoards;

        public static readonly string[] AllCanonicalTribes = new[]
        {
            "Механизм", "Зверь", "Мурлок", "Нежить", "Демон",
            "Элементаль", "Нага", "Пират", "Дракон", "Свинобраз", "Абберация"
        };

        public void ReloadDatabase(string jsonPath = null)
        {
            IsLoaded = false;
            LoadDatabase(jsonPath);
        }

        public void LoadDatabase(string jsonPath = null)
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

            _canonicalBoards.Clear();

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

                _canonicalBoards.Add(board);
            }

            IsLoaded = true;
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
                if (neutralCount >= 3) return true;
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
            int idx = heroCardId.IndexOf("_SKIN_", StringComparison.OrdinalIgnoreCase);
            return idx > 0 ? heroCardId.Substring(0, idx) : heroCardId;
        }

        public static bool HeroesMatch(string heroA, string heroB)
        {
            if (string.IsNullOrWhiteSpace(heroA) || string.IsNullOrWhiteSpace(heroB)) return false;
            string normA = NormalizeHeroId(heroA);
            string normB = NormalizeHeroId(heroB);
            return string.Equals(normA, normB, StringComparison.OrdinalIgnoreCase) ||
                   normA.IndexOf(normB, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normB.IndexOf(normA, StringComparison.OrdinalIgnoreCase) >= 0;
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

            TribeFlags bannedMask = TribeFlagsHelper.ListToMask(bannedTribes);
            if (bannedMask != TribeFlags.None && (card.TribeMask & bannedMask) != TribeFlags.None)
                return false;

            if (availableTribes != null && availableTribes.Count >= 3 && availableTribes.Count <= 7)
            {
                TribeFlags availMask = TribeFlagsHelper.ListToMask(availableTribes);
                if (availMask != TribeFlags.None && (card.TribeMask & availMask) == TribeFlags.None)
                    return false;
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

            // 1. Быстрая побитовая проверка запрещенных племён
            if (bannedMask != TribeFlags.None)
            {
                if (board.PrimaryTribeFlag != TribeFlags.All && (board.PrimaryTribeFlag & bannedMask) != TribeFlags.None)
                    return false;

                if ((board.RequiredTribesMask & bannedMask) != TribeFlags.None)
                    return false;

                if ((board.MinionTribesMask & bannedMask) != TribeFlags.None)
                    return false;
            }

            // 2. Быстрая проверка доступных племён лобби
            if (availableMask != TribeFlags.None)
            {
                if (board.PrimaryTribeFlag != TribeFlags.All && (board.PrimaryTribeFlag & availableMask) == TribeFlags.None)
                    return false;

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

            var allMinions = (state.BoardMinions ?? new List<BoardSlot>()).Concat(state.HandMinions ?? new List<BoardSlot>());
            foreach (var m in allMinions)
            {
                if (m?.Card?.TribesList != null)
                {
                    foreach (var tribe in m.Card.TribesList)
                    {
                        if (string.IsNullOrEmpty(tribe) || tribe == "Без типа") continue;
                        string canon = CanonicalizeTribe(tribe);
                        if (!counts.ContainsKey(canon)) counts[canon] = 0;
                        counts[canon]++;
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

        private static (List<PlayerMinionItem> board, List<PlayerMinionItem> hand, List<PlayerMinionItem> frozen) ExtractPlayerMinionItems(PlayerGameStateSnapshot state)
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

            var fList = new List<PlayerMinionItem>(state?.FrozenTavernMinions?.Count ?? 0);
            if (state?.FrozenTavernMinions != null)
            {
                foreach (var m in state.FrozenTavernMinions)
                {
                    if (m != null)
                    {
                        int id = m.DbfId > 0 ? m.DbfId : GetCanonicalCardId(m);
                        fList.Add(new PlayerMinionItem { DbfId = id, IsGolden = m.IsGolden });
                    }
                }
            }

            return (bList, hList, fList);
        }

        private float QuickScoreBoard(WinningBoard targetBoard,
                                      List<PlayerMinionItem> playerBoard,
                                      List<PlayerMinionItem> playerHand,
                                      List<PlayerMinionItem> playerFrozen,
                                      Dictionary<string, int> playerTribeCounts,
                                      PlayerGameStateSnapshot state)
        {
            if (targetBoard?.Minions == null || targetBoard.Minions.Count == 0) return 0f;

            var availBoard = new List<PlayerMinionItem>(playerBoard);
            var availHand = new List<PlayerMinionItem>(playerHand);
            var availFrozen = new List<PlayerMinionItem>(playerFrozen);

            float exactMatchesScore = 0f;
            var targetMinions = targetBoard.Minions;
            int count = targetMinions.Count;
            var matched = new bool[count];

            // 1. Точное сопоставление (золотая к золотой)
            for (int i = 0; i < count; i++)
            {
                var tm = targetMinions[i];
                int dbf = tm.DbfId > 0 ? tm.DbfId : (tm.Card?.Id ?? 0);
                bool targetGold = tm.IsGolden;

                int bIdx = availBoard.FindIndex(m => m.DbfId == dbf && m.IsGolden == targetGold);
                if (bIdx >= 0)
                {
                    availBoard.RemoveAt(bIdx);
                    matched[i] = true;
                    int tier = tm.Tier > 0 ? tm.Tier : 4;
                    float weight = tier >= 5 ? 2.5f : (tier >= 3 ? 1.5f : 1.0f);
                    exactMatchesScore += weight * 1.0f;
                    continue;
                }

                int hIdx = availHand.FindIndex(m => m.DbfId == dbf && m.IsGolden == targetGold);
                if (hIdx >= 0)
                {
                    availHand.RemoveAt(hIdx);
                    matched[i] = true;
                    int tier = tm.Tier > 0 ? tm.Tier : 4;
                    float weight = tier >= 5 ? 2.5f : (tier >= 3 ? 1.5f : 1.0f);
                    exactMatchesScore += weight * 0.75f;
                    continue;
                }

                int fIdx = availFrozen.FindIndex(m => m.DbfId == dbf && m.IsGolden == targetGold);
                if (fIdx >= 0)
                {
                    availFrozen.RemoveAt(fIdx);
                    matched[i] = true;
                    int tier = tm.Tier > 0 ? tm.Tier : 4;
                    float weight = tier >= 5 ? 2.5f : (tier >= 3 ? 1.5f : 1.0f);
                    exactMatchesScore += weight * 0.30f;
                    continue;
                }
            }

            // 2. Гибкое сопоставление (любая версия)
            for (int i = 0; i < count; i++)
            {
                if (matched[i]) continue;
                var tm = targetMinions[i];
                int dbf = tm.DbfId > 0 ? tm.DbfId : (tm.Card?.Id ?? 0);

                int bIdx = availBoard.FindIndex(m => m.DbfId == dbf);
                if (bIdx >= 0)
                {
                    availBoard.RemoveAt(bIdx);
                    int tier = tm.Tier > 0 ? tm.Tier : 4;
                    float weight = tier >= 5 ? 2.5f : (tier >= 3 ? 1.5f : 1.0f);
                    exactMatchesScore += weight * 1.0f;
                    continue;
                }

                int hIdx = availHand.FindIndex(m => m.DbfId == dbf);
                if (hIdx >= 0)
                {
                    availHand.RemoveAt(hIdx);
                    int tier = tm.Tier > 0 ? tm.Tier : 4;
                    float weight = tier >= 5 ? 2.5f : (tier >= 3 ? 1.5f : 1.0f);
                    exactMatchesScore += weight * 0.75f;
                    continue;
                }

                int fIdx = availFrozen.FindIndex(m => m.DbfId == dbf);
                if (fIdx >= 0)
                {
                    availFrozen.RemoveAt(fIdx);
                    int tier = tm.Tier > 0 ? tm.Tier : 4;
                    float weight = tier >= 5 ? 2.5f : (tier >= 3 ? 1.5f : 1.0f);
                    exactMatchesScore += weight * 0.30f;
                    continue;
                }
            }

            float tribeScore = 0f;
            string primaryTribe = CanonicalizeTribe(targetBoard.PrimaryTribe);
            if (primaryTribe == "Все")
            {
                tribeScore = Math.Min(0.75f, playerTribeCounts.Count * 0.18f);
            }
            else if (playerTribeCounts.TryGetValue(primaryTribe, out int cVal) && cVal > 0)
            {
                tribeScore = Math.Min(0.85f, 0.15f + cVal * 0.20f);
            }

            float normalizedCardScore = Math.Min(1.0f, exactMatchesScore / 7.0f);

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

            float finalScore = 0.05f
                             + (tribeScore * tribeWeight)
                             + (normalizedCardScore * cardWeight)
                             + heroBonus
                             + metaBonus;

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

            // 1. Исключаем забаненные столы и столы из excludeBoardIds мгновенно через маски
            var allowedBoards = _canonicalBoards.Where(b =>
                !excludeBoardIds.Contains(b.Id) &&
                IsBoardAllowedInLobby(b, availMask, bannedMask)
            ).ToList();

            if (allowedBoards.Count == 0) return new List<CompRecommendation>();

            // 2. Подсчет племен на столе и в руке
            var playerTribeCounts = GetPlayerTribeCounts(state);
            var (pBoard, pHand, pFrozen) = ExtractPlayerMinionItems(state);

            // 3. Быстрая оценка и сортировка без аллокаций тяжелых UI моделей
            var sorted = allowedBoards.Select(b => new
            {
                Board = b,
                Score = QuickScoreBoard(b, pBoard, pHand, pFrozen, playerTribeCounts, state)
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
            var (pBoard, pHand, pFrozen) = ExtractPlayerMinionItems(state);

            var bestItem = matchingBoards.Select(b => new
            {
                Board = b,
                Score = QuickScoreBoard(b, pBoard, pHand, pFrozen, playerTribeCounts, state)
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
            var (pBoard, pHand, pFrozen) = ExtractPlayerMinionItems(state);

            var sorted = allowedBoards.Select(b => new
            {
                Board = b,
                Score = QuickScoreBoard(b, pBoard, pHand, pFrozen, playerTribeCounts, state)
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
            var availableFrozen = (state?.FrozenTavernMinions ?? new List<BoardSlot>()).Where(m => m != null).ToList();

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

                // Проверка в заморозке таверны
                var frozenMatch = availableFrozen.FirstOrDefault(c => CardsMatch(slotCopy, c) && c.IsGolden == targetIsGold);
                if (frozenMatch != null)
                {
                    slotCopy.Status = SlotStatus.FrozenInTavern;
                    if (frozenMatch.IsGolden) slotCopy.IsGolden = true;
                    availableFrozen.Remove(frozenMatch);
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

                // Проверка в заморозке
                var frozenMatch = availableFrozen.FirstOrDefault(c => CardsMatch(slotCopy, c));
                if (frozenMatch != null)
                {
                    slotCopy.Status = SlotStatus.FrozenInTavern;
                    if (frozenMatch.IsGolden) slotCopy.IsGolden = true;
                    availableFrozen.Remove(frozenMatch);
                    matchedSlotIndices.Add(i);
                    continue;
                }

                // Карты нет в пуле игрока — остается статус NeedToFind
                slotCopy.Status = SlotStatus.NeedToFind;
            }

            // 4. Подсчет очков и формирование списков
            float exactMatchesScore = 0f;
            int ownedCount = 0;

            foreach (var slotCopy in targetSlots)
            {
                int tier = slotCopy.Tier > 0 ? slotCopy.Tier : 4;
                float minionTierWeight = tier >= 5 ? 2.5f : (tier >= 3 ? 1.5f : 1.0f);

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
                    case SlotStatus.FrozenInTavern:
                        exactMatchesScore += minionTierWeight * 0.30f;
                        break;
                    default: // NeedToFind
                        if (IsCardAvailableInLobby(slotCopy.Card, state?.AvailableTribes, state?.BannedTribes))
                        {
                            missingPriority.Add(slotCopy);
                        }
                        break;
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

            float normalizedCardScore = Math.Min(1.0f, exactMatchesScore / 7.0f);

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

            float finalScore = 0.05f
                             + (tribeScore * tribeWeight)
                             + (normalizedCardScore * cardWeight)
                             + heroBonus
                             + metaBonus;

            finalScore = Math.Max(0.05f, Math.Min(0.98f, finalScore));

            missingPriority = missingPriority.OrderByDescending(m => m.Tier).ToList();

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
