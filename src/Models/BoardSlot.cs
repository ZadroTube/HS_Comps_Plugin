using System;

namespace HSCompsPlugin.Models
{
    public enum SlotStatus
    {
        OnBoard,         // 🟢 Зеленое кольцо (на столе)
        InHand,          // 🟡 Золотое кольцо (в руке)
        FrozenInTavern,  // 🔵 Синее ледяное кольцо (заморожено у Боба)
        NeedToFind       // ⚪ Серое кольцо с тиром (нужно найти)
    }

    public class BoardSlot
    {
        public string CardId { get; set; } // Например: "BG26_147"
        public int DbfId { get; set; }      // Blizzard ID (98576)
        public OfficialCard Card { get; set; }
        public bool IsGolden { get; set; }
        public int Attack { get; set; }
        public int Health { get; set; }
        public bool HasTaunt { get; set; }
        public bool HasDivineShield { get; set; }
        public bool HasReborn { get; set; }
        public bool HasPoisonous { get; set; }
        public bool HasVenomous { get; set; }
        public bool HasWindfury { get; set; }

        public SlotStatus Status { get; set; } = SlotStatus.NeedToFind;
        public int ZonePosition { get; set; } // 0..6 (порядок атаки)

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrEmpty(Card?.NameRu)) return Card.NameRu;
                if (!string.IsNullOrEmpty(CardId))
                {
                    try
                    {
                        var hdtCard = Hearthstone_Deck_Tracker.Hearthstone.Database.GetCardFromId(CardId);
                        if (hdtCard != null)
                        {
                            return !string.IsNullOrEmpty(hdtCard.LocalizedName) ? hdtCard.LocalizedName : hdtCard.Name;
                        }
                    }
                    catch { }
                }
                return "Неизвестное существо";
            }
        }

        public string TooltipText => CleanTooltipText;

        public string CleanTooltipText
        {
            get
            {
                string text = Card?.TextRu;
                if (string.IsNullOrEmpty(text) && Card != null)
                {
                    text = Card.TextEn;
                }

                if (string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(CardId))
                {
                    try
                    {
                        var hdtCard = Hearthstone_Deck_Tracker.Hearthstone.Database.GetCardFromId(CardId);
                        if (hdtCard != null)
                        {
                            text = !string.IsNullOrEmpty(hdtCard.Text) ? hdtCard.Text : hdtCard.EnglishText;
                        }
                    }
                    catch { }
                }

                if (string.IsNullOrEmpty(text)) return "";

                string clean = text.Replace("[x]", "").Replace("$", "").Replace("\n", " ");
                clean = System.Text.RegularExpressions.Regex.Replace(clean, "<.*?>", string.Empty);
                return clean.Trim();
            }
        }

        public string TribeRu
        {
            get
            {
                if (!string.IsNullOrEmpty(Card?.TribeRu)) return Card.TribeRu;
                if (!string.IsNullOrEmpty(CardId))
                {
                    try
                    {
                        var hdtCard = Hearthstone_Deck_Tracker.Hearthstone.Database.GetCardFromId(CardId);
                        if (hdtCard != null && !string.IsNullOrEmpty(hdtCard.Race))
                        {
                            return HSCompsPlugin.Services.GameStateReader.MapRaceStringToRu(hdtCard.Race);
                        }
                    }
                    catch { }
                }
                return "Существо";
            }
        }

        public int Tier
        {
            get
            {
                if (Card?.Tier.HasValue == true && Card.Tier.Value > 0) return Card.Tier.Value;
                if (!string.IsNullOrEmpty(CardId))
                {
                    try
                    {
                        var hdtCard = Hearthstone_Deck_Tracker.Hearthstone.Database.GetCardFromId(CardId);
                        if (hdtCard != null && hdtCard.Cost > 0) return hdtCard.Cost;
                    }
                    catch { }
                }
                return 1;
            }
        }

        public string CropImageUrl => Card?.CropImage ?? "";
        public string FullImageUrl => Card?.ImageRu ?? "";
    }
}
