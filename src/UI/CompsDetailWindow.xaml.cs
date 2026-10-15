using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HSCompsPlugin.Models;
using HSCompsPlugin.Services;

namespace HSCompsPlugin.UI
{
    public partial class CompsDetailWindow : Window
    {
        // Три основных слота вкладок
        private CompRecommendation _tab1Comp; // Статический бенчмарк Героя (фиксируется 1 раз)
        private CompRecommendation _tab2Comp; // Динамическая Альтернатива 2 (приоритет 1)
        private CompRecommendation _tab3Comp; // Динамическая Альтернатива 3 (приоритет 2)

        // Независимая фиксация столов по вкладкам (0 = Топ-1, 1 = Альтернатива 2, 2 = Альтернатива 3)
        private string _lockedTab1BoardId = null;
        private string _lockedAlt2BoardId = null;
        private string _lockedAlt3BoardId = null;

        // Зафиксированные столы Героя по расам лобби для Вкладки 1
        private Dictionary<string, CompRecommendation> _heroTribeBenchmarks = new Dictionary<string, CompRecommendation>(StringComparer.OrdinalIgnoreCase);

        // Текущий снимок состояния матча для динамических поисков
        private PlayerGameStateSnapshot _currentLiveState;

        private int _selectedTabIndex = 0; // 0 = Топ-1, 1 = Альт 2, 2 = Альт 3
        private string _selectedTribeKey = null; // null = основной стол вкладки, либо "mech", "beast" и т.д.

        private BitmapImage _tab1Active;
        private BitmapImage _tab1Inactive;
        private BitmapImage _tab2Active;
        private BitmapImage _tab2Inactive;
        private BitmapImage _tab3Active;
        private BitmapImage _tab3Inactive;

        private BitmapImage _btnMinHover;
        private BitmapImage _btnCloseHover;
        private BitmapImage _btnMinNormal;
        private BitmapImage _btnCloseNormal;

        private BitmapImage _lockActionClosedBitmap;
        private BitmapImage _lockActionOpenBitmap;

        private readonly Dictionary<string, BitmapImage> _tribeNormalSprites = new Dictionary<string, BitmapImage>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, BitmapImage> _tribeActiveSprites = new Dictionary<string, BitmapImage>(StringComparer.OrdinalIgnoreCase);

        private readonly string[] _allTribesList = new[]
        {
            "murloc", "aberration", "undead", "quilboar", "demon",
            "beast", "pirate", "elemental", "dragon", "mech"
        };

        private string[] _activeSlotTribes = Array.Empty<string>();
        private List<string> _lastKnownAvailableTribes = new List<string>();

        private bool _allowClose = false;

        public event Action<int, string, bool> LockStateChanged;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SetActiveWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WM_NCHITTEST = 0x0084;
        private const int WM_MOUSEACTIVATE = 0x0021;
        private const int HTCLIENT = 1;
        private const int MA_ACTIVATE = 1;
        private const byte VK_MENU = 0x12;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        private const uint WM_ACTIVATE = 0x0006;
        private const uint WM_KILLFOCUS = 0x0008;
        private const uint WM_ACTIVATEAPP = 0x001C;
        private const uint WM_MOUSEMOVE = 0x0200;
        private const uint WM_MOUSELEAVE = 0x02A3;

        private IntPtr _hwnd = IntPtr.Zero;
        private System.Windows.Threading.DispatcherTimer _hoverGuardTimer;
        private bool _imageRefreshPending = false;

        public CompsDetailWindow()
        {
            InitializeComponent();
            ApplySavedScale();
            LoadTextures();
            LocalImageCache.Instance.ImageDownloaded += OnImageDownloaded;

            _hoverGuardTimer = new System.Windows.Threading.DispatcherTimer(
                TimeSpan.FromMilliseconds(45),
                System.Windows.Threading.DispatcherPriority.Input,
                OnHoverGuardTick,
                Dispatcher);
            _hoverGuardTimer.Start();
        }

        private void OnHoverGuardTick(object sender, EventArgs e)
        {
            if (!IsVisible || WindowState == WindowState.Minimized) return;
            if (IsCursorInsideWindowRect())
            {
                HdtOverlayInputManager.SetClickthrough(false);
                DisarmHearthstoneHover();
                EnsureWindowActive();
            }
        }

        public void DisarmHearthstoneHover()
        {
            try
            {
                IntPtr hsHwnd = Hearthstone_Deck_Tracker.User32.GetHearthstoneWindow();
                if (hsHwnd == IntPtr.Zero || hsHwnd == _hwnd) return;

                // Отправляем клиентские координаты (-32000, -32000) и WM_MOUSELEAVE в окно Hearthstone (Unity),
                // чтобы внутренний цикл PegUI/UniversalInputManager прекратил рейкаст по картам на столе под окном плагина.
                int offCoord = (-32000 & 0xFFFF) | ((-32000 & 0xFFFF) << 16);
                PostMessage(hsHwnd, WM_MOUSEMOVE, IntPtr.Zero, new IntPtr(offCoord));
                PostMessage(hsHwnd, WM_MOUSELEAVE, IntPtr.Zero, IntPtr.Zero);
                SendMessage(hsHwnd, WM_ACTIVATE, IntPtr.Zero, _hwnd);
                SendMessage(hsHwnd, WM_KILLFOCUS, _hwnd, IntPtr.Zero);
            }
            catch { }
        }

        public void ApplySavedScale()
        {
            double scale = Math.Max(0.5, Math.Min(1.5, PluginSettings.Instance.OverlayScale));
            Width = Math.Round(1040.0 * scale);
            Height = Math.Round(640.0 * scale);
        }

        public bool IsCursorInsideWindowRect()
        {
            if (!IsVisible || WindowState == WindowState.Minimized) return false;
            try
            {
                if (_hwnd == IntPtr.Zero)
                {
                    _hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                }
                if (_hwnd == IntPtr.Zero) return false;

                if (GetCursorPos(out POINT pt) && GetWindowRect(_hwnd, out RECT rect))
                {
                    return pt.X >= rect.Left && pt.X <= rect.Right && pt.Y >= rect.Top && pt.Y <= rect.Bottom;
                }
            }
            catch { }
            return false;
        }

        public void EnsureActiveIfCursorInside()
        {
            if (IsCursorInsideWindowRect())
            {
                HdtOverlayInputManager.SetClickthrough(false);
                EnsureWindowActive();
            }
        }

        public void EnsureWindowActive()
        {
            if (!IsVisible) return;
            try
            {
                if (_hwnd == IntPtr.Zero)
                {
                    _hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                }
                if (_hwnd == IntPtr.Zero) return;

                IntPtr fgHwnd = GetForegroundWindow();
                if (fgHwnd == _hwnd) return;

                uint fgThread = GetWindowThreadProcessId(fgHwnd, out _);
                uint curThread = GetCurrentThreadId();
                bool attached = false;

                if (fgThread != 0 && fgThread != curThread)
                {
                    attached = AttachThreadInput(fgThread, curThread, true);
                }

                // Снятие блокировки ForegroundLockTimeout при переходе фокуса из оверлея HDT
                keybd_event(VK_MENU, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
                keybd_event(VK_MENU, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);

                DisarmHearthstoneHover();
                BringWindowToTop(_hwnd);
                SetForegroundWindow(_hwnd);
                SetActiveWindow(_hwnd);
                SetFocus(_hwnd);

                if (attached)
                {
                    AttachThreadInput(fgThread, curThread, false);
                }

                if (!IsActive)
                {
                    Activate();
                    Focus();
                }
            }
            catch { }
        }

        public void ForceClose()
        {
            _allowClose = true;
            LocalImageCache.Instance.ImageDownloaded -= OnImageDownloaded;
            Close();
        }

        private static bool IsSlotMatchingDownloadedUrl(BoardSlot s, string url)
        {
            if (s == null || string.IsNullOrEmpty(url)) return false;
            if (!string.IsNullOrEmpty(s.CropImageUrl) && string.Equals(s.CropImageUrl, url, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(s.FullImageUrl) && string.Equals(s.FullImageUrl, url, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(s.CardId))
            {
                string cleanId = s.CardId.EndsWith("_G", StringComparison.OrdinalIgnoreCase)
                    ? s.CardId.Substring(0, s.CardId.Length - 2)
                    : s.CardId;
                if (!string.IsNullOrEmpty(cleanId) && url.IndexOf(cleanId, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            if (s.DbfId > 0 && url.IndexOf(s.DbfId.ToString(), StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private void OnImageDownloaded(string url)
        {
            if (_imageRefreshPending) return;
            _imageRefreshPending = true;

            Application.Current?.Dispatcher?.InvokeAsync(async () =>
            {
                await System.Threading.Tasks.Task.Delay(30);
                _imageRefreshPending = false;

                try
                {
                    if (ItemsBoardShowcase?.ItemsSource is System.Collections.IEnumerable showcaseItems)
                    {
                        foreach (var item in showcaseItems)
                            if (item is BoardSlot s) s.NotifyImagesUpdated();
                    }
                    if (ItemsOwnedAvatars?.ItemsSource is System.Collections.IEnumerable ownedItems)
                    {
                        foreach (var item in ownedItems)
                            if (item is BoardSlot s) s.NotifyImagesUpdated();
                    }
                    if (ItemsMissingPriorities?.ItemsSource is System.Collections.IEnumerable missingItems)
                    {
                        foreach (var item in missingItems)
                            if (item is BoardSlot s) s.NotifyImagesUpdated();
                    }

                    var allComps = new[] { _tab1Comp, _tab2Comp, _tab3Comp };
                    foreach (var comp in allComps)
                    {
                        if (comp?.DisplaySlots != null)
                        {
                            foreach (var s in comp.DisplaySlots) s?.NotifyImagesUpdated();
                        }
                        if (comp?.MissingPriorityCards != null)
                        {
                            foreach (var s in comp.MissingPriorityCards) s?.NotifyImagesUpdated();
                        }
                    }

                    if (IsVisible)
                    {
                        ItemsBoardShowcase?.Items.Refresh();
                        ItemsOwnedAvatars?.Items.Refresh();
                        ItemsMissingPriorities?.Items.Refresh();
                    }
                }
                catch { }
            }, System.Windows.Threading.DispatcherPriority.Render);
        }

        public void RefreshDisplayedImages()
        {
            DisplaySelectedComp(forceRefresh: true);
        }

        public int RecommendationsCount => (_tab1Comp != null ? 1 : 0) + (_tab2Comp != null ? 1 : 0) + (_tab3Comp != null ? 1 : 0);

        private BitmapImage LoadImageSafe(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return null;

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string assemblyDir = Path.GetDirectoryName(typeof(CompsDetailWindow).Assembly.Location) ?? "";
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            string[] candidates = new[]
            {
                Path.Combine(assemblyDir, relativePath),
                Path.Combine(assemblyDir, "HSCompsPlugin", relativePath),
                Path.Combine(baseDir, relativePath),
                Path.Combine(baseDir, "Plugins", "HSCompsPlugin", relativePath),
                Path.Combine(appData, "HearthstoneDeckTracker", "Plugins", "HSCompsPlugin", relativePath)
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

            try
            {
                string normPath = relativePath.Replace('\\', '/').TrimStart('/');
                var packUri = new Uri($"pack://application:,,,/HSCompsPlugin;component/{normPath}", UriKind.Absolute);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = packUri;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { }

            return null;
        }

        private void LoadTextures()
        {
            try
            {
                var bg = LoadImageSafe(Path.Combine("assets", "ui", "details_window_bg.png"));
                if (bg != null) ImgWindowBg.Source = bg;

                _tab1Active = LoadImageSafe(Path.Combine("assets", "ui", "tabs", "tab_1_active.png"));
                _tab1Inactive = LoadImageSafe(Path.Combine("assets", "ui", "tabs", "tab_1_inactive.png"));
                _tab2Active = LoadImageSafe(Path.Combine("assets", "ui", "tabs", "tab_2_active.png"));
                _tab2Inactive = LoadImageSafe(Path.Combine("assets", "ui", "tabs", "tab_2_inactive.png"));
                _tab3Active = LoadImageSafe(Path.Combine("assets", "ui", "tabs", "tab_3_active.png"));
                _tab3Inactive = LoadImageSafe(Path.Combine("assets", "ui", "tabs", "tab_3_inactive.png"));

                _btnMinHover = LoadImageSafe(Path.Combine("assets", "ui", "window", "btn_min_hover.png"));
                _btnCloseHover = LoadImageSafe(Path.Combine("assets", "ui", "window", "btn_close_hover.png"));
                _btnMinNormal = LoadImageSafe(Path.Combine("assets", "ui", "window", "btn_min_normal.png"));
                _btnCloseNormal = LoadImageSafe(Path.Combine("assets", "ui", "window", "btn_close_normal.png"));

                _lockActionClosedBitmap = LoadImageSafe(Path.Combine("assets", "ui", "lock", "btn_lock_table_closed.png"));
                _lockActionOpenBitmap = LoadImageSafe(Path.Combine("assets", "ui", "lock", "btn_lock_table_open.png"));

                foreach (var t in _allTribesList)
                {
                    var norm = LoadImageSafe(Path.Combine("assets", "ui", "tribes", $"{t}_normal.png"));
                    if (norm != null) _tribeNormalSprites[t] = norm;

                    var act = LoadImageSafe(Path.Combine("assets", "ui", "tribes", $"{t}_active.png"));
                    if (act != null) _tribeActiveSprites[t] = act;
                }

                ImgBtnMin.Source = null;
                ImgBtnClose.Source = null;

                UpdateTribeVisuals();
            }
            catch { }
        }

        public void SetAvailableTribes(IEnumerable<string> tribes)
        {
            if (tribes == null) return;
            var rawList = tribes.Where(t => !string.IsNullOrEmpty(t)).ToList();
            if (rawList.Count >= 3)
            {
                _lastKnownAvailableTribes = new List<string>(rawList);
            }

            var canon = rawList.Select(CanonicalizeToSpriteKey).Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList();
            if (canon.Count >= 3 && canon.Count <= 7)
            {
                _activeSlotTribes = canon.Take(5).ToArray();
            }
            else if (canon.Count > 0 && canon.Count < 3)
            {
                _activeSlotTribes = canon.ToArray();
            }
            UpdateTribeVisuals();
        }

        public string GetLockedBoardIdForTab(int tabIndex)
        {
            switch (tabIndex)
            {
                case 0: return _lockedTab1BoardId;
                case 1: return _lockedAlt2BoardId;
                case 2: return _lockedAlt3BoardId;
                default: return null;
            }
        }

        public bool IsBoardLocked(WinningBoard board)
        {
            if (board == null) return false;
            string lockedId = GetLockedBoardIdForTab(_selectedTabIndex);
            if (string.IsNullOrEmpty(lockedId)) return false;

            return string.Equals(board.Id, lockedId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(board.Archetype, lockedId, StringComparison.OrdinalIgnoreCase);
        }

        public bool IsCurrentCompLocked()
        {
            var comp = GetCurrentActiveComp();
            return comp?.Board != null && IsBoardLocked(comp.Board);
        }

        public void SetLockedBoardId(int tabIndex, string lockedId)
        {
            switch (tabIndex)
            {
                case 0: _lockedTab1BoardId = lockedId; break;
                case 1: _lockedAlt2BoardId = lockedId; break;
                case 2: _lockedAlt3BoardId = lockedId; break;
            }

            SyncLockStateAcrossAllComps();
            UpdateLockVisual();
        }

        private void SyncLockStateAcrossAllComps()
        {
            if (_tab1Comp?.Board != null)
            {
                _tab1Comp.IsLocked = !string.IsNullOrEmpty(_lockedTab1BoardId) &&
                    (string.Equals(_tab1Comp.Board.Id, _lockedTab1BoardId, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(_tab1Comp.Board.Archetype, _lockedTab1BoardId, StringComparison.OrdinalIgnoreCase));
            }

            if (_tab2Comp?.Board != null)
            {
                _tab2Comp.IsLocked = !string.IsNullOrEmpty(_lockedAlt2BoardId) &&
                    (string.Equals(_tab2Comp.Board.Id, _lockedAlt2BoardId, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(_tab2Comp.Board.Archetype, _lockedAlt2BoardId, StringComparison.OrdinalIgnoreCase));
            }

            if (_tab3Comp?.Board != null)
            {
                _tab3Comp.IsLocked = !string.IsNullOrEmpty(_lockedAlt3BoardId) &&
                    (string.Equals(_tab3Comp.Board.Id, _lockedAlt3BoardId, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(_tab3Comp.Board.Archetype, _lockedAlt3BoardId, StringComparison.OrdinalIgnoreCase));
            }

            if (_heroTribeBenchmarks != null)
            {
                foreach (var bench in _heroTribeBenchmarks.Values)
                {
                    if (bench?.Board != null)
                    {
                        bench.IsLocked = !string.IsNullOrEmpty(_lockedTab1BoardId) &&
                            (string.Equals(bench.Board.Id, _lockedTab1BoardId, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(bench.Board.Archetype, _lockedTab1BoardId, StringComparison.OrdinalIgnoreCase));
                    }
                }
            }
        }

        private void UpdateLockVisual()
        {
            bool locked = IsCurrentCompLocked();
            if (ImgLockAction != null)
            {
                ImgLockAction.Source = locked ? _lockActionOpenBitmap : _lockActionClosedBitmap;
            }
            if (BorderLockAction != null)
            {
                BorderLockAction.ToolTip = locked 
                    ? "Открепить стол (вернуть авто-рекомендацию)" 
                    : "Закрепить целевой стол до конца матча";
            }
        }

        public void SetHeroBenchmarks(CompRecommendation heroTop1, Dictionary<string, CompRecommendation> tribeBenchmarks, IEnumerable<string> availableTribes = null)
        {
            if (availableTribes != null)
            {
                SetAvailableTribes(availableTribes);
            }

            if (string.IsNullOrEmpty(_lockedTab1BoardId) || _tab1Comp == null)
            {
                _tab1Comp = heroTop1;
            }
            _heroTribeBenchmarks = tribeBenchmarks ?? new Dictionary<string, CompRecommendation>(StringComparer.OrdinalIgnoreCase);

            SyncLockStateAcrossAllComps();

            if (_selectedTabIndex == 0)
            {
                UpdateTabVisuals();
                DisplaySelectedComp(forceRefresh: true);
            }
        }

        public void UpdateDynamicAlternatives(CompRecommendation tab1, CompRecommendation alt2, CompRecommendation alt3, PlayerGameStateSnapshot liveState)
        {
            _currentLiveState = liveState;
            if (tab1 != null) _tab1Comp = tab1;
            _tab2Comp = alt2;
            _tab3Comp = alt3;

            SyncLockStateAcrossAllComps();

            UpdateTabVisuals();
            DisplaySelectedComp(forceRefresh: true);
        }

        public void UpdateDynamicAlternatives(CompRecommendation alt2, CompRecommendation alt3, PlayerGameStateSnapshot liveState)
        {
            UpdateDynamicAlternatives(null, alt2, alt3, liveState);
        }

        public void UpdateRecommendations(List<CompRecommendation> recs, IEnumerable<string> availableTribes = null)
        {
            if (availableTribes != null)
            {
                SetAvailableTribes(availableTribes);
            }

            _tab1Comp = (recs != null && recs.Count > 0) ? recs[0] : null;
            _tab2Comp = (recs != null && recs.Count > 1) ? recs[1] : null;
            _tab3Comp = (recs != null && recs.Count > 2) ? recs[2] : null;

            SyncLockStateAcrossAllComps();

            UpdateTabVisuals();
            DisplaySelectedComp(forceRefresh: true);
        }

        public void ResetMatch()
        {
            _lockedTab1BoardId = null;
            _lockedAlt2BoardId = null;
            _lockedAlt3BoardId = null;
            _tab1Comp = null;
            _tab2Comp = null;
            _tab3Comp = null;
            _heroTribeBenchmarks.Clear();
            _selectedTabIndex = 0;
            _selectedTribeKey = null;
            _lastKnownAvailableTribes.Clear();
            _activeSlotTribes = Array.Empty<string>();
            UpdateTribeVisuals();
            DisplaySelectedComp(forceRefresh: true);
        }

        private CompRecommendation GetCurrentActiveComp()
        {
            // 1. Если кликнута вкладка конкретного племени
            if (!string.IsNullOrEmpty(_selectedTribeKey))
            {
                string tribeRu = SpriteKeyToRussian(_selectedTribeKey);

                if (_selectedTabIndex == 0)
                {
                    // Для Вкладки 1: если для этой вкладки уже зафиксирован стол этого племени, возвращаем его с живой оценкой
                    string lockedForThisTab = GetLockedBoardIdForTab(0);
                    if (!string.IsNullOrEmpty(lockedForThisTab))
                    {
                        var lockedBoard = VectorSearchEngine.Instance.GetBoardById(lockedForThisTab);
                        if (lockedBoard != null && VectorSearchEngine.TribesMatch(lockedBoard.PrimaryTribe, tribeRu))
                        {
                            var eval = _currentLiveState != null
                                ? VectorSearchEngine.Instance.EvaluateBoardById(lockedForThisTab, _currentLiveState, isLocked: true)
                                : null;
                            if (eval != null)
                            {
                                eval.IsLocked = true;
                                return eval;
                            }
                        }
                    }

                    // Статический бенчмарк Героя за эту расу с живой оценкой карт
                    if (_heroTribeBenchmarks != null && _heroTribeBenchmarks.TryGetValue(tribeRu, out var bench))
                    {
                        if (bench?.Board != null && VectorSearchEngine.TribesMatch(bench.Board.PrimaryTribe, tribeRu))
                        {
                            var eval = _currentLiveState != null
                                ? VectorSearchEngine.Instance.EvaluateBoardById(bench.Board.Id, _currentLiveState, isLocked: IsBoardLocked(bench.Board))
                                : bench;
                            if (eval != null)
                            {
                                eval.IsLocked = IsBoardLocked(eval.Board);
                                return eval;
                            }
                        }
                    }
                    if (_heroTribeBenchmarks != null)
                    {
                        foreach (var kvp in _heroTribeBenchmarks)
                        {
                            if (VectorSearchEngine.TribesMatch(kvp.Key, tribeRu) &&
                                kvp.Value?.Board != null &&
                                VectorSearchEngine.TribesMatch(kvp.Value.Board.PrimaryTribe, tribeRu))
                            {
                                var eval = _currentLiveState != null
                                    ? VectorSearchEngine.Instance.EvaluateBoardById(kvp.Value.Board.Id, _currentLiveState, isLocked: IsBoardLocked(kvp.Value.Board))
                                    : kvp.Value;
                                if (eval != null)
                                {
                                    eval.IsLocked = IsBoardLocked(eval.Board);
                                    return eval;
                                }
                            }
                        }
                    }

                    // Если у Героя нет персонального бенчмарка для этого племени — находим лучший стол этого племени в лобби
                    var bestLobbyTribeComp = VectorSearchEngine.Instance.FindDynamicCompForTribe(tribeRu, _currentLiveState);
                    if (bestLobbyTribeComp != null)
                    {
                        if (bestLobbyTribeComp.Board != null) bestLobbyTribeComp.IsLocked = IsBoardLocked(bestLobbyTribeComp.Board);
                        return bestLobbyTribeComp;
                    }
                }
                else
                {
                    // Для Вкладок 2 и 3: если для этой вкладки уже зафиксирован стол этого племени, возвращаем его
                    string lockedForThisTab = GetLockedBoardIdForTab(_selectedTabIndex);
                    if (!string.IsNullOrEmpty(lockedForThisTab))
                    {
                        var lockedBoard = VectorSearchEngine.Instance.GetBoardById(lockedForThisTab);
                        if (lockedBoard != null && VectorSearchEngine.TribesMatch(lockedBoard.PrimaryTribe, tribeRu))
                        {
                            var eval = _currentLiveState != null 
                                ? VectorSearchEngine.Instance.EvaluateBoardById(lockedForThisTab, _currentLiveState, isLocked: true)
                                : null;
                            if (eval != null)
                            {
                                eval.IsLocked = true;
                                return eval;
                            }
                        }
                    }

                    // Динамически находим лучший стол выбранного племени
                    var exclude = new HashSet<string>();
                    if (_tab1Comp?.Board?.Id != null) exclude.Add(_tab1Comp.Board.Id);
                    if (_selectedTabIndex == 1 && _tab3Comp?.Board?.Id != null) exclude.Add(_tab3Comp.Board.Id);
                    if (_selectedTabIndex == 2 && _tab2Comp?.Board?.Id != null) exclude.Add(_tab2Comp.Board.Id);

                    var dynComp = VectorSearchEngine.Instance.FindDynamicCompForTribe(tribeRu, _currentLiveState, exclude);
                    if (dynComp == null)
                    {
                        // Попробуем без исключений, если все столы этого племени были в исключениях
                        dynComp = VectorSearchEngine.Instance.FindDynamicCompForTribe(tribeRu, _currentLiveState, null);
                    }
                    if (dynComp != null)
                    {
                        if (dynComp.Board != null) dynComp.IsLocked = IsBoardLocked(dynComp.Board);
                        return dynComp;
                    }
                }
            }

            // 2. Основной стол выбранной вкладки
            CompRecommendation mainComp;
            switch (_selectedTabIndex)
            {
                case 0:
                    if (!string.IsNullOrEmpty(_lockedTab1BoardId))
                    {
                        if (_tab1Comp?.Board == null || !string.Equals(_tab1Comp.Board.Id, _lockedTab1BoardId, StringComparison.OrdinalIgnoreCase))
                        {
                            var evalLocked = _currentLiveState != null
                                ? VectorSearchEngine.Instance.EvaluateBoardById(_lockedTab1BoardId, _currentLiveState, isLocked: true)
                                : null;
                            if (evalLocked != null)
                            {
                                evalLocked.IsLocked = true;
                                _tab1Comp = evalLocked;
                            }
                        }
                    }
                    mainComp = _tab1Comp;
                    break;
                case 1: mainComp = _tab2Comp; break;
                case 2: mainComp = _tab3Comp; break;
                default: mainComp = _tab1Comp; break;
            }

            if (mainComp?.Board != null)
            {
                mainComp.IsLocked = IsBoardLocked(mainComp.Board);
            }
            return mainComp;
        }

        private void UpdateTabVisuals()
        {
            if (ImgTab1 != null)
                ImgTab1.Source = (_selectedTabIndex == 0) ? _tab1Active : null;

            if (ImgTab2 != null)
                ImgTab2.Source = (_selectedTabIndex == 1) ? _tab2Active : null;

            if (ImgTab3 != null)
                ImgTab3.Source = (_selectedTabIndex == 2) ? _tab3Active : null;
        }

        private void DisplaySelectedComp(bool forceRefresh = false)
        {
            RenderOptions.SetBitmapScalingMode(
                this,
                PluginSettings.Instance.IsHighPerformanceMode
                    ? BitmapScalingMode.Linear
                    : BitmapScalingMode.HighQuality);

            var comp = GetCurrentActiveComp();

            if (comp == null || comp.Board == null)
            {
                ItemsBoardShowcase.ItemsSource = null;
                ItemsOwnedAvatars.ItemsSource = null;
                ItemsMissingPriorities.ItemsSource = null;
                CtrlOwnedCount.Text = "0/7";
                CtrlMissingCount.Text = "0";
                CtrlAvgPlacement.Text = "-";
                CtrlTop1Rate.Text = "-";
                UpdateTribeVisuals();
                UpdateLockVisual();
                return;
            }

            // Обновление кнопок-спрайтов племен
            UpdateTribeVisuals();

            if (forceRefresh)
            {
                ItemsBoardShowcase.ItemsSource = null;
                ItemsOwnedAvatars.ItemsSource = null;
                ItemsMissingPriorities.ItemsSource = null;
            }

            // 7 карт основного стола
            ItemsBoardShowcase.ItemsSource = comp.DisplaySlots;

            // Левая панель: ЧТО СОБРАНО (строго N/7 и до 7 карт)
            var owned = comp.DisplaySlots.Where(s => s.Status == SlotStatus.OnBoard || s.Status == SlotStatus.InHand).ToList();
            CtrlOwnedCount.Text = $"{owned.Count}/7";
            ItemsOwnedAvatars.ItemsSource = owned.Take(7).ToList();

            // Правая панель: КЛЮЧЕВЫЕ КАРТЫ ДЛЯ ПОИСКА (чистое одиночное число M без дроби и ровно M карт!)
            var missing = comp.MissingPriorityCards ?? new List<BoardSlot>();
            CtrlMissingCount.Text = $"{missing.Count}";
            ItemsMissingPriorities.ItemsSource = missing;

            // Мета-статистика в нижнем баннере
            CtrlAvgPlacement.Text = comp.Board.AveragePlacement.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            CtrlTop1Rate.Text = $"{(comp.Board.Top1Rate * 100):0}";

            // Кнопка фиксации стола (надежно рассчитывается через IsBoardLocked)
            bool locked = IsBoardLocked(comp.Board);
            comp.IsLocked = locked;
            if (ImgLockAction != null)
            {
                ImgLockAction.Source = locked ? _lockActionOpenBitmap : _lockActionClosedBitmap;
            }
        }

        private Image GetSlotImage(int index)
        {
            switch (index)
            {
                case 0: return ImgTribeSlot0;
                case 1: return ImgTribeSlot1;
                case 2: return ImgTribeSlot2;
                case 3: return ImgTribeSlot3;
                case 4: return ImgTribeSlot4;
                default: return null;
            }
        }

        private Border GetSlotBorder(int index)
        {
            switch (index)
            {
                case 0: return TribeSlot0;
                case 1: return TribeSlot1;
                case 2: return TribeSlot2;
                case 3: return TribeSlot3;
                case 4: return TribeSlot4;
                default: return null;
            }
        }

        private static readonly Dictionary<string, double> _tribeAspectRatios = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            { "aberration", 993.0 / 138.0 },
            { "beast", 544.0 / 123.0 },
            { "demon", 780.0 / 127.0 },
            { "dragon", 926.0 / 119.0 },
            { "elemental", 1037.0 / 111.0 },
            { "mech", 1014.0 / 115.0 },
            { "murloc", 876.0 / 133.0 },
            { "pirate", 716.0 / 122.0 },
            { "quilboar", 1035.0 / 120.0 },
            { "undead", 748.0 / 118.0 }
        };

        private void ApplyDynamicTribesLayout()
        {
            const double W_plank = 1360.0;
            const double X_center = 780.0;
            const double Y_center = 201.0;

            double arSum = 0;
            double[] ars = new double[5];
            for (int i = 0; i < 5; i++)
            {
                string tribe = (i < _activeSlotTribes.Length) ? _activeSlotTribes[i] : "murloc";
                double ar = 7.0;
                if (_tribeNormalSprites.TryGetValue(tribe, out var bmp) && bmp != null && bmp.PixelHeight > 0)
                {
                    ar = (double)bmp.PixelWidth / bmp.PixelHeight;
                }
                else if (_tribeAspectRatios.TryGetValue(tribe, out double staticAr))
                {
                    ar = staticAr;
                }
                ars[i] = ar;
                arSum += ar;
            }

            double H = Math.Min(35.0, (W_plank - 4 * 22.0) / arSum);
            H = Math.Round(H);

            double[] widths = new double[5];
            double totalWordsWidth = 0;
            for (int i = 0; i < 5; i++)
            {
                widths[i] = Math.Round(H * ars[i]);
                totalWordsWidth += widths[i];
            }

            double gap = (W_plank - totalWordsWidth) / 4.0;
            double startX;
            if (gap > 42.0)
            {
                gap = 42.0;
                double totalSpan = totalWordsWidth + 4.0 * gap;
                startX = Math.Round(X_center - totalSpan / 2.0);
            }
            else
            {
                startX = 100.0;
            }

            double currentX = startX;
            double top = Math.Round(Y_center - H / 2.0);

            for (int i = 0; i < 5; i++)
            {
                var border = GetSlotBorder(i);
                var img = GetSlotImage(i);

                if (border != null)
                {
                    border.Width = widths[i];
                    border.Height = H;
                    Canvas.SetLeft(border, Math.Round(currentX));
                    Canvas.SetTop(border, top);
                }
                if (img != null)
                {
                    img.Width = widths[i];
                    img.Height = H;
                    img.Stretch = Stretch.Fill;
                }
                currentX += widths[i] + gap;
            }
        }

        private string CanonicalizeToSpriteKey(string tribe)
        {
            if (string.IsNullOrEmpty(tribe)) return null;
            string lower = tribe.Trim().ToLowerInvariant();
            if (lower.StartsWith("все") || lower.Contains("menagerie") || lower == "all") return null;
            if (lower.StartsWith("мурл") || lower.Contains("murloc")) return "murloc";
            if (lower.StartsWith("аббер") || lower.Contains("aberr")) return "aberration";
            if (lower.StartsWith("неж") || lower.Contains("undead")) return "undead";
            if (lower.StartsWith("свин") || lower.Contains("quilboar") || lower.Contains("boar")) return "quilboar";
            if (lower.StartsWith("дем") || lower.Contains("demon")) return "demon";
            if (lower.StartsWith("звер") || lower.Contains("beast")) return "beast";
            if (lower.StartsWith("пир") || lower.Contains("pirate")) return "pirate";
            if (lower.StartsWith("элем") || lower.Contains("elemental")) return "elemental";
            if (lower.StartsWith("драк") || lower.Contains("dragon")) return "dragon";
            if (lower.StartsWith("мех") || lower.Contains("mech")) return "mech";
            return null;
        }

        private string SpriteKeyToRussian(string spriteKey)
        {
            switch (spriteKey?.ToLowerInvariant())
            {
                case "mech": return "Механизм";
                case "beast": return "Зверь";
                case "murloc": return "Мурлок";
                case "undead": return "Нежить";
                case "demon": return "Демон";
                case "elemental": return "Элементаль";
                case "aberration": return "Абберация";
                case "pirate": return "Пират";
                case "dragon": return "Дракон";
                case "quilboar": return "Свинобраз";
                default: return "Все";
            }
        }

        private void UpdateTribeVisuals()
        {
            string highlightKey = _selectedTribeKey;

            // Если конкретное племя не выбрано, подсвечиваем племя текущего отображаемого стола
            if (string.IsNullOrEmpty(highlightKey))
            {
                var comp = GetCurrentActiveComp();
                if (comp?.Board?.PrimaryTribe != null)
                {
                    highlightKey = CanonicalizeToSpriteKey(comp.Board.PrimaryTribe);
                }
            }

            for (int i = 0; i < 5; i++)
            {
                var img = GetSlotImage(i);
                if (img == null) continue;

                if (i < _activeSlotTribes.Length)
                {
                    string tribe = _activeSlotTribes[i];
                    bool isSelected = string.Equals(tribe, highlightKey, StringComparison.OrdinalIgnoreCase);

                    if (isSelected && _tribeActiveSprites.TryGetValue(tribe, out var act))
                    {
                        img.Source = act;
                    }
                    else if (_tribeNormalSprites.TryGetValue(tribe, out var norm))
                    {
                        img.Source = norm;
                    }
                    else
                    {
                        img.Source = null;
                    }
                }
                else
                {
                    img.Source = null;
                }
            }

            ApplyDynamicTribesLayout();
        }

        private void SetShowcaseHighlightForSlot(BoardSlot hoveredSlot, bool highlight)
        {
            var comp = GetCurrentActiveComp();
            if (comp?.DisplaySlots == null) return;

            foreach (var s in comp.DisplaySlots)
            {
                if (s == null) continue;
                if (!highlight)
                {
                    s.IsHighlighted = false;
                    continue;
                }

                bool isMatch = ReferenceEquals(s, hoveredSlot)
                               || (hoveredSlot != null && s.DbfId > 0 && s.DbfId == hoveredSlot.DbfId)
                               || (hoveredSlot != null && !string.IsNullOrEmpty(s.CardId) && string.Equals(s.CardId, hoveredSlot.CardId, StringComparison.OrdinalIgnoreCase));
                s.IsHighlighted = isMatch;
            }
        }

        private void OnSubCardMouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is BoardSlot slot)
            {
                SetShowcaseHighlightForSlot(slot, true);
            }
        }

        private void OnSubCardMouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is BoardSlot slot)
            {
                SetShowcaseHighlightForSlot(slot, false);
            }
        }

        private void OnTribeSlotEnter(object sender, MouseEventArgs e)
        {
            if (sender is Border border && int.TryParse(border.Tag?.ToString(), out int slotIndex))
            {
                if (slotIndex >= 0 && slotIndex < _activeSlotTribes.Length)
                {
                    string tribe = _activeSlotTribes[slotIndex];
                    if (_tribeActiveSprites.TryGetValue(tribe, out var act))
                    {
                        var img = GetSlotImage(slotIndex);
                        if (img != null) img.Source = act;
                    }
                }
            }
        }

        private void OnTribeSlotLeave(object sender, MouseEventArgs e)
        {
            if (sender is Border border && int.TryParse(border.Tag?.ToString(), out int slotIndex))
            {
                if (slotIndex >= 0 && slotIndex < _activeSlotTribes.Length)
                {
                    string tribe = _activeSlotTribes[slotIndex];
                    string currentKey = _selectedTribeKey;
                    if (string.IsNullOrEmpty(currentKey))
                    {
                        var comp = GetCurrentActiveComp();
                        if (comp?.Board?.PrimaryTribe != null)
                        {
                            currentKey = CanonicalizeToSpriteKey(comp.Board.PrimaryTribe);
                        }
                    }

                    if (!string.Equals(tribe, currentKey, StringComparison.OrdinalIgnoreCase))
                    {
                        if (_tribeNormalSprites.TryGetValue(tribe, out var norm))
                        {
                            var img = GetSlotImage(slotIndex);
                            if (img != null) img.Source = norm;
                        }
                    }
                }
            }
        }

        private void OnTribeSlotClick(object sender, MouseButtonEventArgs e)
        {
            if (e != null) e.Handled = true;
            if (sender is Border border && int.TryParse(border.Tag?.ToString(), out int slotIndex))
            {
                if (slotIndex >= 0 && slotIndex < _activeSlotTribes.Length)
                {
                    string clickedTribe = _activeSlotTribes[slotIndex];

                    // Повторный клик по активному племени сбрасывает фильтрацию и возвращает главный стол
                    if (string.Equals(_selectedTribeKey, clickedTribe, StringComparison.OrdinalIgnoreCase))
                    {
                        _selectedTribeKey = null;
                    }
                    else
                    {
                        _selectedTribeKey = clickedTribe;
                    }

                    UpdateTribeVisuals();
                    DisplaySelectedComp(forceRefresh: true);
                }
            }
        }

        public void SelectTab(int index)
        {
            if (index < 0 || index > 2) return;
            _selectedTabIndex = index;
            _selectedTribeKey = null; // При смене вкладки возвращаем основной стол этой вкладки
            UpdateTabVisuals();
            DisplaySelectedComp(forceRefresh: true);
        }

        private void OnTab1Click(object sender, MouseButtonEventArgs e)
        {
            if (e != null) e.Handled = true;
            SelectTab(0);
        }

        private void OnTab2Click(object sender, MouseButtonEventArgs e)
        {
            if (e != null) e.Handled = true;
            SelectTab(1);
        }

        private void OnTab3Click(object sender, MouseButtonEventArgs e)
        {
            if (e != null) e.Handled = true;
            SelectTab(2);
        }

        public void ToggleLock()
        {
            var comp = GetCurrentActiveComp();
            if (comp == null || comp.Board == null) return;

            int currentTab = _selectedTabIndex;
            bool isCurrentlyLocked = IsBoardLocked(comp.Board);
            bool willBeLocked = !isCurrentlyLocked;

            comp.IsLocked = willBeLocked;
            string newLockedId = willBeLocked ? comp.Board.Id : null;

            SetLockedBoardId(currentTab, newLockedId);

            if (currentTab == 0 && willBeLocked)
            {
                _tab1Comp = comp;
            }

            SyncLockStateAcrossAllComps();
            UpdateLockVisual();
            DisplaySelectedComp(forceRefresh: true);

            LockStateChanged?.Invoke(currentTab, comp.Board.Id, willBeLocked);
        }

        private void OnLockCompClick(object sender, MouseButtonEventArgs e)
        {
            if (e != null) e.Handled = true;
            ToggleLock();
        }

        private void OnWindowMouseEnter(object sender, MouseEventArgs e)
        {
            HdtOverlayInputManager.SetClickthrough(false);
            EnsureWindowActive();
        }

        private void OnWindowMouseLeave(object sender, MouseEventArgs e)
        {
            if (IsCursorInsideWindowRect())
            {
                // Курсор все еще внутри габаритов окна (например, над всплывающим тултипом карты)
                return;
            }

            HdtOverlayInputManager.SetClickthrough(true);
        }

        private void OnWindowPreviewMouseMove(object sender, MouseEventArgs e)
        {
            HdtOverlayInputManager.SetClickthrough(false);
            EnsureWindowActive();
        }

        private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            HdtOverlayInputManager.SetClickthrough(false);
            EnsureWindowActive();
        }

        private void OnWindowPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e != null && (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl) || Keyboard.Modifiers.HasFlag(ModifierKeys.Control)))
            {
                e.Handled = true;
                double currentScale = PluginSettings.Instance.OverlayScale;
                double delta = (e.Delta > 0) ? 0.05 : -0.05;
                double newScale = Math.Max(0.5, Math.Min(1.5, Math.Round((currentScale + delta) * 20.0) / 20.0));

                PluginSettings.Instance.OverlayScale = newScale;
                PluginSettings.Instance.Save();
                ApplySavedScale();
            }
        }

        private void OnWindowMouseMove(object sender, MouseEventArgs e)
        {
            if (e != null) e.Handled = true;
        }

        private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && (e.OriginalSource == CanvasRoot || e.OriginalSource == ImgWindowBg))
            {
                try
                {
                    if (!IsActive) Activate();
                    DragMove();
                }
                catch { }
            }
        }

        private void OnMinMouseEnter(object sender, MouseEventArgs e)
        {
            if (_btnMinHover != null) ImgBtnMin.Source = _btnMinHover;
        }

        private void OnMinMouseLeave(object sender, MouseEventArgs e)
        {
            ImgBtnMin.Source = null;
        }

        private void OnCloseMouseEnter(object sender, MouseEventArgs e)
        {
            if (_btnCloseHover != null) ImgBtnClose.Source = _btnCloseHover;
        }

        private void OnCloseMouseLeave(object sender, MouseEventArgs e)
        {
            ImgBtnClose.Source = null;
        }

        public new void Hide()
        {
            HdtOverlayInputManager.SetClickthrough(true);
            base.Hide();
            try
            {
                Hearthstone_Deck_Tracker.User32.BringHsToForeground();
            }
            catch { }
        }

        private void OnMinimizeClick(object sender, MouseButtonEventArgs e)
        {
            if (e != null) e.Handled = true;
            Hide();
        }

        private void OnCloseClick(object sender, MouseButtonEventArgs e)
        {
            if (e != null) e.Handled = true;
            Hide();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            bool isAppShuttingDown = Application.Current == null ||
                                     Application.Current.Dispatcher == null ||
                                     Application.Current.Dispatcher.HasShutdownStarted ||
                                     Application.Current.Dispatcher.HasShutdownFinished;

            if (!_allowClose && !isAppShuttingDown)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            base.OnClosing(e);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                _hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (_hwnd != IntPtr.Zero)
                {
                    int exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
                    exStyle &= ~WS_EX_TRANSPARENT;
                    exStyle &= ~WS_EX_NOACTIVATE;
                    SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle);

                    var source = System.Windows.Interop.HwndSource.FromHwnd(_hwnd);
                    source?.AddHook(WndProc);
                }
            }
            catch { }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_MOUSEACTIVATE)
            {
                handled = true;
                return new IntPtr(MA_ACTIVATE);
            }
            return IntPtr.Zero;
        }
    }
}
