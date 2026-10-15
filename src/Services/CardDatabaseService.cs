using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HearthDb;
using HSCompsPlugin.Models;
using Newtonsoft.Json;

namespace HSCompsPlugin.Services
{
    public class CardDatabaseService
    {
        private static CardDatabaseService _instance;
        public static CardDatabaseService Instance => _instance ?? (_instance = new CardDatabaseService());

        private readonly ConcurrentDictionary<int, OfficialCard> _cardsByDbfId = new ConcurrentDictionary<int, OfficialCard>();
        private readonly ConcurrentDictionary<string, OfficialCard> _cardsByCardId = new ConcurrentDictionary<string, OfficialCard>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<int, OfficialCard> _baseCardsByGoldenUpgradeId = new ConcurrentDictionary<int, OfficialCard>();
        private readonly ConcurrentDictionary<int, string> _cardIdByDbfId = new ConcurrentDictionary<int, string>();
        private readonly object _loadLock = new object();

        public volatile bool IsLoaded;
        public int TotalCardsCount => _cardsByDbfId.Count;

        public string GetCardIdByDbfId(int dbfId)
        {
            if (dbfId <= 0) return null;
            if (!IsLoaded) LoadDatabase();
            _cardIdByDbfId.TryGetValue(dbfId, out var cid);
            return cid;
        }

        public void LoadDatabase(string jsonPath = null)
        {
            if (IsLoaded) return;
            lock (_loadLock)
            {
                if (IsLoaded) return;

            if (string.IsNullOrEmpty(jsonPath))
            {
                // Поиск bg_cards_official.json в директории сборки или плагина
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string assemblyDir = Path.GetDirectoryName(typeof(CardDatabaseService).Assembly.Location) ?? "";

                string[] candidatePaths = new[]
                {
                    Path.Combine(assemblyDir, "bg_cards_official.json"),
                    Path.Combine(baseDir, "bg_cards_official.json"),
                    Path.Combine(baseDir, "Plugins", "HSCompsPlugin", "bg_cards_official.json"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HearthstoneDeckTracker", "Plugins", "HSCompsPlugin", "bg_cards_official.json")
                };

                jsonPath = candidatePaths.FirstOrDefault(File.Exists);
            }

            if (string.IsNullOrEmpty(jsonPath) || !File.Exists(jsonPath))
            {
                throw new FileNotFoundException($"Файл официальной базы карт не найден. Путь: {jsonPath}");
            }

            string jsonContent = File.ReadAllText(jsonPath, Encoding.UTF8);
            var container = JsonConvert.DeserializeObject<OfficialCardsContainer>(jsonContent);

            if (container?.Cards == null)
            {
                throw new InvalidDataException("Не удалось распарсить базу официальных карт bg_cards_official.json");
            }

            _cardsByDbfId.Clear();
            _cardsByCardId.Clear();
            _baseCardsByGoldenUpgradeId.Clear();
            _cardIdByDbfId.Clear();

            foreach (var card in container.Cards)
            {
                _cardsByDbfId[card.Id] = card;

                if (card.GoldenUpgradeId.HasValue && card.GoldenUpgradeId.Value > 0)
                {
                    _baseCardsByGoldenUpgradeId[card.GoldenUpgradeId.Value] = card;
                }

                if (!string.IsNullOrEmpty(card.Slug) && !char.IsDigit(card.Slug[0]))
                {
                    _cardsByCardId[card.Slug] = card;
                    _cardIdByDbfId[card.Id] = card.Slug;
                }
            }

            // Загрузка дополнительных карт из supplementary_cards.json (если файл существует)
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string assemblyDir = Path.GetDirectoryName(typeof(CardDatabaseService).Assembly.Location) ?? "";
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

                string[] suppCandidates = new[]
                {
                    Path.Combine(Path.GetDirectoryName(jsonPath) ?? "", "supplementary_cards.json"),
                    Path.Combine(assemblyDir, "supplementary_cards.json"),
                    Path.Combine(baseDir, "supplementary_cards.json"),
                    Path.Combine(baseDir, "Plugins", "HSCompsPlugin", "supplementary_cards.json"),
                    Path.Combine(appData, "HearthstoneDeckTracker", "Plugins", "HSCompsPlugin", "supplementary_cards.json")
                };

                string suppPath = suppCandidates.FirstOrDefault(File.Exists);
                if (!string.IsNullOrEmpty(suppPath))
                {
                    string suppJson = File.ReadAllText(suppPath, Encoding.UTF8);
                    var suppContainer = JsonConvert.DeserializeObject<OfficialCardsContainer>(suppJson);
                    if (suppContainer?.Cards != null)
                    {
                        foreach (var card in suppContainer.Cards)
                        {
                            _cardsByDbfId[card.Id] = card;
                            if (card.GoldenUpgradeId.HasValue && card.GoldenUpgradeId.Value > 0)
                            {
                                _baseCardsByGoldenUpgradeId[card.GoldenUpgradeId.Value] = card;
                            }
                            if (!string.IsNullOrEmpty(card.Slug))
                            {
                                _cardsByCardId[card.Slug] = card;
                                if (!char.IsDigit(card.Slug[0]))
                                {
                                    _cardIdByDbfId[card.Id] = card.Slug;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CardDatabaseService] Ошибка загрузки supplementary_cards.json: {ex.Message}");
            }

            // Маппинг строковых CardId из HearthDb
            try
            {
                foreach (var kvp in Cards.All)
                {
                    string cardId = kvp.Key;
                    var dbCard = kvp.Value;
                    if (dbCard == null) continue;

                    if (_cardsByDbfId.TryGetValue(dbCard.DbfId, out var officialCard))
                    {
                        _cardsByCardId[cardId] = officialCard;
                        if (!cardId.EndsWith("_G", StringComparison.OrdinalIgnoreCase))
                        {
                            _cardIdByDbfId[dbCard.DbfId] = cardId;
                        }
                    }
                    else if (_baseCardsByGoldenUpgradeId.TryGetValue(dbCard.DbfId, out var baseCard))
                    {
                        _cardsByCardId[cardId] = baseCard;
                    }
                }
            }
            catch (Exception ex)
            {
                // Если HearthDb недоступен в изолированном контексте, продолжаем с локальным словарем
                System.Diagnostics.Debug.WriteLine($"[CardDatabaseService] Ошибка маппинга HearthDb: {ex.Message}");
            }

            IsLoaded = true;
            }
        }

        public (OfficialCard card, bool isGolden) ResolveCard(string cardId, int? dbfId = null)
        {
            if (!IsLoaded) LoadDatabase();

            bool isGolden = false;

            // 1. Проверка по DbfId напрямую
            if (dbfId.HasValue && dbfId.Value > 0)
            {
                if (_cardsByDbfId.TryGetValue(dbfId.Value, out var cardByDbf))
                {
                    return (cardByDbf, false);
                }

                if (_baseCardsByGoldenUpgradeId.TryGetValue(dbfId.Value, out var baseCardByGoldenDbf))
                {
                    return (baseCardByGoldenDbf, true);
                }
            }

            if (string.IsNullOrWhiteSpace(cardId))
            {
                return (null, false);
            }

            // 2. Проверка золотого суффикса _G
            string searchCardId = cardId.Trim();
            if (searchCardId.EndsWith("_G", StringComparison.OrdinalIgnoreCase))
            {
                isGolden = true;
                searchCardId = searchCardId.Substring(0, searchCardId.Length - 2);
            }

            // 3. Поиск в кэше CardId
            if (_cardsByCardId.TryGetValue(searchCardId, out var mappedCard))
            {
                return (mappedCard, isGolden);
            }

            // 4. Поиск через HearthDb / HDT Database напрямую (с автоматическим синтезом метаданных для новых карт/токенов)
            try
            {
                if (Cards.All.TryGetValue(searchCardId, out var dbCard) && dbCard != null)
                {
                    if (_cardsByDbfId.TryGetValue(dbCard.DbfId, out var offCard))
                    {
                        _cardsByCardId[searchCardId] = offCard;
                        return (offCard, isGolden);
                    }
                    if (_baseCardsByGoldenUpgradeId.TryGetValue(dbCard.DbfId, out var baseFromGold))
                    {
                        _cardsByCardId[searchCardId] = baseFromGold;
                        return (baseFromGold, true);
                    }
                }

                var hdtCard = Hearthstone_Deck_Tracker.Hearthstone.Database.GetCardFromId(searchCardId);
                if (hdtCard != null && hdtCard.DbfId > 0 && hdtCard.Id != "UNKNOWN")
                {
                    if (_cardsByDbfId.TryGetValue(hdtCard.DbfId, out var existingOff))
                    {
                        _cardsByCardId[searchCardId] = existingOff;
                        return (existingOff, isGolden);
                    }

                    string ruTribe = !string.IsNullOrEmpty(hdtCard.Race)
                        ? GameStateReader.MapRaceStringToRu(hdtCard.Race) ?? "Без типа"
                        : "Без типа";

                    var synthesized = new OfficialCard
                    {
                        Id = hdtCard.DbfId,
                        Slug = searchCardId,
                        NameRu = !string.IsNullOrEmpty(hdtCard.LocalizedName) ? hdtCard.LocalizedName : hdtCard.Name,
                        NameEn = hdtCard.Name,
                        Tier = hdtCard.Cost > 0 ? hdtCard.Cost : 1,
                        CardTypeId = 4,
                        CardType = "Minion",
                        TribeRu = ruTribe,
                        TribeEn = hdtCard.Race ?? "Neutral",
                        Attack = hdtCard.Attack,
                        Health = hdtCard.Health,
                        TextRu = hdtCard.Text ?? "",
                        TextEn = hdtCard.EnglishText ?? ""
                    };

                    _cardsByDbfId[synthesized.Id] = synthesized;
                    _cardsByCardId[searchCardId] = synthesized;
                    _cardIdByDbfId[synthesized.Id] = searchCardId;
                    return (synthesized, isGolden);
                }
            }
            catch
            {
                // игнорируем
            }

            return (null, isGolden);
        }

        public OfficialCard GetByDbfId(int dbfId)
        {
            if (!IsLoaded) LoadDatabase();
            _cardsByDbfId.TryGetValue(dbfId, out var card);
            return card;
        }

        public OfficialCard GetById(string cardId)
        {
            if (string.IsNullOrWhiteSpace(cardId)) return null;
            return ResolveCard(cardId).card;
        }

        public OfficialCard GetCard(int? dbfId, string cardId)
        {
            return ResolveCard(cardId, dbfId).card;
        }

        public OfficialCard GetCard(int dbfId, string cardId)
        {
            return ResolveCard(cardId, dbfId > 0 ? (int?)dbfId : null).card;
        }

        public IEnumerable<OfficialCard> GetAllCards()
        {
            if (!IsLoaded) LoadDatabase();
            return _cardsByDbfId.Values;
        }
    }
}
