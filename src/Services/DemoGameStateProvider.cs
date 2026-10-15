using System.Collections.Generic;
using HSCompsPlugin.Models;

namespace HSCompsPlugin.Services
{
    public static class DemoGameStateProvider
    {
        public static PlayerGameStateSnapshot CreateDemoState()
        {
            var cardService = CardDatabaseService.Instance;

            var state = new PlayerGameStateSnapshot
            {
                Turn = 8,
                HeroCardId = "TB_BaconShop_HERO_70",
                TavernTier = 4,
                AvailableTribes = new List<string> { "Механизм", "Элементаль", "Мурлок", "Зверь", "Нежить" },
                BannedTribes = new List<string> { "Демон", "Пират", "Дракон", "Нага", "Свинобраз" },
                ActiveTrinkets = new List<string> { "BG30_MagicItem_434" }
            };

            // 1. Добавляем реальных существ из состава сборки mech_volumizer на стол:
            // DbfId: 98713 (BG26_149), DbfId: 90425 (BG23_318)
            var (card1, isGold1) = cardService.ResolveCard("BG26_149", 98713);
            if (card1 != null)
            {
                state.BoardMinions.Add(new BoardSlot
                {
                    CardId = "BG26_149",
                    DbfId = card1.Id,
                    Card = card1,
                    IsGolden = false,
                    Attack = 4,
                    Health = 7,
                    HasDivineShield = true,
                    HasTaunt = false,
                    ZonePosition = 0,
                    Status = SlotStatus.OnBoard
                });
            }

            var (card2, isGold2) = cardService.ResolveCard("BG23_318", 90425);
            if (card2 != null)
            {
                state.BoardMinions.Add(new BoardSlot
                {
                    CardId = "BG23_318",
                    DbfId = card2.Id,
                    Card = card2,
                    IsGolden = false,
                    Attack = 3,
                    Health = 6,
                    HasDivineShield = false,
                    HasTaunt = true,
                    ZonePosition = 1,
                    Status = SlotStatus.OnBoard
                });
            }

            // Добавим темповое существо на стол (Accord-o-Tron: 98576)
            var (accordCard, _) = cardService.ResolveCard("BG26_147", 98576);
            if (accordCard != null)
            {
                state.BoardMinions.Add(new BoardSlot
                {
                    CardId = "BG26_147",
                    DbfId = accordCard.Id,
                    Card = accordCard,
                    IsGolden = false,
                    Attack = 5,
                    Health = 5,
                    ZonePosition = 2,
                    Status = SlotStatus.OnBoard
                });
            }

            // 2. Добавляем существо в руку: DbfId: 132676 (BG36_851)
            var (cardHand, _) = cardService.ResolveCard("BG36_851", 132676);
            if (cardHand != null)
            {
                state.HandMinions.Add(new BoardSlot
                {
                    CardId = "BG36_851",
                    DbfId = cardHand.Id,
                    Card = cardHand,
                    IsGolden = true,
                    Attack = 6,
                    Health = 10,
                    ZonePosition = 0,
                    Status = SlotStatus.InHand
                });
            }

            return state;
        }

        public static PlayerGameStateSnapshot CreateMainMenuState()
        {
            return new PlayerGameStateSnapshot
            {
                Turn = 0,
                HeroCardId = null,
                TavernTier = 1,
                AvailableTribes = new List<string> { "Механизм", "Элементаль", "Мурлок", "Зверь", "Нежить" },
                BannedTribes = new List<string> { "Демон", "Пират", "Дракон", "Нага", "Свинобраз" },
                ActiveTrinkets = new List<string>(),
                BoardMinions = new List<BoardSlot>(),
                HandMinions = new List<BoardSlot>()
            };
        }
    }
}
