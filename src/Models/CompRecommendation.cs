using System.Collections.Generic;

namespace HSCompsPlugin.Models
{
    public class CompRecommendation
    {
        public WinningBoard Board { get; set; }
        public float SimilarityScore { get; set; } // 0.0 - 1.0 (например, 0.89f = 89%)
        public int MatchPercent => (int)(SimilarityScore * 100);

        public List<BoardSlot> DisplaySlots { get; set; } = new List<BoardSlot>();
        public int OwnedCount { get; set; }
        public int MissingCount { get; set; }
        public List<BoardSlot> MissingPriorityCards { get; set; } = new List<BoardSlot>();

        public bool IsLocked { get; set; }
        public string Title => Board?.ArchetypeNameRu ?? "Целевой стол";
        public string MetaStatsText => $"Среднее место: {Board?.AveragePlacement:0.0} | Топ-1: {(Board?.Top1Rate ?? 0.44):0%}";
    }
}
