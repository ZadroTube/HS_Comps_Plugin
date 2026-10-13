using System;
using System.Collections.Generic;

namespace HSCompsPlugin.Models
{
    [Flags]
    public enum TribeFlags : ushort
    {
        None = 0,
        Mech = 1 << 0,       // Механизм
        Beast = 1 << 1,      // Зверь
        Murloc = 1 << 2,     // Мурлок
        Undead = 1 << 3,     // Нежить
        Demon = 1 << 4,      // Демон
        Elemental = 1 << 5,  // Элементаль
        Naga = 1 << 6,       // Нага
        Pirate = 1 << 7,     // Пират
        Dragon = 1 << 8,     // Дракон
        Quilboar = 1 << 9,   // Свинобраз
        Aberration = 1 << 10,// Абберация
        All = (1 << 11) - 1  // Все племена
    }

    public static class TribeFlagsHelper
    {
        public static TribeFlags StringToFlag(string tribe)
        {
            if (string.IsNullOrWhiteSpace(tribe)) return TribeFlags.None;
            string lower = tribe.Trim().ToLowerInvariant();

            if (lower.StartsWith("мех") || lower.Contains("mech")) return TribeFlags.Mech;
            if (lower.StartsWith("звер") || lower.Contains("beast")) return TribeFlags.Beast;
            if (lower.StartsWith("мурл") || lower.Contains("murloc")) return TribeFlags.Murloc;
            if (lower.StartsWith("неж") || lower.Contains("undead")) return TribeFlags.Undead;
            if (lower.StartsWith("дем") || lower.Contains("demon")) return TribeFlags.Demon;
            if (lower.StartsWith("элем") || lower.Contains("elemental")) return TribeFlags.Elemental;
            if (lower.StartsWith("наг") || lower.Contains("naga")) return TribeFlags.Naga;
            if (lower.StartsWith("пир") || lower.Contains("pirate")) return TribeFlags.Pirate;
            if (lower.StartsWith("драк") || lower.Contains("dragon")) return TribeFlags.Dragon;
            if (lower.StartsWith("свин") || lower.Contains("quilboar") || lower.Contains("boar")) return TribeFlags.Quilboar;
            if (lower.StartsWith("аббер") || lower.Contains("aberr")) return TribeFlags.Aberration;
            if (lower.StartsWith("все") || lower.StartsWith("all") || lower.Contains("menagerie")) return TribeFlags.All;

            return TribeFlags.None;
        }

        public static TribeFlags ListToMask(IEnumerable<string> tribes)
        {
            if (tribes == null) return TribeFlags.None;
            TribeFlags mask = TribeFlags.None;
            foreach (var t in tribes)
            {
                mask |= StringToFlag(t);
            }
            return mask;
        }

        public static bool Matches(TribeFlags a, TribeFlags b)
        {
            if (a == TribeFlags.None || b == TribeFlags.None) return false;
            if (a == TribeFlags.All || b == TribeFlags.All) return true;
            return (a & b) != TribeFlags.None;
        }
    }
}
