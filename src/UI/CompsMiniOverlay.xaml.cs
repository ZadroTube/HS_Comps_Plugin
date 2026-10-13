using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using HSCompsPlugin.Models;
using HSCompsPlugin.Services;

namespace HSCompsPlugin.UI
{
    public partial class CompsMiniOverlay : UserControl
    {
        private List<CompRecommendation> _recommendations = new List<CompRecommendation>();
        private int _currentIndex = 0;
        private bool _isLocked = false;

        private bool _isDragging = false;
        private Point _dragStartPos;
        private Point _elementStartPos;

        private BitmapImage _hudBgMain;
        private BitmapImage _hudBgLeftArrowHover;
        private BitmapImage _hudBgRightArrowHover;

        public event Action OpenDetailsRequested;
        public event Action<int, string, bool> LockStateChanged;

        public CompsMiniOverlay()
        {
            InitializeComponent();
            LoadTextures();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private BitmapImage LoadImageSafe(string relativePath)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string assemblyDir = Path.GetDirectoryName(typeof(CompsMiniOverlay).Assembly.Location) ?? "";

            string[] candidates = new[]
            {
                Path.Combine(assemblyDir, relativePath),
                Path.Combine(baseDir, relativePath),
                Path.Combine(Directory.GetCurrentDirectory(), relativePath),
                Path.Combine(baseDir, "Plugins", "HSCompsPlugin", relativePath),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HearthstoneDeckTracker", "Plugins", "HSCompsPlugin", relativePath)
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

        private void LoadTextures()
        {
            try
            {
                _hudBgMain = LoadImageSafe(Path.Combine("assets", "ui", "hud", "hud_overlay_bg_new_main.png"))
                             ?? LoadImageSafe(Path.Combine("assets", "ui", "hud_overlay_bg.png"));
                _hudBgLeftArrowHover = LoadImageSafe(Path.Combine("assets", "ui", "hud", "hud_overlay_bg_new_left_arrow.png"));
                _hudBgRightArrowHover = LoadImageSafe(Path.Combine("assets", "ui", "hud", "hud_overlay_bg_new_right_arrow.png"));

                if (_hudBgMain != null) ImgHudBg.Source = _hudBgMain;
            }
            catch { }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ApplySavedSettings();

            // Регистрация в подсистеме ввода HDT для снятия WS_EX_TRANSPARENT и поддержки мыши
            HdtOverlayInputManager.RegisterElement(this);
            HdtOverlayInputManager.RegisterElement(BorderArrowLeft);
            HdtOverlayInputManager.RegisterElement(BorderArrowRight);
            HdtOverlayInputManager.RegisterElement(BorderDetails);
            HdtOverlayInputManager.RegisterElement(Slot0);
            HdtOverlayInputManager.RegisterElement(Slot1);
            HdtOverlayInputManager.RegisterElement(Slot2);
            HdtOverlayInputManager.RegisterElement(Slot3);
            HdtOverlayInputManager.RegisterElement(Slot4);
            HdtOverlayInputManager.RegisterElement(Slot5);
            HdtOverlayInputManager.RegisterElement(Slot6);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            HdtOverlayInputManager.UnregisterElement(this);
            HdtOverlayInputManager.UnregisterElement(BorderArrowLeft);
            HdtOverlayInputManager.UnregisterElement(BorderArrowRight);
            HdtOverlayInputManager.UnregisterElement(BorderDetails);
            HdtOverlayInputManager.UnregisterElement(Slot0);
            HdtOverlayInputManager.UnregisterElement(Slot1);
            HdtOverlayInputManager.UnregisterElement(Slot2);
            HdtOverlayInputManager.UnregisterElement(Slot3);
            HdtOverlayInputManager.UnregisterElement(Slot4);
            HdtOverlayInputManager.UnregisterElement(Slot5);
            HdtOverlayInputManager.UnregisterElement(Slot6);
        }

        private void OnHudMouseEnter(object sender, MouseEventArgs e)
        {
            HdtOverlayInputManager.RegisterElement(this);
            HdtOverlayInputManager.SetClickthrough(false);
        }

        private void OnHudMouseLeave(object sender, MouseEventArgs e)
        {
            if (!_isDragging)
            {
                HdtOverlayInputManager.SetClickthrough(true);
            }
        }

        public void ApplySavedSettings()
        {
            var settings = PluginSettings.Instance;

            // Восстановление масштаба
            if (settings.OverlayScale > 0.4 && settings.OverlayScale < 2.5)
            {
                OverlayScaleTransform.ScaleX = settings.OverlayScale;
                OverlayScaleTransform.ScaleY = settings.OverlayScale;
            }

            // Восстановление положения на холсте
            if (settings.OverlayX >= 0 && Parent is Canvas parentCanvas)
            {
                Canvas.SetLeft(this, settings.OverlayX);
                Canvas.SetTop(this, settings.OverlayY);
            }
        }

        private string _lockedTab1BoardId = null;
        private string _lockedAlt2BoardId = null;
        private string _lockedAlt3BoardId = null;

        public void SetLockedBoardId(int tabIndex, string boardId)
        {
            switch (tabIndex)
            {
                case 0: _lockedTab1BoardId = boardId; break;
                case 1: _lockedAlt2BoardId = boardId; break;
                case 2: _lockedAlt3BoardId = boardId; break;
            }
            SyncLocks();
            DisplayCurrentComp();
        }

        public void ResetLocks()
        {
            _lockedTab1BoardId = null;
            _lockedAlt2BoardId = null;
            _lockedAlt3BoardId = null;
            SyncLocks();
            DisplayCurrentComp();
        }

        private void SyncLocks()
        {
            if (_recommendations == null) return;
            for (int i = 0; i < _recommendations.Count; i++)
            {
                var r = _recommendations[i];
                if (r?.Board == null) continue;
                string lockedId = (i == 0) ? _lockedTab1BoardId : (i == 1) ? _lockedAlt2BoardId : _lockedAlt3BoardId;
                r.IsLocked = !string.IsNullOrEmpty(lockedId) &&
                    (string.Equals(r.Board.Id, lockedId, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(r.Board.Archetype, lockedId, StringComparison.OrdinalIgnoreCase));
            }
        }

        public void UpdateRecommendations(List<CompRecommendation> recs)
        {
            _recommendations = (recs ?? new List<CompRecommendation>()).Take(3).ToList();
            SyncLocks();

            if (_currentIndex >= _recommendations.Count)
            {
                _currentIndex = 0;
            }

            DisplayCurrentComp();
        }

        private void DisplayCurrentComp()
        {
            if (_recommendations.Count == 0)
            {
                CtrlPageIndicator.Text = "0/0";
                Slot0.Visibility = Visibility.Collapsed; Slot0.Content = null;
                Slot1.Visibility = Visibility.Collapsed; Slot1.Content = null;
                Slot2.Visibility = Visibility.Collapsed; Slot2.Content = null;
                Slot3.Visibility = Visibility.Collapsed; Slot3.Content = null;
                Slot4.Visibility = Visibility.Collapsed; Slot4.Content = null;
                Slot5.Visibility = Visibility.Collapsed; Slot5.Content = null;
                Slot6.Visibility = Visibility.Collapsed; Slot6.Content = null;
                return;
            }

            var current = _recommendations[_currentIndex];
            CtrlPageIndicator.Text = $"{_currentIndex + 1}/{_recommendations.Count}";

            _isLocked = current.IsLocked;

            var slots = current.DisplaySlots;
            Slot0.Visibility = (slots != null && slots.Count > 0 && slots[0] != null) ? Visibility.Visible : Visibility.Collapsed;
            Slot0.Content = (slots != null && slots.Count > 0) ? slots[0] : null;

            Slot1.Visibility = (slots != null && slots.Count > 1 && slots[1] != null) ? Visibility.Visible : Visibility.Collapsed;
            Slot1.Content = (slots != null && slots.Count > 1) ? slots[1] : null;

            Slot2.Visibility = (slots != null && slots.Count > 2 && slots[2] != null) ? Visibility.Visible : Visibility.Collapsed;
            Slot2.Content = (slots != null && slots.Count > 2) ? slots[2] : null;

            Slot3.Visibility = (slots != null && slots.Count > 3 && slots[3] != null) ? Visibility.Visible : Visibility.Collapsed;
            Slot3.Content = (slots != null && slots.Count > 3) ? slots[3] : null;

            Slot4.Visibility = (slots != null && slots.Count > 4 && slots[4] != null) ? Visibility.Visible : Visibility.Collapsed;
            Slot4.Content = (slots != null && slots.Count > 4) ? slots[4] : null;

            Slot5.Visibility = (slots != null && slots.Count > 5 && slots[5] != null) ? Visibility.Visible : Visibility.Collapsed;
            Slot5.Content = (slots != null && slots.Count > 5) ? slots[5] : null;

            Slot6.Visibility = (slots != null && slots.Count > 6 && slots[6] != null) ? Visibility.Visible : Visibility.Collapsed;
            Slot6.Content = (slots != null && slots.Count > 6) ? slots[6] : null;
        }

        public void PrevComp()
        {
            if (_recommendations.Count <= 1) return;
            _currentIndex--;
            if (_currentIndex < 0) _currentIndex = _recommendations.Count - 1;
            DisplayCurrentComp();
        }

        public void NextComp()
        {
            if (_recommendations.Count <= 1) return;
            _currentIndex++;
            if (_currentIndex >= _recommendations.Count) _currentIndex = 0;
            DisplayCurrentComp();
        }

        public void ToggleLock()
        {
            if (_recommendations.Count == 0 || _currentIndex >= _recommendations.Count) return;
            var current = _recommendations[_currentIndex];
            if (current?.Board == null) return;

            int tabIndex = _currentIndex;
            bool willBeLocked = !current.IsLocked;
            current.IsLocked = willBeLocked;
            _isLocked = willBeLocked;

            SetLockedBoardId(tabIndex, willBeLocked ? current.Board.Id : null);
            LockStateChanged?.Invoke(tabIndex, current.Board.Id, willBeLocked);
        }

        public void OpenDetails()
        {
            OpenDetailsRequested?.Invoke();
        }

        private void OnPrevCompClick(object sender, MouseButtonEventArgs e)
        {
            if (e != null) e.Handled = true;
            PrevComp();
        }

        private void OnNextCompClick(object sender, MouseButtonEventArgs e)
        {
            if (e != null) e.Handled = true;
            NextComp();
        }

        private void OnArrowLeftMouseEnter(object sender, MouseEventArgs e)
        {
            if (_hudBgLeftArrowHover != null) ImgHudBg.Source = _hudBgLeftArrowHover;
        }

        private void OnArrowLeftMouseLeave(object sender, MouseEventArgs e)
        {
            if (_hudBgMain != null) ImgHudBg.Source = _hudBgMain;
        }

        private void OnArrowRightMouseEnter(object sender, MouseEventArgs e)
        {
            if (_hudBgRightArrowHover != null) ImgHudBg.Source = _hudBgRightArrowHover;
        }

        private void OnArrowRightMouseLeave(object sender, MouseEventArgs e)
        {
            if (_hudBgMain != null) ImgHudBg.Source = _hudBgMain;
        }

        private void OnDetailsMouseEnter(object sender, MouseEventArgs e)
        {
            if (BorderDetailsHoverGlow != null) BorderDetailsHoverGlow.Visibility = Visibility.Visible;
        }

        private void OnDetailsMouseLeave(object sender, MouseEventArgs e)
        {
            if (BorderDetailsHoverGlow != null) BorderDetailsHoverGlow.Visibility = Visibility.Hidden;
        }

        private void OnLockToggleClick(object sender, MouseButtonEventArgs e)
        {
            if (e != null) e.Handled = true;
            ToggleLock();
        }

        private void OnDetailsClick(object sender, MouseButtonEventArgs e)
        {
            if (e != null) e.Handled = true;
            OpenDetails();
        }

        // ==========================================================
        // Надежное перемещение мини-HUD по Canvas с фильтрацией кнопок
        // ==========================================================
        private void OnHudMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && Parent is Canvas parentCanvas)
            {
                if (IsInteractiveElement(e.OriginalSource as DependencyObject))
                {
                    return;
                }

                _isDragging = true;
                _dragStartPos = e.GetPosition(parentCanvas);

                double curLeft = Canvas.GetLeft(this);
                double curTop = Canvas.GetTop(this);

                if (double.IsNaN(curLeft)) curLeft = 50;
                if (double.IsNaN(curTop)) curTop = 15;

                _elementStartPos = new Point(curLeft, curTop);

                CaptureMouse();
                e.Handled = true;
            }
        }

        private bool IsInteractiveElement(DependencyObject dep)
        {
            while (dep != null && dep != this)
            {
                if (dep is FrameworkElement fe)
                {
                    if (fe.Cursor == Cursors.Hand ||
                        fe.Name == "BorderArrowLeft" ||
                        fe.Name == "BorderArrowRight" ||
                        fe.Name == "BorderDetails" ||
                        fe.Name == "Slot0" || fe.Name == "Slot1" || fe.Name == "Slot2" ||
                        fe.Name == "Slot3" || fe.Name == "Slot4" || fe.Name == "Slot5" || fe.Name == "Slot6")
                    {
                        return true;
                    }
                }
                dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);
            }
            return false;
        }

        private void OnHudMouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging && Parent is Canvas parentCanvas)
            {
                Point curPos = e.GetPosition(parentCanvas);
                double deltaX = curPos.X - _dragStartPos.X;
                double deltaY = curPos.Y - _dragStartPos.Y;

                double newX = _elementStartPos.X + deltaX;
                double newY = _elementStartPos.Y + deltaY;

                double canvasW = parentCanvas.ActualWidth > 0 ? parentCanvas.ActualWidth : SystemParameters.PrimaryScreenWidth;
                double canvasH = parentCanvas.ActualHeight > 0 ? parentCanvas.ActualHeight : SystemParameters.PrimaryScreenHeight;

                double maxX = Math.Max(100, canvasW - ActualWidth * OverlayScaleTransform.ScaleX);
                double maxY = Math.Max(100, canvasH - ActualHeight * OverlayScaleTransform.ScaleY);

                newX = Math.Max(0, Math.Min(maxX, newX));
                newY = Math.Max(0, Math.Min(maxY, newY));

                Canvas.SetLeft(this, newX);
                Canvas.SetTop(this, newY);
            }
        }

        private void OnHudMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                ReleaseMouseCapture();
                if (e != null) e.Handled = true;

                double left = Canvas.GetLeft(this);
                double top = Canvas.GetTop(this);

                if (!double.IsNaN(left) && !double.IsNaN(top))
                {
                    PluginSettings.Instance.OverlayX = left;
                    PluginSettings.Instance.OverlayY = top;
                    PluginSettings.Instance.Save();
                }

                if (!IsMouseOver)
                {
                    HdtOverlayInputManager.SetClickthrough(true);
                }
            }
        }

        // ==========================================================
        // Масштабирование (Ctrl + Колесо мыши)
        // ==========================================================
        private void OnHudMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e != null && (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl) || Keyboard.Modifiers.HasFlag(ModifierKeys.Control)))
            {
                e.Handled = true;
                double currentScale = OverlayScaleTransform.ScaleX;
                double delta = (e.Delta > 0) ? 0.05 : -0.05;
                double newScale = Math.Max(0.4, Math.Min(2.0, currentScale + delta));

                OverlayScaleTransform.ScaleX = newScale;
                OverlayScaleTransform.ScaleY = newScale;

                PluginSettings.Instance.OverlayScale = newScale;
                PluginSettings.Instance.Save();
            }
        }
    }
}
