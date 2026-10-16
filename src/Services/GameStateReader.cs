using System;
using System.Collections.Generic;
using System.Linq;
using HearthDb.Enums;
using Hearthstone_Deck_Tracker.API;
using Hearthstone_Deck_Tracker.Hearthstone.Entities;
using HSCompsPlugin.Models;

namespace HSCompsPlugin.Services
{
    public enum SelectionOverlayMode
    {
        None = 0,
        HeroSelection = 1,
        TrinketSelection = 2
    }

    public class OfferedSelectionItem
    {
        public string CardId { get; set; }
        public int DbfId { get; set; }
        public int ZonePosition { get; set; }
    }

    public class SelectionOverlayState
    {
        public SelectionOverlayMode Mode { get; set; } = SelectionOverlayMode.None;
        public List<OfferedSelectionItem> Items { get; set; } = new List<OfferedSelectionItem>();
    }

    public class GameStateReader
    {
        private static PlayerGameStateSnapshot _lastRecruitSnapshot = new PlayerGameStateSnapshot();
        private static bool _hasValidRecruitSnapshot = false;

        private static List<string> _cachedLobbyTribes = null;
        private static int _lastSeenTurn = 0;
        private static bool _heroSelectionDismissed = false;
        private static int _dismissedTrinketMaxId = 0;
        private static int _activeTrinketBatchMinId = 0;
        private static int _activeTrinketBatchMaxId = 0;
        private static int _activeTrinketBatchTurn = -1;
        private static int _activeTrinketBatchInitialCount = 0;
        private static bool _hmConfirmedCurrentBatchVisible = false;

        // Кэшированные дескрипторы рефлексии для нулевых накладных расходов в рантайме
        private static bool _reflectionInitialized = false;
        private static System.Reflection.PropertyInfo _hmClientProp;
        private static System.Reflection.MethodInfo _hmGetRacesMethod;
        private static System.Reflection.PropertyInfo _bgDbCurrentProp;
        private static System.Reflection.PropertyInfo _bgDbRacesProp;

        public static void Reset()
        {
            try
            {
                _waitingTribesCts?.Cancel();
                _waitingTribesCts?.Dispose();
            }
            catch { }
            _waitingTribesCts = null;
            _lastRecruitSnapshot = new PlayerGameStateSnapshot();
            _hasValidRecruitSnapshot = false;
            _cachedLobbyTribes = null;
            _lastSeenTurn = 0;
            _heroSelectionDismissed = false;
            _dismissedTrinketMaxId = 0;
            _activeTrinketBatchMinId = 0;
            _activeTrinketBatchMaxId = 0;
            _activeTrinketBatchTurn = -1;
            _activeTrinketBatchInitialCount = 0;
            _hmConfirmedCurrentBatchVisible = false;
        }

        public static bool IsValidHeroCardId(string heroCardId)
        {
            if (string.IsNullOrWhiteSpace(heroCardId)) return false;
            if (heroCardId.IndexOf("KelThuzad", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (heroCardId.IndexOf("Enchant", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (heroCardId.IndexOf(" BaconShop8Player", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (heroCardId.EndsWith("_Buddy", StringComparison.OrdinalIgnoreCase) || heroCardId.EndsWith("_Buddy_G", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        public static bool IsInBattlegrounds
        {
            get
            {
                try
                {
                    if (Core.Game == null || Core.Game.IsInMenu) return false;
                    if (Core.Game.CurrentMode != Hearthstone_Deck_Tracker.Enums.Hearthstone.Mode.GAMEPLAY)
                        return false;
                    return Core.Game.IsBattlegroundsMatch
                           || Core.Game.CurrentGameMode == Hearthstone_Deck_Tracker.Enums.GameMode.Battlegrounds
                           || Core.Game.CurrentGameType == GameType.GT_BATTLEGROUNDS
                           || Core.Game.CurrentGameType == GameType.GT_BATTLEGROUNDS_DUO
                           || Core.Game.CurrentGameType == GameType.GT_BATTLEGROUNDS_FRIENDLY;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static bool IsInMainMenu
        {
            get
            {
                try
                {
                    if (Core.Game == null || IsInBattlegrounds) return false;
                    var mode = Core.Game.CurrentMode;
                    return Core.Game.IsInMenu
                           || mode == Hearthstone_Deck_Tracker.Enums.Hearthstone.Mode.HUB
                           || mode == Hearthstone_Deck_Tracker.Enums.Hearthstone.Mode.BACON
                           || mode == Hearthstone_Deck_Tracker.Enums.Hearthstone.Mode.BACON_COLLECTION
                           || mode == Hearthstone_Deck_Tracker.Enums.Hearthstone.Mode.GAME_MODE
                           || mode == Hearthstone_Deck_Tracker.Enums.Hearthstone.Mode.COLLECTIONMANAGER;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static PlayerGameStateSnapshot CaptureCurrentState()
        {
            if (Core.Game == null) return _lastRecruitSnapshot;

            int rawTurn = Math.Max(0, Core.Game.GetTurnNumber());
            if (rawTurn <= 1 && _lastSeenTurn > 1)
            {
                // Автоматический сброс состояния при старте нового матча
                Reset();
            }
            if (rawTurn > 0)
            {
                _lastSeenTurn = rawTurn;
            }

            // Во время фазы боя используем сохраненный снимок фазы вербовки,
            // чтобы смерти и спавны существ не разрушали анализ стола (включая 1-2 ход с пустым столом)
            if (Core.Game.IsBattlegroundsCombatPhase && _hasValidRecruitSnapshot && _lastRecruitSnapshot != null)
            {
                return _lastRecruitSnapshot;
            }

            var snapshot = new PlayerGameStateSnapshot
            {
                Turn = Math.Max(1, rawTurn)
            };

            var player = Core.Game.Player;
            var playerEntity = Core.Game.PlayerEntity;
            var heroEntity = player?.Hero ?? playerEntity;

            if (heroEntity != null)
            {
                snapshot.HeroCardId = heroEntity.CardId;
                int techLevel = heroEntity.GetTag(GameTag.PLAYER_TECH_LEVEL);
                if (techLevel <= 0 && playerEntity != null)
                {
                    techLevel = playerEntity.GetTag(GameTag.PLAYER_TECH_LEVEL);
                }
                snapshot.TavernTier = Math.Max(1, techLevel);

                int maxHp = heroEntity.GetTag(GameTag.HEALTH);
                int damage = heroEntity.GetTag(GameTag.DAMAGE);
                int armor = heroEntity.GetTag(GameTag.ARMOR);
                int effectiveHp = Math.Max(0, maxHp - damage) + Math.Max(0, armor);
                if (effectiveHp > 0)
                {
                    snapshot.HeroHealth = effectiveHp;
                }
            }

            // Определение доступных и забаненных фракций в текущем лобби
            DetermineLobbyTribes(snapshot);

            var cardService = CardDatabaseService.Instance;

            // 1. Считывание стола игрока
            if (player?.Board != null)
            {
                int pos = 0;
                foreach (var entity in player.Board.OrderBy(e => e.GetTag(GameTag.ZONE_POSITION)))
                {
                    if (entity == null || !entity.IsMinion) continue;
                    var (card, isGolden) = cardService.ResolveCard(entity.CardId, entity.Card?.DbfId);
                    int dbf = entity.Card?.DbfId ?? card?.Id ?? 0;
                    card = EnsureCardMetadata(card, entity, dbf);

                    snapshot.BoardMinions.Add(new BoardSlot
                    {
                        CardId = entity.CardId,
                        DbfId = dbf,
                        Card = card,
                        IsGolden = isGolden || entity.GetTag(GameTag.PREMIUM) == 1,
                        Attack = Math.Max(1, entity.GetTag(GameTag.ATK)),
                        Health = Math.Max(1, entity.GetTag(GameTag.HEALTH)),
                        HasTaunt = entity.HasTag(GameTag.TAUNT),
                        HasDivineShield = entity.HasTag(GameTag.DIVINE_SHIELD),
                        HasReborn = entity.HasTag(GameTag.REBORN),
                        HasPoisonous = entity.HasTag(GameTag.POISONOUS) || entity.HasTag(GameTag.VENOMOUS),
                        ZonePosition = pos++,
                        Status = SlotStatus.OnBoard
                    });
                }
            }

            // 2. Считывание руки игрока
            if (player?.Hand != null)
            {
                int handPos = 0;
                foreach (var entity in player.Hand)
                {
                    if (entity != null && entity.IsMinion)
                    {
                        var (card, isGolden) = cardService.ResolveCard(entity.CardId, entity.Card?.DbfId);
                        int dbf = entity.Card?.DbfId ?? card?.Id ?? 0;
                        card = EnsureCardMetadata(card, entity, dbf);

                        snapshot.HandMinions.Add(new BoardSlot
                        {
                            CardId = entity.CardId,
                            DbfId = dbf,
                            Card = card,
                            IsGolden = isGolden || entity.GetTag(GameTag.PREMIUM) == 1,
                            Attack = Math.Max(1, entity.GetTag(GameTag.ATK)),
                            Health = Math.Max(1, entity.GetTag(GameTag.HEALTH)),
                            ZonePosition = handPos++,
                            Status = SlotStatus.InHand
                        });
                    }
                }
            }

            // 3. Считывание активных тринкетов
            if (Core.Game.Entities != null && player != null)
            {
                foreach (var entity in Core.Game.Entities.Values)
                {
                    if (entity != null && entity.IsControlledBy(player.Id) && entity.IsInPlay &&
                        entity.GetTag(GameTag.CARDTYPE) != (int)CardType.ENCHANTMENT)
                    {
                        if (!string.IsNullOrEmpty(entity.CardId) &&
                            (IsTrinketCardId(entity.CardId, entity.Card?.DbfId ?? 0) || MetaStatsService.Instance.IsTrinket(entity.CardId, entity.Card?.DbfId ?? 0)))
                        {
                            snapshot.ActiveTrinkets.Add(entity.CardId);
                        }
                    }
                }
            }

            // Сохраняем снимок фазы вербовки
            if (!Core.Game.IsBattlegroundsCombatPhase)
            {
                _lastRecruitSnapshot = snapshot;
                _hasValidRecruitSnapshot = true;
            }

            return snapshot;
        }

        private static OfficialCard EnsureCardMetadata(OfficialCard card, Entity entity, int dbf)
        {
            if (card != null && card.TribesList != null && card.TribesList.Count > 0)
                return card;

            var race1 = MapRaceToRu((Race)entity.GetTag(GameTag.CARDRACE));
            var tribes = new List<string>();
            if (!string.IsNullOrEmpty(race1)) tribes.Add(race1);

            if (entity.Card != null && !string.IsNullOrEmpty(entity.Card.Race) && entity.Card.Race != "None")
            {
                if (!tribes.Contains(entity.Card.Race))
                    tribes.Add(entity.Card.Race);
            }

            if (card != null)
            {
                if (tribes.Count > 0 && (string.IsNullOrEmpty(card.TribeRu) || card.TribeRu == "Без типа"))
                {
                    card.TribeRu = string.Join(" / ", tribes);
                }
                return card;
            }

            int techLevel = entity.GetTag(GameTag.TECH_LEVEL);
            if (techLevel <= 0 && entity.Card != null && entity.Card.Cost > 0)
            {
                techLevel = entity.Card.Cost;
            }

            return new OfficialCard
            {
                Id = dbf,
                NameRu = entity.Card?.Name ?? entity.CardId,
                NameEn = entity.Card?.EnglishText,
                Tier = techLevel > 0 ? techLevel : 1,
                TribeRu = tribes.Count > 0 ? string.Join(" / ", tribes) : "Без типа",
                CardType = "Minion",
                Attack = entity.GetTag(GameTag.ATK),
                Health = entity.GetTag(GameTag.HEALTH)
            };
        }

        public static readonly List<string> AllBgTribes = new List<string>
        {
            "Механизм", "Зверь", "Мурлок", "Нежить", "Демон",
            "Элементаль", "Пират", "Дракон", "Свинобраз", "Абберация"
        };

        public static event Action<List<string>> LobbyTribesUpdated;
        private static bool _hookedDbUpdated = false;
        private static bool _hookedMinionsVm = false;
        private static System.Threading.CancellationTokenSource _waitingTribesCts = null;

        static GameStateReader()
        {
            try
            {
                HookBattlegroundsDbUpdated();
                HookMinionsVmPropertyChanged();
            }
            catch
            {
                // Headless or test environment without HDT Core / WPF
            }
        }

        public static void HookMinionsVmPropertyChanged()
        {
            if (_hookedMinionsVm) return;
            try
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    if (_hookedMinionsVm) return;
                    var vm = Core.OverlayWindow?.BattlegroundsMinionsVM;
                    if (vm != null)
                    {
                        vm.PropertyChanged += (s, e) =>
                        {
                            if (e.PropertyName == "AvailableRaces")
                            {
                                var available = ParseRaces(vm.AvailableRaces);
                                if (available.Count >= 3 && available.Count <= 7)
                                {
                                    ApplyDetectedTribes(available, null);
                                }
                            }
                        };
                        _hookedMinionsVm = true;

                        if (vm.AvailableRaces != null)
                        {
                            var available = ParseRaces(vm.AvailableRaces);
                            if (available.Count >= 3 && available.Count <= 7)
                            {
                                ApplyDetectedTribes(available, null);
                            }
                        }
                    }
                });
            }
            catch { }
        }

        private static void HookBattlegroundsDbUpdated()
        {
            if (_hookedDbUpdated) return;
            try
            {
                var hdtAssembly = typeof(Hearthstone_Deck_Tracker.API.Core).Assembly;
                var singletonType = hdtAssembly.GetType("Hearthstone_Deck_Tracker.Utility.Battlegrounds.BattlegroundsDbSingleton");
                var updatedEvent = singletonType?.GetEvent("Updated");
                if (updatedEvent != null)
                {
                    Action<bool> onUpdated = (isDuos) =>
                    {
                        _cachedLobbyTribes = null;
                        var dummy = new PlayerGameStateSnapshot();
                        DetermineLobbyTribes(dummy);
                        if (dummy.AvailableTribes != null && dummy.AvailableTribes.Count >= 3 && dummy.AvailableTribes.Count <= 7)
                        {
                            ApplyDetectedTribes(dummy.AvailableTribes, dummy);
                        }
                    };
                    updatedEvent.AddEventHandler(null, onUpdated);
                    _hookedDbUpdated = true;
                }
            }
            catch { }
        }

        public static void StartWaitingForTribes()
        {
            _cachedLobbyTribes = null;
            try
            {
                _waitingTribesCts?.Cancel();
                _waitingTribesCts?.Dispose();
            }
            catch { }
            _waitingTribesCts = new System.Threading.CancellationTokenSource();
            var token = _waitingTribesCts.Token;

            HookMinionsVmPropertyChanged();

            var immediate = TryReadFromOverlayVm() ?? TryReadFromPinningVm();
            if (immediate != null && immediate.Count >= 3 && immediate.Count <= 7)
            {
                ApplyDetectedTribes(immediate, null);
                return;
            }

            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var waitTask = Hearthstone_Deck_Tracker.Hearthstone.BattlegroundsUtils.WaitForAvailableRaces();
                    if (waitTask != null)
                    {
                        var races = await waitTask.ConfigureAwait(false);
                        if (token.IsCancellationRequested) return;
                        if (races != null)
                        {
                            var parsed = ParseRaces(races);
                            if (parsed.Count >= 3 && parsed.Count <= 7)
                            {
                                ApplyDetectedTribes(parsed, null);
                                return;
                            }
                        }
                    }
                }
                catch { }

                for (int i = 0; i < 75; i++)
                {
                    if (token.IsCancellationRequested) return;
                    await System.Threading.Tasks.Task.Delay(400, token).ConfigureAwait(false);
                    if (token.IsCancellationRequested) return;

                    if (_cachedLobbyTribes != null && _cachedLobbyTribes.Count >= 3 && _cachedLobbyTribes.Count <= 7)
                        return;

                    var tribes = TryReadFromOverlayVm() ?? TryReadFromPinningVm() ?? TryReadFromHearthMirror();
                    if (tribes != null && tribes.Count >= 3 && tribes.Count <= 7)
                    {
                        ApplyDetectedTribes(tribes, null);
                        return;
                    }
                }
            }, token);
        }

        public static List<string> TryReadFromOverlayVm()
        {
            try
            {
                List<string> result = null;
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.Invoke(() =>
                    {
                        result = TryReadFromOverlayVmInternal();
                    });
                    return result;
                }
                return TryReadFromOverlayVmInternal();
            }
            catch { }
            return null;
        }

        private static List<string> TryReadFromOverlayVmInternal()
        {
            try
            {
                var vm = Core.OverlayWindow?.BattlegroundsMinionsVM;
                if (vm != null && vm.AvailableRaces != null)
                {
                    var parsed = ParseRaces(vm.AvailableRaces);
                    if (parsed.Count >= 3 && parsed.Count <= 7)
                        return parsed;
                }
            }
            catch { }
            return null;
        }

        public static List<string> TryReadFromPinningVm()
        {
            try
            {
                List<string> result = null;
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.Invoke(() =>
                    {
                        result = TryReadFromPinningVmInternal();
                    });
                    return result;
                }
                return TryReadFromPinningVmInternal();
            }
            catch { }
            return null;
        }

        private static List<string> TryReadFromPinningVmInternal()
        {
            try
            {
                var pinVm = Core.OverlayWindow?.BattlegroundsMinionPinningViewModel;
                if (pinVm != null && pinVm.AvailableRaces != null)
                {
                    var parsed = ParseRaces(pinVm.AvailableRaces);
                    if (parsed.Count >= 3 && parsed.Count <= 7)
                        return parsed;
                }
            }
            catch { }
            return null;
        }

        private static void EnsureReflectionCached()
        {
            if (_reflectionInitialized) return;
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (string.Equals(asm.GetName().Name, "HearthMirror", StringComparison.OrdinalIgnoreCase))
                    {
                        var refType = asm.GetType("HearthMirror.Reflection");
                        _hmClientProp = refType?.GetProperty("Client", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        break;
                    }
                }

                var hdtAssembly = typeof(Core).Assembly;
                var singletonType = hdtAssembly.GetType("Hearthstone_Deck_Tracker.Utility.Battlegrounds.BattlegroundsDbSingleton");
                _bgDbCurrentProp = singletonType?.GetProperty("Current");
                if (_hmClientProp != null || _bgDbCurrentProp != null)
                {
                    _reflectionInitialized = true;
                }
            }
            catch { }
        }

        public static List<string> TryReadFromHearthMirror()
        {
            try
            {
                EnsureReflectionCached();
                var client = _hmClientProp?.GetValue(null);
                if (client != null)
                {
                    if (_hmGetRacesMethod == null)
                    {
                        _hmGetRacesMethod = client.GetType().GetMethod("GetAvailableBattlegroundsRaces");
                    }
                    var listObj = _hmGetRacesMethod?.Invoke(client, null);
                    if (listObj is System.Collections.IEnumerable enumerable)
                    {
                        var parsed = ParseRaces(enumerable);
                        if (parsed.Count >= 3 && parsed.Count <= 7)
                            return parsed;
                    }
                }
            }
            catch { }
            return null;
        }

        public static void ApplyDetectedTribes(List<string> tribes, PlayerGameStateSnapshot snapshot)
        {
            if (tribes == null || tribes.Count < 3 || tribes.Count > 7) return;

            bool isNew = _cachedLobbyTribes == null || !_cachedLobbyTribes.SequenceEqual(tribes);
            _cachedLobbyTribes = new List<string>(tribes);

            if (snapshot != null)
            {
                snapshot.AvailableTribes = new List<string>(tribes);
                snapshot.BannedTribes = AllBgTribes.Except(snapshot.AvailableTribes).ToList();
            }

            if (isNew)
            {
                try
                {
                    LobbyTribesUpdated?.Invoke(_cachedLobbyTribes);
                }
                catch { }
            }
        }

        private static void DetermineLobbyTribes(PlayerGameStateSnapshot snapshot)
        {
            if (_cachedLobbyTribes != null && _cachedLobbyTribes.Count >= 3 && _cachedLobbyTribes.Count <= 7 && snapshot.Turn >= 3)
            {
                snapshot.AvailableTribes = new List<string>(_cachedLobbyTribes);
                snapshot.BannedTribes = AllBgTribes.Except(snapshot.AvailableTribes).ToList();
                return;
            }

            // Layer 1: Direct HearthMirror (самый свежий источник из памяти клиента на муллигане)
            var tribes = TryReadFromHearthMirror();

            // Layer 2: Overlay VM
            if (tribes == null)
            {
                tribes = TryReadFromOverlayVm();
            }

            // Layer 3: Pinning VM
            if (tribes == null)
            {
                tribes = TryReadFromPinningVm();
            }

            // Layer 4: BattlegroundsUtils.GetAvailableRaces()
            if (tribes == null)
            {
                try
                {
                    var bgRaces = Hearthstone_Deck_Tracker.Hearthstone.BattlegroundsUtils.GetAvailableRaces();
                    if (bgRaces != null)
                    {
                        var parsed = ParseRaces(bgRaces);
                        if (parsed.Count >= 3 && parsed.Count <= 7)
                            tribes = parsed;
                    }
                }
                catch { }
            }

            // Layer 5: BattlegroundsDbSingleton.Current.Races (если там валидный срез 3..7)
            if (tribes == null)
            {
                try
                {
                    EnsureReflectionCached();
                    var currentDb = _bgDbCurrentProp?.GetValue(null);
                    if (currentDb != null)
                    {
                        if (_bgDbRacesProp == null)
                        {
                            _bgDbRacesProp = currentDb.GetType().GetProperty("Races");
                        }
                        if (_bgDbRacesProp?.GetValue(currentDb) is System.Collections.IEnumerable racesEnum)
                        {
                            var parsed = ParseRaces(racesEnum);
                            if (parsed.Count >= 3 && parsed.Count <= 7)
                                tribes = parsed;
                        }
                    }
                }
                catch { }
            }

            if (tribes != null && tribes.Count >= 3 && tribes.Count <= 7)
            {
                ApplyDetectedTribes(tribes, snapshot);
                return;
            }

            if (snapshot.AvailableTribes == null || snapshot.AvailableTribes.Count == 0)
            {
                snapshot.AvailableTribes = (_cachedLobbyTribes != null && _cachedLobbyTribes.Count <= 7) ? _cachedLobbyTribes : new List<string>();
                snapshot.BannedTribes = AllBgTribes.Except(snapshot.AvailableTribes).ToList();
            }
        }

        public static List<string> ParseRaces(System.Collections.IEnumerable racesEnum)
        {
            var available = new List<string>();
            if (racesEnum == null) return available;
            foreach (var raceObj in racesEnum)
            {
                if (raceObj == null) continue;
                string ruTribe = MapRaceObjectToRu(raceObj);
                if (!string.IsNullOrEmpty(ruTribe) && !available.Contains(ruTribe))
                {
                    available.Add(ruTribe);
                }
            }
            return available;
        }

        public static string MapRaceObjectToRu(object raceObj)
        {
            if (raceObj == null) return null;
            if (raceObj is Race race)
            {
                return MapRaceToRu(race);
            }
            if (raceObj is int intVal)
            {
                return MapRaceToRu((Race)intVal);
            }
            string str = raceObj.ToString();
            if (int.TryParse(str, out int parsedInt))
            {
                return MapRaceToRu((Race)parsedInt);
            }
            return MapRaceStringToRu(str);
        }

        public static string MapRaceToRu(Race race)
        {
            switch (race)
            {
                case Race.MECHANICAL: return "Механизм";
                case Race.BEAST: return "Зверь";
                case Race.MURLOC: return "Мурлок";
                case Race.UNDEAD: return "Нежить";
                case Race.DEMON: return "Демон";
                case Race.ELEMENTAL: return "Элементаль";
                case Race.NAGA: return "Нага";
                case Race.PIRATE: return "Пират";
                case Race.DRAGON: return "Дракон";
                case Race.QUILBOAR: return "Свинобраз";
                case Race.ABERRATION: return "Абберация";
                default: return null;
            }
        }

        public static string MapRaceStringToRu(string raceStr)
        {
            if (string.IsNullOrEmpty(raceStr)) return null;
            string upper = raceStr.Trim().ToUpperInvariant();
            switch (upper)
            {
                case "MECHANICAL":
                case "MECH": return "Механизм";
                case "BEAST": return "Зверь";
                case "MURLOC": return "Мурлок";
                case "UNDEAD": return "Нежить";
                case "DEMON": return "Демон";
                case "ELEMENTAL": return "Элементаль";
                case "NAGA": return "Нага";
                case "ABERRATION": return "Абберация";
                case "PIRATE": return "Пират";
                case "DRAGON": return "Дракон";
                case "QUILBOAR": return "Свинобраз";
                default: return null;
            }
        }
  
        private static System.Reflection.MethodInfo _hmStaticGetCardChoicesMethod;
        private static System.Reflection.MethodInfo _hmClientGetCardChoicesMethod;
        private static bool _hmChoicesInitialized = false;

        public static bool IsHeroCardId(string cardId, int dbfId = 0)
        {
            if (!IsValidHeroCardId(cardId)) return false;
            if (string.Equals(cardId, "TB_BaconShop_HERO_PH", StringComparison.OrdinalIgnoreCase)) return false;
            if (cardId.IndexOf("_HERO_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                cardId.StartsWith("TB_BaconShop_HERO_", StringComparison.OrdinalIgnoreCase))
            {
                if (cardId.EndsWith("p", StringComparison.OrdinalIgnoreCase) ||
                    cardId.IndexOf("_HP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    cardId.IndexOf("_Buddy", StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
                return true;
            }
            var off = CardDatabaseService.Instance.GetCard(dbfId, cardId);
            if (off != null && string.Equals(off.CardType, "Hero", StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        private static readonly System.Text.RegularExpressions.Regex EnchantmentSuffixRegex =
            new System.Text.RegularExpressions.Regex(@"e\d*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        public static bool IsTrinketCardId(string cardId, int dbfId = 0)
        {
            if (string.IsNullOrWhiteSpace(cardId) && dbfId <= 0) return false;
            if (!string.IsNullOrEmpty(cardId))
            {
                string clean = cardId.Trim();
                if (clean.IndexOf("_HERO_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    clean.IndexOf("_HP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    clean.IndexOf("_Buddy", StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;

                // Отсекаем только чары (e, e1, e2...), но НЕ отсекаем суффикс t/t2/t3,
                // так как Большие тринкеты 9 хода в Hearthstone имеют ID вида BG30_MagicItem_XXXt!
                if (EnchantmentSuffixRegex.IsMatch(clean))
                    return false;

                if (clean.IndexOf("MagicItem", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    clean.IndexOf("Trinket", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            var off = CardDatabaseService.Instance.GetCard(dbfId, cardId);
            if (off != null && (off.CardTypeId == 40 || off.CardTypeId == 42 || off.CardTypeId == 44 ||
                string.Equals(off.CardType, "BattlegroundTrinket", StringComparison.OrdinalIgnoreCase)))
                return true;
            return false;
        }

        private static object InvokeGetCardChoicesMethod(System.Reflection.MethodInfo method, object instance)
        {
            if (method == null) return null;
            var prms = method.GetParameters();
            if (prms.Length == 0)
                return method.Invoke(instance, null);
            object[] args = new object[prms.Length];
            for (int i = 0; i < prms.Length; i++)
            {
                args[i] = prms[i].HasDefaultValue
                    ? prms[i].DefaultValue
                    : (prms[i].ParameterType.IsValueType ? Activator.CreateInstance(prms[i].ParameterType) : null);
            }
            return method.Invoke(instance, args);
        }

        private static (bool hmApiAvailable, bool isVisible, List<string> cardIds) TryGetHearthMirrorCardChoices()
        {
            try
            {
                EnsureReflectionCached();
                if (!_hmChoicesInitialized)
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (string.Equals(asm.GetName().Name, "HearthMirror", StringComparison.OrdinalIgnoreCase))
                        {
                            var refType = asm.GetType("HearthMirror.Reflection");
                            if (refType != null)
                            {
                                _hmStaticGetCardChoicesMethod = refType.GetMethod("GetCardChoices", System.Type.EmptyTypes)
                                    ?? refType.GetMethods().FirstOrDefault(m => m.Name == "GetCardChoices");
                            }
                            break;
                        }
                    }
                    var clientObj = _hmClientProp?.GetValue(null);
                    if (clientObj != null)
                    {
                        var cType = clientObj.GetType();
                        _hmClientGetCardChoicesMethod = cType.GetMethod("GetCardChoices", System.Type.EmptyTypes)
                            ?? cType.GetMethods().FirstOrDefault(m => m.Name == "GetCardChoices");
                    }
                    if (_hmStaticGetCardChoicesMethod != null || _hmClientGetCardChoicesMethod != null)
                    {
                        _hmChoicesInitialized = true;
                    }
                }

                object clientInstance = _hmClientProp?.GetValue(null);
                bool apiAvailable = _hmStaticGetCardChoicesMethod != null || (_hmClientGetCardChoicesMethod != null && clientInstance != null);
                if (!apiAvailable)
                {
                    return (false, false, null);
                }

                object choicesObj = null;
                if (_hmStaticGetCardChoicesMethod != null)
                {
                    choicesObj = InvokeGetCardChoicesMethod(_hmStaticGetCardChoicesMethod, null);
                }
                if (choicesObj == null && _hmClientGetCardChoicesMethod != null && clientInstance != null)
                {
                    choicesObj = InvokeGetCardChoicesMethod(_hmClientGetCardChoicesMethod, clientInstance);
                }

                if (choicesObj == null)
                {
                    return (true, false, null);
                }

                var type = choicesObj.GetType();
                var isVisibleProp = type.GetProperty("IsVisible");
                bool isVisible = true;
                if (isVisibleProp != null && isVisibleProp.GetValue(choicesObj) is bool bVis)
                {
                    isVisible = bVis;
                }

                if (!isVisible)
                {
                    return (true, false, null);
                }

                var cardsProp = type.GetProperty("Cards");
                if (cardsProp?.GetValue(choicesObj) is System.Collections.IEnumerable cardsEnum)
                {
                    var list = new List<string>();
                    foreach (var item in cardsEnum)
                    {
                        if (item is string s && !string.IsNullOrWhiteSpace(s))
                        {
                            list.Add(s.Trim());
                        }
                    }
                    return (true, list.Count > 0, list);
                }

                return (true, false, null);
            }
            catch
            {
                return (false, false, null);
            }
        }

        public static SelectionOverlayState GetActiveSelectionState()
        {
            var result = new SelectionOverlayState { Mode = SelectionOverlayMode.None };
            try
            {
                // Жесткая блокировка вне активного боя Полей сражений (исключает показ в главном меню и лобби)
                if (!IsInBattlegrounds || Core.Game == null || Core.Game.IsInMenu ||
                    Core.Game.CurrentMode != Hearthstone_Deck_Tracker.Enums.Hearthstone.Mode.GAMEPLAY)
                {
                    return result;
                }

                int turn = Math.Max(0, Core.Game.GetTurnNumber());
                if (turn <= 1 && _lastSeenTurn > 1)
                {
                    Reset();
                }
                if (turn > 0)
                {
                    _lastSeenTurn = turn;
                }

                // В фазе боя окно выбора всегда закрыто; любой активный пакет тринкетов с вербовки помечается завершенным
                if (Core.Game.IsBattlegroundsCombatPhase)
                {
                    if (_activeTrinketBatchMaxId > 0)
                    {
                        _dismissedTrinketMaxId = Math.Max(_dismissedTrinketMaxId, _activeTrinketBatchMaxId);
                        _activeTrinketBatchMinId = 0;
                        _activeTrinketBatchMaxId = 0;
                        _activeTrinketBatchInitialCount = 0;
                        _hmConfirmedCurrentBatchVisible = false;
                    }
                    return result;
                }

                // При переходе на следующий ход автоматически закрываем пакет тринкетов прошлого хода
                if (_activeTrinketBatchTurn > 0 && turn != _activeTrinketBatchTurn && _activeTrinketBatchMaxId > 0)
                {
                    _dismissedTrinketMaxId = Math.Max(_dismissedTrinketMaxId, _activeTrinketBatchMaxId);
                    _activeTrinketBatchMinId = 0;
                    _activeTrinketBatchMaxId = 0;
                    _activeTrinketBatchTurn = -1;
                    _activeTrinketBatchInitialCount = 0;
                    _hmConfirmedCurrentBatchVisible = false;
                }

                var player = Core.Game.Player;
                var playerEntity = Core.Game.PlayerEntity;
                if (player == null || Core.Game.Entities == null)
                    return result;

                var playHero = player.Hero;
                bool mulliganDone = playerEntity != null && playerEntity.GetTag(GameTag.MULLIGAN_STATE) == (int)Mulligan.DONE;
                bool hasChosenRealHeroInPlay = playHero != null &&
                                               playHero.IsInPlay &&
                                               IsHeroCardId(playHero.CardId, playHero.Card?.DbfId ?? 0);
                if (mulliganDone || hasChosenRealHeroInPlay || turn >= 2)
                {
                    _heroSelectionDismissed = true;
                }

                var (_, hmVisible, hmCards) = TryGetHearthMirrorCardChoices();
                if (_hmConfirmedCurrentBatchVisible && !hmVisible)
                {
                    // Если HearthMirror подтверждал открытие текущего окна выбора и теперь сообщил о его закрытии — мгновенно скрываем
                    if (turn <= 1)
                    {
                        _heroSelectionDismissed = true;
                    }
                    if (_activeTrinketBatchMaxId > 0)
                    {
                        _dismissedTrinketMaxId = Math.Max(_dismissedTrinketMaxId, _activeTrinketBatchMaxId);
                        _activeTrinketBatchMinId = 0;
                        _activeTrinketBatchMaxId = 0;
                        _activeTrinketBatchInitialCount = 0;
                    }
                    _hmConfirmedCurrentBatchVisible = false;
                    return result;
                }

                // 1. Выбор Героя на муллигане (Ход 0..1 до завершения муллигана)
                if (!_heroSelectionDismissed && turn <= 1)
                {
                    var offeredHeroes = new List<OfferedSelectionItem>();
                    var seenHeroIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    if (hmVisible && hmCards != null && hmCards.Count >= 2)
                    {
                        int pos = 0;
                        foreach (var cid in hmCards)
                        {
                            var (off, _) = CardDatabaseService.Instance.ResolveCard(cid, null);
                            int dbf = off?.Id ?? 0;
                            if (IsHeroCardId(cid, dbf) || MetaStatsService.Instance.IsHero(cid, dbf))
                            {
                                string norm = MetaStatsService.NormalizeHeroCardId(cid);
                                if (seenHeroIds.Add(norm))
                                {
                                    offeredHeroes.Add(new OfferedSelectionItem { CardId = cid, DbfId = dbf, ZonePosition = pos++ });
                                }
                            }
                        }
                        if (offeredHeroes.Count >= 2)
                        {
                            _hmConfirmedCurrentBatchVisible = true;
                            result.Mode = SelectionOverlayMode.HeroSelection;
                            result.Items = offeredHeroes.Take(4).ToList();
                            return result;
                        }
                    }

                    foreach (var e in Core.Game.Entities.Values
                        .Where(x => x != null && x.IsControlledBy(player.Id) && (x.IsInSetAside || x.IsInHand) && !x.IsInPlay)
                        .OrderBy(x => x.GetTag(GameTag.ZONE_POSITION) > 0 ? x.GetTag(GameTag.ZONE_POSITION) : 99)
                        .ThenBy(x => x.Id))
                    {
                        int dbf = e.Card?.DbfId ?? 0;
                        if (!string.IsNullOrEmpty(e.CardId) && IsHeroCardId(e.CardId, dbf))
                        {
                            string norm = MetaStatsService.NormalizeHeroCardId(e.CardId);
                            if (seenHeroIds.Add(norm))
                            {
                                offeredHeroes.Add(new OfferedSelectionItem
                                {
                                    CardId = e.CardId,
                                    DbfId = dbf,
                                    ZonePosition = offeredHeroes.Count
                                });
                            }
                        }
                    }

                    if (offeredHeroes.Count >= 2 && offeredHeroes.Count <= 4)
                    {
                        result.Mode = SelectionOverlayMode.HeroSelection;
                        result.Items = offeredHeroes;
                        return result;
                    }
                }

                // 2. Выбор Тринкетов (Малый тринкет на 6 ходу / Марин на 5 ходу, Большой тринкет на 9 ходу)
                if (turn >= 5)
                {
                    var allCandidateTrinkets = Core.Game.Entities.Values
                        .Where(x => x != null &&
                                    x.IsControlledBy(player.Id) &&
                                    x.Id > _dismissedTrinketMaxId &&
                                    (x.IsInSetAside || x.IsInHand) &&
                                    !x.IsInPlay &&
                                    x.GetTag(GameTag.CARDTYPE) != (int)CardType.ENCHANTMENT &&
                                    x.GetTag(GameTag.ZONE_POSITION) >= 1 &&
                                    x.GetTag(GameTag.ZONE_POSITION) <= 4 &&
                                    !string.IsNullOrEmpty(x.CardId) &&
                                    (IsTrinketCardId(x.CardId, x.Card?.DbfId ?? 0) || MetaStatsService.Instance.IsTrinket(x.CardId, x.Card?.DbfId ?? 0)))
                        .ToList();

                    if (allCandidateTrinkets.Count >= 2)
                    {
                        // Изолируем строго последний пакет выбора (отсекая невыбранные тринкеты 6 хода в SETASIDE при наступлении 9 хода!)
                        int maxCandId = allCandidateTrinkets.Max(x => x.Id);
                        var currentBatch = allCandidateTrinkets
                            .Where(x => x.Id >= maxCandId - 12)
                            .OrderBy(x => x.GetTag(GameTag.ZONE_POSITION))
                            .ThenBy(x => x.Id)
                            .ToList();

                        if (currentBatch.Count >= 2)
                        {
                            int batchMinId = currentBatch.Min(x => x.Id);
                            int batchMaxId = currentBatch.Max(x => x.Id);

                            if (batchMaxId > _activeTrinketBatchMaxId)
                            {
                                _activeTrinketBatchMinId = batchMinId;
                                _activeTrinketBatchMaxId = batchMaxId;
                                _activeTrinketBatchTurn = turn;
                                _activeTrinketBatchInitialCount = currentBatch.Count;
                                _hmConfirmedCurrentBatchVisible = false;
                            }
                            else
                            {
                                if (batchMinId < _activeTrinketBatchMinId && _activeTrinketBatchMinId > 0)
                                    _activeTrinketBatchMinId = batchMinId;
                                if (currentBatch.Count > _activeTrinketBatchInitialCount)
                                    _activeTrinketBatchInitialCount = currentBatch.Count;
                            }

                            // Проверяем, был ли уже выбран тринкет из этого пакета (перешел в PLAY / GRAVEYARD / REMOVEDFROMGAME или количество карт в SETASIDE уменьшилось)
                            bool anyResolvedFromBatch = Core.Game.Entities.Values.Any(x =>
                                x != null &&
                                x.IsControlledBy(player.Id) &&
                                x.Id >= _activeTrinketBatchMinId &&
                                (x.IsInPlay || x.IsInGraveyard || x.IsInSecret || x.GetTag(GameTag.ZONE) == (int)Zone.REMOVEDFROMGAME) &&
                                x.GetTag(GameTag.CARDTYPE) != (int)CardType.ENCHANTMENT &&
                                !string.IsNullOrEmpty(x.CardId) &&
                                (IsTrinketCardId(x.CardId, x.Card?.DbfId ?? 0) || MetaStatsService.Instance.IsTrinket(x.CardId, x.Card?.DbfId ?? 0)));

                            bool batchCountDropped = _activeTrinketBatchInitialCount >= 3 && currentBatch.Count < _activeTrinketBatchInitialCount;

                            if (anyResolvedFromBatch || batchCountDropped)
                            {
                                _dismissedTrinketMaxId = Math.Max(_dismissedTrinketMaxId, _activeTrinketBatchMaxId);
                                _activeTrinketBatchMinId = 0;
                                _activeTrinketBatchMaxId = 0;
                                _activeTrinketBatchInitialCount = 0;
                                _hmConfirmedCurrentBatchVisible = false;
                                return result;
                            }

                            if (hmVisible && hmCards != null && hmCards.Count >= 2)
                            {
                                _hmConfirmedCurrentBatchVisible = true;
                            }

                            var offeredTrinkets = new List<OfferedSelectionItem>();
                            var seenTrinkets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var e in currentBatch)
                            {
                                if (seenTrinkets.Add(e.CardId))
                                {
                                    offeredTrinkets.Add(new OfferedSelectionItem
                                    {
                                        CardId = e.CardId,
                                        DbfId = e.Card?.DbfId ?? 0,
                                        ZonePosition = e.GetTag(GameTag.ZONE_POSITION)
                                    });
                                }
                            }

                            if (offeredTrinkets.Count >= 2 && offeredTrinkets.Count <= 4)
                            {
                                result.Mode = SelectionOverlayMode.TrinketSelection;
                                result.Items = offeredTrinkets;
                                return result;
                            }
                        }
                    }
                }
            }
            catch { }
            return result;
        }
    }
}
