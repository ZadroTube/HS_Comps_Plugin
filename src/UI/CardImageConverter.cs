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

    public class BoardShowcaseImageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is BoardSlot slot)
            {
                var full = LocalImageCache.Instance.GetFullCardBitmap(slot.DbfId, slot.CardId, slot.FullImageUrl);
                if (full != null)
                {
                    return full;
                }

                // Попробуем получить обычную карту, если золотая версия не имеет отдельного арта
                var norm = LocalImageCache.Instance.GetNormalCardBitmap(slot.DbfId, slot.CardId, slot.FullImageUrl);
                if (norm != null)
                {
                    return norm;
                }

                // В центральной части главного окна отображаются ИСКЛЮЧИТЕЛЬНО полноразмерные карты Hearthstone.
                // Ни в коем случае не возвращаем круглый токен мини-худа (frame_in_hand / frame_on_table).
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
                    case SlotStatus.FrozenInTavern:
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
                // Нижний левый блок главного окна («что собрано»): рамка frame_already_have с точной центровкой
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
                // Нижний правый блок главного окна («карты для поиска»): рамка frame_need_to_find
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
                        case SlotStatus.FrozenInTavern:
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
                string cardId = !string.IsNullOrEmpty(slot.Card?.Slug) ? slot.Card.Slug : slot.CardId;
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
                string baseCardId = !string.IsNullOrEmpty(slot.Card?.Slug) ? slot.Card.Slug : slot.CardId;
                string goldenCardId = $"{baseCardId}_G";
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

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string assemblyDir = Path.GetDirectoryName(typeof(StatusRingConverter).Assembly.Location) ?? "";

            string ringFile = "ring_bob.png";
            switch (status)
            {
                case SlotStatus.OnBoard:
                    ringFile = "ring_board.png";
                    break;
                case SlotStatus.InHand:
                    ringFile = "ring_hand.png";
                    break;
                case SlotStatus.FrozenInTavern:
                    ringFile = "ring_frozen.png";
                    break;
                case SlotStatus.NeedToFind:
                    ringFile = "ring_bob.png";
                    break;
            }

            string[] candidates = new[]
            {
                Path.Combine(assemblyDir, "assets", "ui", ringFile),
                Path.Combine(baseDir, "assets", "ui", ringFile),
                Path.Combine(baseDir, "Plugins", "HSCompsPlugin", "assets", "ui", ringFile),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HearthstoneDeckTracker", "Plugins", "HSCompsPlugin", "assets", "ui", ringFile)
            };

            foreach (var p in candidates)
            {
                if (File.Exists(p))
                {
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(p, UriKind.Absolute);
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        bmp.Freeze();
                        return bmp;
                    }
                    catch { }
                }
            }

            return null;
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

    public class FrozenVisibilityConverter : IValueConverter
    {
        public static readonly FrozenVisibilityConverter Instance = new FrozenVisibilityConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SlotStatus status && status == SlotStatus.FrozenInTavern)
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
