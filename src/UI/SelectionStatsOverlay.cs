using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using HSCompsPlugin.Services;

namespace HSCompsPlugin.UI
{
    /// <summary>
    /// Компактный сквозной оверлей (IsHitTestVisible = false), отображающий плашки
    /// [Тир | Частота выбора | Частота побед] строго под картами в момент выбора Героя или Тринкета.
    /// Не использует XAML/BAML и полностью пропускает клики мыши в Hearthstone.
    /// </summary>
    public class SelectionStatsOverlay : Canvas
    {
        private string _lastRenderSignature = string.Empty;

        public SelectionStatsOverlay()
        {
            IsHitTestVisible = false;
            Focusable = false;
            Background = Brushes.Transparent;
            Visibility = Visibility.Collapsed;
        }

        public void HideOverlay()
        {
            if (Visibility != Visibility.Collapsed || Children.Count > 0)
            {
                Children.Clear();
                Visibility = Visibility.Collapsed;
                _lastRenderSignature = string.Empty;
            }
        }

        public void UpdateSelection(SelectionOverlayMode mode, IList<EvaluatedSelectionScore> scores, double canvasWidth, double canvasHeight)
        {
            if (mode == SelectionOverlayMode.None || scores == null || scores.Count == 0 || !PluginSettings.Instance.ShowSelectionStatsOverlay)
            {
                HideOverlay();
                return;
            }

            if (canvasWidth <= 100) canvasWidth = SystemParameters.PrimaryScreenWidth;
            if (canvasHeight <= 100) canvasHeight = SystemParameters.PrimaryScreenHeight;

            double scale = Math.Max(0.65, Math.Min(1.35, PluginSettings.Instance.OverlayScale));
            string sig = $"{mode}_{canvasWidth:0}_{canvasHeight:0}_{scale:0.00}_" +
                         string.Join("|", scores.Select(s => $"{s.CardId}:{s.Tier}:{s.PickRate}:{s.WinRate:0.0}:{s.IsBestChoice}:{s.HasSynergy}"));

            if (Visibility == Visibility.Visible && string.Equals(sig, _lastRenderSignature, StringComparison.Ordinal))
            {
                return;
            }

            _lastRenderSignature = sig;
            Width = canvasWidth;
            Height = canvasHeight;
            Children.Clear();

            int count = scores.Count;
            double centerX = canvasWidth / 2.0;

            // Геометрия слотов выбора в Hearthstone (относительно высоты игрового вьюпорта canvasHeight)
            // Для 4 карт: центры расположены на centerX + (i - 1.5) * stepX
            // Для 2 героев: centerX + (i - 0.5) * stepX
            double stepX;
            if (count == 2)
                stepX = canvasHeight * 0.355;
            else if (count == 3)
                stepX = canvasHeight * 0.325;
            else
                stepX = canvasHeight * 0.302;

            double topY = (mode == SelectionOverlayMode.HeroSelection)
                ? canvasHeight * 0.658
                : canvasHeight * 0.665;

            double cardWidth = Math.Round(192.0 * scale);

            for (int i = 0; i < count; i++)
            {
                var score = scores[i];
                if (score == null) continue;

                double offsetIndex = i - (count - 1) / 2.0;
                double slotCenterX = centerX + offsetIndex * stepX;

                var plaque = BuildCompactPlaque(score, scale);
                plaque.Width = cardWidth;

                SetLeft(plaque, Math.Round(slotCenterX - cardWidth / 2.0));
                SetTop(plaque, Math.Round(topY));
                Children.Add(plaque);
            }

            Visibility = Visibility.Visible;
        }

        private FrameworkElement BuildCompactPlaque(EvaluatedSelectionScore score, double scale)
        {
            var (tierBg, tierBorder, tierTextBrush) = GetTierPalette(score.Tier);

            Color outerBorderColor = score.IsBestChoice
                ? Color.FromRgb(255, 212, 92)
                : (score.HasSynergy ? Color.FromRgb(120, 224, 143) : Color.FromRgb(122, 86, 50));

            var rootBorder = new Border
            {
                IsHitTestVisible = false,
                Background = new SolidColorBrush(Color.FromArgb(238, 24, 16, 11)),
                BorderBrush = new SolidColorBrush(outerBorderColor),
                BorderThickness = new Thickness(score.IsBestChoice ? 2.0 : 1.4),
                CornerRadius = new CornerRadius(8 * scale),
                Padding = new Thickness(8 * scale, 6 * scale, 8 * scale, 6 * scale),
                Effect = new DropShadowEffect
                {
                    Color = score.IsBestChoice ? Color.FromRgb(255, 184, 0) : Colors.Black,
                    BlurRadius = score.IsBestChoice ? 12 : 8,
                    ShadowDepth = score.IsBestChoice ? 0 : 2,
                    Opacity = score.IsBestChoice ? 0.75 : 0.88
                }
            };

            var stack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                IsHitTestVisible = false
            };

            // Строка 1: Бейдж Тира [ТИР S] слева + Метка [★ ЛУЧШИЙ] или [Ср. #3.59] справа
            var topGrid = new Grid { Margin = new Thickness(0, 0, 0, 4 * scale) };
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var tierPill = new Border
            {
                Background = tierBg,
                BorderBrush = tierBorder,
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(4 * scale),
                Padding = new Thickness(6 * scale, 1 * scale, 6 * scale, 1 * scale),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };

            tierPill.Child = new TextBlock
            {
                Text = $"ТИР {score.Tier}",
                Foreground = tierTextBrush,
                FontWeight = FontWeights.ExtraBold,
                FontSize = Math.Max(9.5, Math.Round(11.5 * scale, 1))
            };
            Grid.SetColumn(tierPill, 0);
            topGrid.Children.Add(tierPill);

            var rightTagText = new TextBlock
            {
                Text = score.IsBestChoice
                    ? "★ ЛУЧШИЙ"
                    : $"Ср. #{score.AvgPlacement.ToString("0.00", CultureInfo.InvariantCulture)}",
                Foreground = score.IsBestChoice
                    ? new SolidColorBrush(Color.FromRgb(255, 224, 115))
                    : new SolidColorBrush(Color.FromRgb(198, 178, 156)),
                FontWeight = FontWeights.Bold,
                FontSize = Math.Max(9.0, Math.Round(11.0 * scale, 1)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(rightTagText, 1);
            topGrid.Children.Add(rightTagText);

            stack.Children.Add(topGrid);

            // Строка 2: Частота выбора (PickRate) и Частота побед (WinRate)
            var metricsGrid = new Grid();
            metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var pickPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
            pickPanel.Children.Add(new TextBlock
            {
                Text = "Выбор: ",
                Foreground = new SolidColorBrush(Color.FromRgb(186, 166, 145)),
                FontSize = Math.Max(9.0, Math.Round(11.0 * scale, 1)),
                FontWeight = FontWeights.SemiBold
            });
            pickPanel.Children.Add(new TextBlock
            {
                Text = $"{score.PickRate}%",
                Foreground = new SolidColorBrush(Color.FromRgb(255, 236, 179)),
                FontSize = Math.Max(9.5, Math.Round(11.5 * scale, 1)),
                FontWeight = FontWeights.Bold
            });
            Grid.SetColumn(pickPanel, 0);
            metricsGrid.Children.Add(pickPanel);

            var winPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            winPanel.Children.Add(new TextBlock
            {
                Text = "Победы: ",
                Foreground = new SolidColorBrush(Color.FromRgb(186, 166, 145)),
                FontSize = Math.Max(9.0, Math.Round(11.0 * scale, 1)),
                FontWeight = FontWeights.SemiBold
            });
            winPanel.Children.Add(new TextBlock
            {
                Text = $"{score.WinRate.ToString("0.0", CultureInfo.InvariantCulture)}%",
                Foreground = score.WinRate >= 58.0
                    ? new SolidColorBrush(Color.FromRgb(144, 245, 135))
                    : new SolidColorBrush(Color.FromRgb(255, 226, 148)),
                FontSize = Math.Max(9.5, Math.Round(11.5 * scale, 1)),
                FontWeight = FontWeights.Bold
            });
            Grid.SetColumn(winPanel, 1);
            metricsGrid.Children.Add(winPanel);

            stack.Children.Add(metricsGrid);

            // Строка 3 (опционально): Заметка о синергии со столом или лобби
            if (!string.IsNullOrEmpty(score.SynergyNote))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = $"⚡ {score.SynergyNote}",
                    Foreground = new SolidColorBrush(Color.FromRgb(138, 238, 158)),
                    FontSize = Math.Max(8.5, Math.Round(10.2 * scale, 1)),
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 3 * scale, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }

            rootBorder.Child = stack;
            return rootBorder;
        }

        private static (Brush bg, Brush border, Brush fg) GetTierPalette(string tier)
        {
            switch ((tier ?? "B").Trim().ToUpperInvariant())
            {
                case "S":
                    return (
                        new SolidColorBrush(Color.FromArgb(235, 36, 74, 24)),
                        new SolidColorBrush(Color.FromRgb(255, 215, 75)),
                        new SolidColorBrush(Color.FromRgb(255, 238, 135))
                    );
                case "A":
                    return (
                        new SolidColorBrush(Color.FromArgb(235, 24, 64, 38)),
                        new SolidColorBrush(Color.FromRgb(108, 224, 130)),
                        new SolidColorBrush(Color.FromRgb(178, 252, 190))
                    );
                case "B":
                    return (
                        new SolidColorBrush(Color.FromArgb(235, 24, 48, 74)),
                        new SolidColorBrush(Color.FromRgb(98, 178, 248)),
                        new SolidColorBrush(Color.FromRgb(184, 224, 255))
                    );
                case "C":
                    return (
                        new SolidColorBrush(Color.FromArgb(235, 68, 46, 20)),
                        new SolidColorBrush(Color.FromRgb(224, 158, 72)),
                        new SolidColorBrush(Color.FromRgb(255, 214, 158))
                    );
                default:
                    return (
                        new SolidColorBrush(Color.FromArgb(235, 58, 34, 34)),
                        new SolidColorBrush(Color.FromRgb(176, 112, 112)),
                        new SolidColorBrush(Color.FromRgb(228, 186, 186))
                    );
            }
        }
    }
}
