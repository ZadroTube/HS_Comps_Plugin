using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace HSCompsPlugin.UI
{
    public partial class SpriteNumberControl : UserControl
    {
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(nameof(Text), typeof(string), typeof(SpriteNumberControl),
                new PropertyMetadata(string.Empty, OnTextChanged));

        public static readonly DependencyProperty DigitHeightProperty =
            DependencyProperty.Register(nameof(DigitHeight), typeof(double), typeof(SpriteNumberControl),
                new PropertyMetadata(20.0, OnDigitHeightChanged));

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public double DigitHeight
        {
            get => (double)GetValue(DigitHeightProperty);
            set => SetValue(DigitHeightProperty, value);
        }

        private static readonly Dictionary<string, BitmapImage> _glyphCache = new Dictionary<string, BitmapImage>(StringComparer.OrdinalIgnoreCase);
        private static bool _cacheInitialized = false;

        public SpriteNumberControl()
        {
            InitializeComponent();
            EnsureGlyphsLoaded();
        }

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SpriteNumberControl control)
            {
                control.RenderDigits();
            }
        }

        private static void OnDigitHeightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SpriteNumberControl control)
            {
                control.RenderDigits();
            }
        }

        private static void EnsureGlyphsLoaded()
        {
            if (_cacheInitialized) return;

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string assemblyDir = Path.GetDirectoryName(typeof(SpriteNumberControl).Assembly.Location) ?? "";

            string[] searchRoots = new[]
            {
                Path.Combine(assemblyDir, "assets", "ui", "digits"),
                Path.Combine(baseDir, "assets", "ui", "digits"),
                Path.Combine(baseDir, "Plugins", "HSCompsPlugin", "assets", "ui", "digits"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HearthstoneDeckTracker", "Plugins", "HSCompsPlugin", "assets", "ui", "digits")
            };

            string foundRoot = null;
            foreach (var root in searchRoots)
            {
                if (Directory.Exists(root) && File.Exists(Path.Combine(root, "digit_0.png")))
                {
                    foundRoot = root;
                    break;
                }
            }

            if (foundRoot == null) return;

            string[] keys = new[] { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "slash", "dot", "percent" };
            foreach (var k in keys)
            {
                string p = Path.Combine(foundRoot, $"digit_{k}.png");
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
                        _glyphCache[k] = bmp;
                    }
                    catch { }
                }
            }

            _cacheInitialized = true;
        }

        private void RenderDigits()
        {
            if (string.IsNullOrEmpty(Text))
            {
                if (DigitsPanel.Children.Count > 0)
                {
                    DigitsPanel.Children.Clear();
                }
                return;
            }

            EnsureGlyphsLoaded();

            double h = DigitHeight > 0 ? DigitHeight : 20.0;
            int childIndex = 0;

            for (int i = 0; i < Text.Length; i++)
            {
                char ch = Text[i];
                string key = null;

                if (char.IsDigit(ch))
                {
                    key = ch.ToString();
                }
                else if (ch == '/')
                {
                    key = "slash";
                }
                else if (ch == '.' || ch == ',')
                {
                    key = "dot";
                }
                else if (ch == '%')
                {
                    key = "percent";
                }
                else if (ch == ' ')
                {
                    // Пробел
                    double w = h * 0.35;
                    if (childIndex < DigitsPanel.Children.Count && DigitsPanel.Children[childIndex] is Border existingBorder)
                    {
                        if (Math.Abs(existingBorder.Width - w) > 0.01) existingBorder.Width = w;
                        if (Math.Abs(existingBorder.Height - h) > 0.01) existingBorder.Height = h;
                    }
                    else
                    {
                        var spacer = new Border { Width = w, Height = h };
                        SetOrAddChild(childIndex, spacer);
                    }
                    childIndex++;
                    continue;
                }

                if (key != null && _glyphCache.TryGetValue(key, out var bmp))
                {
                    double glyphHeight = (key == "dot") ? Math.Max(4.0, h * 0.24) : h;
                    var vAlign = (key == "dot") ? VerticalAlignment.Bottom : VerticalAlignment.Center;
                    var margin = (key == "dot") ? new Thickness(1, 0, 1, h * 0.12) : new Thickness(0.5, 0, 0.5, 0);

                    if (childIndex < DigitsPanel.Children.Count && DigitsPanel.Children[childIndex] is Image existingImg)
                    {
                        if (!ReferenceEquals(existingImg.Source, bmp)) existingImg.Source = bmp;
                        if (Math.Abs(existingImg.Height - glyphHeight) > 0.01) existingImg.Height = glyphHeight;
                        if (existingImg.VerticalAlignment != vAlign) existingImg.VerticalAlignment = vAlign;
                        if (existingImg.Margin != margin) existingImg.Margin = margin;
                    }
                    else
                    {
                        var img = new Image
                        {
                            Source = bmp,
                            Height = glyphHeight,
                            VerticalAlignment = vAlign,
                            Stretch = System.Windows.Media.Stretch.Uniform,
                            Margin = margin,
                            SnapsToDevicePixels = true
                        };
                        SetOrAddChild(childIndex, img);
                    }
                    childIndex++;
                }
                else
                {
                    // Запасной текстовый символ, если нет спрайта
                    string str = ch.ToString();
                    double fontSize = h * 0.75;
                    if (childIndex < DigitsPanel.Children.Count && DigitsPanel.Children[childIndex] is TextBlock existingTb)
                    {
                        if (existingTb.Text != str) existingTb.Text = str;
                        if (Math.Abs(existingTb.FontSize - fontSize) > 0.01) existingTb.FontSize = fontSize;
                    }
                    else
                    {
                        var tb = new TextBlock
                        {
                            Text = str,
                            Foreground = System.Windows.Media.Brushes.Gold,
                            FontSize = fontSize,
                            FontWeight = FontWeights.Bold,
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        SetOrAddChild(childIndex, tb);
                    }
                    childIndex++;
                }
            }

            // Удаляем лишние старые элементы
            while (DigitsPanel.Children.Count > childIndex)
            {
                DigitsPanel.Children.RemoveAt(DigitsPanel.Children.Count - 1);
            }
        }

        private void SetOrAddChild(int index, UIElement element)
        {
            if (index < DigitsPanel.Children.Count)
            {
                DigitsPanel.Children.RemoveAt(index);
                DigitsPanel.Children.Insert(index, element);
            }
            else
            {
                DigitsPanel.Children.Add(element);
            }
        }
    }
}
