using System;
using System.Collections.Generic;

namespace HSCompsPlugin.Models
{
    public class WinningBoard
    {
        public string Id { get; set; }
        public string Archetype { get; set; }
        public string ArchetypeNameRu { get; set; }
        public string PrimaryTribe { get; set; }
        public List<BoardSlot> Minions { get; set; } = new List<BoardSlot>();
        public List<string> Trinkets { get; set; } = new List<string>();
        public List<string> RequiredTribes { get; set; } = new List<string>();
        public double AveragePlacement { get; set; } = 1.0;
        public double Top1Rate { get; set; } = 0.5;
        public int Turn { get; set; } = 12;
        public string HeroCardId { get; set; }
        public int PlayerRank { get; set; } = 6500;

        public float[] FeatureVector { get; set; }

        // Высокопроизводительные кэшированные свойства для мгновенного матчинга
        [Newtonsoft.Json.JsonIgnore]
        public TribeFlags PrimaryTribeFlag { get; set; } = TribeFlags.None;

        [Newtonsoft.Json.JsonIgnore]
        public TribeFlags RequiredTribesMask { get; set; } = TribeFlags.None;

        [Newtonsoft.Json.JsonIgnore]
        public TribeFlags MinionTribesMask { get; set; } = TribeFlags.None;

        [Newtonsoft.Json.JsonIgnore]
        public int[] MinionDbfIds { get; set; }

        [Newtonsoft.Json.JsonIgnore]
        public bool IsMalformedBoard { get; set; } = false;

        // Предрассчитанные веса и нормализованные ключи (вычисляются 1 раз при загрузке базы)
        [Newtonsoft.Json.JsonIgnore]
        public string CanonicalPrimaryTribe { get; set; } = "Все";

        [Newtonsoft.Json.JsonIgnore]
        public string NormalizedHeroCardId { get; set; } = string.Empty;

        [Newtonsoft.Json.JsonIgnore]
        public float[] MinionWeights { get; set; }

        [Newtonsoft.Json.JsonIgnore]
        public float NormDivisor { get; set; } = 6.5f;

        [Newtonsoft.Json.JsonIgnore]
        public HashSet<string> TrinketSet { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        [Newtonsoft.Json.JsonIgnore]
        public TribeFlags TrinketTribesMask { get; set; } = TribeFlags.None;
    }
}
