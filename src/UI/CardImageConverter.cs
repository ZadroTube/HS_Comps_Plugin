using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using HSCompsPlugin.Models;
using HSCompsPlugin.Services;

namespace HSCompsPlugin.UI
{
    public class CardCropImageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is BoardSlot slot)
            {
                return LocalImageCache.Instance.GetCropCardBitmap(slot.DbfId, slot.CardId, slot.CropImageUrl);
            }

            if (value is string url)
            {
                return LocalImageCache.Instance.GetImage(url);
            }

            if (value is int dbfId)
            {
                return LocalImageCache.Instance.GetCropCardBitmap(dbfId);
            }

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class CardFullImageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is BoardSlot slot)
            {
                return LocalImageCache.Instance.GetFullCardBitmap(slot.DbfId, slot.CardId, slot.FullImageUrl);
            }

            if (value is string url)
            {
                return LocalImageCache.Instance.GetImage(url);
            }

            if (value is int dbfId)
            {
                return LocalImageCache.Instance.GetFullCardBitmap(dbfId);
            }

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    internal static class CardIdResolverHelper
    {
        public static bool IsSyntheticNumericBgId(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return true;
            if (char.IsDigit(cardId[0])) return true;
            if (cardId.StartsWith("BG_", StringComparison.OrdinalIgnoreCase) && cardId.Length > 3 && char.IsDigit(cardId[3]))
                return true;
            return false;
        }

        public static string ResolveEffectiveCardId(BoardSlot slot)
        {
            if (slot == null) return string.Empty;
            string cardId = slot.CardId;
            if (!IsSyntheticNumericBgId(cardId))
            {
                return cardId;
            }
            string slug = slot.Card?.Slug;
            if (!string.IsNullOrEmpty(slug) && !char.IsDigit(slug[0]))
            {
                return slug;
            }
            int dbf = slot.DbfId > 0 ? slot.DbfId : (slot.Card?.Id ?? 0);
            if (dbf > 0)
            {
                string mapped = CardDatabaseService.Instance.GetCardIdByDbfId(dbf);
                if (!string.IsNullOrEmpty(mapped)) return mapped;
            }
            return cardId ?? string.Empty;
        }

        public static string ResolveCleanBaseCardId(BoardSlot slot)
        {
            string raw = ResolveEffectiveCardId(slot);
            if (!string.IsNullOrEmpty(raw) && raw.EndsWith("_G", StringComparison.OrdinalIgnoreCase))
            {
                return raw.Substring(0, raw.Length - 2);
            }
            return raw;
        }
    }

    public class BoardShowcaseImageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is BoardSlot slot)
            {
                string effectiveId = CardIdResolverHelper.ResolveEffectiveCardId(slot);
                string baseCardId = CardIdResolverHelper.ResolveCleanBaseCardId(slot);

                // Если карта на столе золотая — проверяем золотую версию (без блокировки показа обычной карты)
                if (slot.IsGolden)
                {
                    int goldenDbfId = slot.Card?.GoldenUpgradeId ?? 0;
                    string goldenCardId = !string.IsNullOrEmpty(effectiveId) && (effectiveId.EndsWith("_G", StringComparison.OrdinalIgnoreCase) || effectiveId.StartsWith("TB_BaconUps_", StringComparison.OrdinalIgnoreCase))
                        ? effectiveId
                        : (!string.IsNullOrEmpty(baseCardId) ? $"{baseCardId}_G" : slot.CardId);

                    var goldBmp = LocalImageCache.Instance.GetGoldenCardBitmap(goldenDbfId, goldenCardId, baseCardId);
                    if (goldBmp != null)
                    {
                        return goldBmp;
                    }
                }

                // Мгновенно возвращаем обычную полную карту (из локального кэша или официального CloudFront ImageRu)
                var norm = LocalImageCache.Instance.GetNormalCardBitmap(slot.DbfId, baseCardId, slot.FullImageUrl);
                if (norm != null)
                {
                    return norm;
                }

                var full = LocalImageCache.Instance.GetFullCardBitmap(slot.DbfId, slot.CardId, slot.FullImageUrl);
                if (full != null)
                {
                    return full;
                }

                return null;
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class TokenStatsVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is BoardSlot slot)
            {
                bool hasFull = LocalImageCache.Instance.HasOfficialCardRender(slot.DbfId, slot.CardId);
                return hasFull ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
            }
            return System.Windows.Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class MiniHudFramedTokenConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is BoardSlot slot)
            {
                string frame = "frame_need_to_find";
                switch (slot.Status)
                {
                    case SlotStatus.OnBoard:
                        frame = "frame_on_table";
                        break;
                    case SlotStatus.InHand:
                        frame = "frame_in_hand";
                        break;
                    case SlotStatus.NeedToFind:
                    default:
                        frame = "frame_need_to_find";
                        break;
                }
                return LocalImageCache.Instance.GetFramedMinionBitmap(slot.DbfId, slot.CardId, frame, slot.Status, slot.CropImageUrl);
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class OwnedFramedTokenConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is BoardSlot slot)
            {
                return LocalImageCache.Instance.GetFramedMinionBitmap(slot.DbfId, slot.CardId, "frame_already_have", slot.Status, slot.CropImageUrl);
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class PriorityFramedTokenConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is BoardSlot slot)
            {
                return LocalImageCache.Instance.GetFramedMinionBitmap(slot.DbfId, slot.CardId, "frame_need_to_find", slot.Status, slot.CropImageUrl);
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class CardFramedTokenConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is BoardSlot slot)
            {
                string frame = parameter as string;
                if (string.IsNullOrEmpty(frame))
                {
                    switch (slot.Status)
                    {
                        case SlotStatus.OnBoard: frame = "frame_on_table"; break;
                        case SlotStatus.InHand: frame = "frame_in_hand"; break;
                        default: frame = "frame_need_to_find"; break;
                    }
                }
                return LocalImageCache.Instance.GetFramedMinionBitmap(slot.DbfId, slot.CardId, frame, slot.Status, slot.CropImageUrl);
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class CardNormalImageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is BoardSlot slot)
            {
                int dbfId = slot.Card?.Id ?? slot.DbfId;
                string cardId = CardIdResolverHelper.ResolveCleanBaseCardId(slot);
                return LocalImageCache.Instance.GetNormalCardBitmap(dbfId, cardId, slot.FullImageUrl);
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class CardGoldenImageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is BoardSlot slot)
            {
                int goldenDbfId = slot.Card?.GoldenUpgradeId ?? 0;
                string baseCardId = CardIdResolverHelper.ResolveCleanBaseCardId(slot);
                string goldenCardId = !string.IsNullOrEmpty(baseCardId) ? $"{baseCardId}_G" : slot.CardId;
                return LocalImageCache.Instance.GetGoldenCardBitmap(goldenDbfId, goldenCardId, baseCardId);
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class StatusRingConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is SlotStatus status))
                return null;

            return LocalImageCache.Instance.GetRingBitmap(status);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class BooleanToVisibilityConverter : IValueConverter
    {
        public static readonly BooleanToVisibilityConverter Instance = new BooleanToVisibilityConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b) return System.Windows.Visibility.Visible;
            return System.Windows.Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class OnBoardVisibilityConverter : IValueConverter
    {
        public static readonly OnBoardVisibilityConverter Instance = new OnBoardVisibilityConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SlotStatus status && status == SlotStatus.OnBoard)
                return System.Windows.Visibility.Visible;
            return System.Windows.Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class InHandVisibilityConverter : IValueConverter
    {
        public static readonly InHandVisibilityConverter Instance = new InHandVisibilityConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SlotStatus status && status == SlotStatus.InHand)
                return System.Windows.Visibility.Visible;
            return System.Windows.Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class NeedToFindVisibilityConverter : IValueConverter
    {
        public static readonly NeedToFindVisibilityConverter Instance = new NeedToFindVisibilityConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SlotStatus status && status == SlotStatus.NeedToFind)
                return System.Windows.Visibility.Visible;
            return System.Windows.Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class StringToVisibilityConverter : IValueConverter
    {
        public static readonly StringToVisibilityConverter Instance = new StringToVisibilityConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s && !string.IsNullOrWhiteSpace(s))
                return System.Windows.Visibility.Visible;
            return System.Windows.Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
