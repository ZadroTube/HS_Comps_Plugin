using System;
using System.Collections.Generic;
using System.Linq;
using HearthDb.Enums;
using Hearthstone_Deck_Tracker.API;
using Hearthstone_Deck_Tracker.Hearthstone.Entities;
using HSCompsPlugin.Models;

namespace HSCompsPlugin.Services
{
    public class GameStateReader
    {
        private static PlayerGameStateSnapshot _lastRecruitSnapshot = new PlayerGameStateSnapshot();

        private static List<string> _cachedLobbyTribes = null;

        public static void Reset()
        {
            _waitingTribesCts?.Cancel();
            _waitingTribesCts = null;
            _lastRecruitSnapshot = new PlayerGameStateSnapshot();
            _cachedLobbyTribes = null;
        }

        public static bool IsInBattlegrounds
        {
            get
            {
                try
                {
                    if (Core.Game == null) return false;
                    return Core.Game.IsBattlegroundsMatch
                           || Core.Game.CurrentGameMode == Hearthstone_Deck_Tracker.Enums.GameMode.Battlegrounds
                           || Core.Game.CurrentMode == Hearthstone_Deck_Tracker.Enums.Hearthstone.Mode.BACON
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
                    return mode == Hearthstone_Deck_Tracker.Enums.Hearthstone.Mode.HUB
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

            // Во время фазы боя используем сохраненный снимок фазы вербовки,
            // чтобы смерти и спавны существ не разрушали анализ стола
            if (Core.Game.IsBattlegroundsCombatPhase && _lastRecruitSnapshot != null && _lastRecruitSnapshot.BoardMinions.Count > 0)
            {
                return _lastRecruitSnapshot;
            }

            var snapshot = new PlayerGameStateSnapshot
            {
                Turn = Math.Max(1, Core.Game.GetTurnNumber())
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
                    if (entity == null) continue;
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

            // 3. Считывание таверны Боба (замороженные и доступные карты)
            var bob = Core.Game.Opponent;
            if (bob?.Board != null)
            {
                int bobPos = 0;
                foreach (var entity in bob.Board)
                {
                    if (entity != null && entity.IsMinion)
                    {
                        var (card, isGolden) = cardService.ResolveCard(entity.CardId, entity.Card?.DbfId);
                        int dbf = entity.Card?.DbfId ?? card?.Id ?? 0;
                        bool isFrozen = entity.HasTag(GameTag.FROZEN);

                        if (isFrozen)
                        {
                            card = EnsureCardMetadata(card, entity, dbf);
                            snapshot.FrozenTavernMinions.Add(new BoardSlot
                            {
                                CardId = entity.CardId,
                                DbfId = dbf,
                                Card = card,
                                IsGolden = isGolden,
                                ZonePosition = bobPos++,
                                Status = SlotStatus.FrozenInTavern
                            });
                        }
                    }
                }
            }

            // 4. Считывание активных тринкетов
            if (Core.Game.Entities != null && player != null)
            {
                foreach (var entity in Core.Game.Entities.Values)
                {
                    if (entity.IsControlledBy(player.Id) && entity.IsInPlay)
                    {
                        if (entity.CardId != null && entity.CardId.Contains("MagicItem"))
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
            "Элементаль", "Нага", "Пират", "Дракон", "Свинобраз", "Абберация"
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
            _waitingTribesCts?.Cancel();
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

        public static List<string> TryReadFromHearthMirror()
        {
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (string.Equals(asm.GetName().Name, "HearthMirror", StringComparison.OrdinalIgnoreCase))
                    {
                        var refType = asm.GetType("HearthMirror.Reflection");
                        var clientProp = refType?.GetProperty("Client", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        var client = clientProp?.GetValue(null);
                        if (client != null)
                        {
                            var getRacesMethod = client.GetType().GetMethod("GetAvailableBattlegroundsRaces");
                            var listObj = getRacesMethod?.Invoke(client, null);
                            if (listObj is System.Collections.IEnumerable enumerable)
                            {
                                var parsed = ParseRaces(enumerable);
                                if (parsed.Count >= 3 && parsed.Count <= 7)
                                    return parsed;
                            }
                        }
                        break;
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
            if (_cachedLobbyTribes != null && _cachedLobbyTribes.Count >= 3 && _cachedLobbyTribes.Count <= 7)
            {
                snapshot.AvailableTribes = new List<string>(_cachedLobbyTribes);
                snapshot.BannedTribes = AllBgTribes.Except(snapshot.AvailableTribes).ToList();
                return;
            }

            // Layer 1: Overlay VM
            var tribes = TryReadFromOverlayVm();

            // Layer 2: Pinning VM
            if (tribes == null)
            {
                tribes = TryReadFromPinningVm();
            }

            // Layer 3: Direct HearthMirror (в обход отравленного кэша HDT)
            if (tribes == null)
            {
                tribes = TryReadFromHearthMirror();
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
                    var hdtAssembly = typeof(Core).Assembly;
                    var singletonType = hdtAssembly.GetType("Hearthstone_Deck_Tracker.Utility.Battlegrounds.BattlegroundsDbSingleton");
                    if (singletonType != null)
                    {
                        var currentProp = singletonType.GetProperty("Current");
                        var currentDb = currentProp?.GetValue(null);
                        if (currentDb != null)
                        {
                            var racesProp = currentDb.GetType().GetProperty("Races");
                            if (racesProp?.GetValue(currentDb) is System.Collections.IEnumerable racesEnum)
                            {
                                var parsed = ParseRaces(racesEnum);
                                if (parsed.Count >= 3 && parsed.Count <= 7)
                                    tribes = parsed;
                            }
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
    }
}
