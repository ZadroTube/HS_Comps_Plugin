using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace HSCompsPlugin.Models
{
    public class OfficialCardsContainer
    {
        [JsonProperty("version")]
        public int Version { get; set; }

        [JsonProperty("lastUpdated")]
        public string LastUpdated { get; set; }

        [JsonProperty("totalCards")]
        public int TotalCards { get; set; }

        [JsonProperty("cards")]
        public List<OfficialCard> Cards { get; set; } = new List<OfficialCard>();
    }

    public class OfficialCard
    {
        [JsonProperty("id")]
        public int Id { get; set; } // Blizzard DbfId

        [JsonProperty("slug")]
        public string Slug { get; set; }

        [JsonProperty("nameRu")]
        public string NameRu { get; set; }

        [JsonProperty("nameEn")]
        public string NameEn { get; set; }

        [JsonProperty("tier")]
        public int? Tier { get; set; }

        [JsonProperty("cardTypeId")]
        public int CardTypeId { get; set; }

        [JsonProperty("cardType")]
        public string CardType { get; set; }

        private string _tribeRu;
        [JsonProperty("tribeRu")]
        public string TribeRu
        {
            get => _tribeRu;
            set
            {
                _tribeRu = value;
                _cachedTribesList = null;
                _cachedTribeMask = null;
            }
        }

        [JsonProperty("tribeEn")]
        public string TribeEn { get; set; }

        [JsonProperty("attack")]
        public int? Attack { get; set; }

        [JsonProperty("health")]
        public int? Health { get; set; }

        [JsonProperty("armor")]
        public int? Armor { get; set; }

        [JsonProperty("manaCost")]
        public int ManaCost { get; set; }

        [JsonProperty("textRu")]
        public string TextRu { get; set; }

        [JsonProperty("textEn")]
        public string TextEn { get; set; }

        [JsonProperty("cropImage")]
        public string CropImage { get; set; }

        [JsonProperty("imageRu")]
        public string ImageRu { get; set; }

        [JsonProperty("goldenUpgradeId")]
        public int? GoldenUpgradeId { get; set; }

        [JsonProperty("heroPowerId")]
        public int? HeroPowerId { get; set; }

        [JsonProperty("companionId")]
        public int? CompanionId { get; set; }

        [JsonIgnore]
        public bool IsMinion => string.Equals(CardType, "Minion", StringComparison.OrdinalIgnoreCase);

        [JsonIgnore]
        public bool IsTrinket => CardTypeId == 44 || string.Equals(CardType, "Trinket", StringComparison.OrdinalIgnoreCase) || string.Equals(CardType, "BattlegroundTrinket", StringComparison.OrdinalIgnoreCase);

        [JsonIgnore]
        public bool IsHero => string.Equals(CardType, "Hero", StringComparison.OrdinalIgnoreCase);

        private List<string> _cachedTribesList;
        [JsonIgnore]
        public List<string> TribesList
        {
            get
            {
                if (_cachedTribesList == null)
                {
                    if (string.IsNullOrWhiteSpace(_tribeRu) || _tribeRu == "Без типа")
                    {
                        _cachedTribesList = new List<string>(0);
                    }
                    else
                    {
                        _cachedTribesList = _tribeRu.Split(new[] { " / " }, StringSplitOptions.RemoveEmptyEntries)
                                                    .Select(t => t.Trim())
                                                    .ToList();
                    }
                }
                return _cachedTribesList;
            }
        }

        private TribeFlags? _cachedTribeMask;
        [JsonIgnore]
        public TribeFlags TribeMask
        {
            get
            {
                if (!_cachedTribeMask.HasValue)
                {
                    _cachedTribeMask = TribeFlagsHelper.ListToMask(TribesList);
                }
                return _cachedTribeMask.Value;
            }
        }
    }
}
