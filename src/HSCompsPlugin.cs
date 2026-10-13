using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Hearthstone_Deck_Tracker.API;
using Hearthstone_Deck_Tracker.Plugins;
using HSCompsPlugin.Models;
using HSCompsPlugin.Services;
using HSCompsPlugin.UI;
using PluginSettings = HSCompsPlugin.Services.PluginSettings;

namespace HSCompsPlugin
{
    public class HSCompsPluginMain : IPlugin
    {
        public string Name => "HS Comps Plugin";
        public string Description => "RAG / Векторный советник победных столов для Battlegrounds на актуальном патче";
        public string ButtonText => "Настройки";
        public string Author => "ZadroTube";
        public Version Version => new Version(3, 0, 0);

        public MenuItem MenuItem { get; private set; }

        private CompsMiniOverlay _miniOverlay;
        private CompsDetailWindow _detailWindow;
        private PluginSettingsWindow _settingsWindow;
        private MenuItem _menuItemShowInBgs;
        private MenuItem _menuItemShowInMainMenu;
        private MenuItem _menuItemDontHideMiniOnDetails;

        private bool _isDemoMode = false;
        private string _lockedTab1BoardId = null;
        private string _lockedAlt2BoardId = null;
        private string _lockedAlt3BoardId = null;
        private DateTime _lastUpdateTime = DateTime.MinValue;
        private string _lastProcessedDemoLockId = "__none__";
        private int _lastLiveStateHash = 0;

        // Архитектура: Однократная фиксация бенчмарка Героя (Вкладка 1)
        private bool _isHeroLocked = false;
        private string _lockedHeroCardId = null;
        private CompRecommendation _heroBenchmarkComp = null;
        private Dictionary<string, CompRecommendation> _heroTribeBenchmarks = new Dictionary<string, CompRecommendation>(StringComparer.OrdinalIgnoreCase);

        // Динамические альтернативы в реальном времени (Вкладки 2 и 3)
        private CompRecommendation _alt2Comp = null;
        private CompRecommendation _alt3Comp = null;

        // Асинхронный фоновый воркер вычислений (Zero UI blocking)
        private System.Threading.CancellationTokenSource _searchCts = null;
        private int _activeSearchId = 0;

        public void OnLoad()
        {
            try
            {
                // 1. Инициализация официальной базы карт (Zero Hallucination)
                CardDatabaseService.Instance.LoadDatabase();

                // 2. Инициализация векторной базы победных столов
                VectorSearchEngine.Instance.LoadDatabase();

                // 3. Создание UI компонентов
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _miniOverlay = new CompsMiniOverlay();
                    _detailWindow = new CompsDetailWindow();

                    _miniOverlay.OpenDetailsRequested += ToggleDetailWindow;
                    _miniOverlay.LockStateChanged += OnCompLockChanged;
                    _detailWindow.LockStateChanged += OnCompLockChanged;
                    _detailWindow.IsVisibleChanged += (s, e) => ApplySettingsState();

                    // Добавление оверлея на Canvas HDT с учетом сохраненных координат
                    if (Core.OverlayCanvas != null)
                    {
                        var settings = PluginSettings.Instance;
                        double top = settings.OverlayY >= 0 ? settings.OverlayY : 15;
                        double left = settings.OverlayX >= 0 ? settings.OverlayX : Math.Max(50, (Core.OverlayCanvas.ActualWidth - 813) / 2);

                        Canvas.SetTop(_miniOverlay, top);
                        Canvas.SetLeft(_miniOverlay, left);
                        _miniOverlay.ApplySavedSettings();

                        bool shouldBeVisible = (GameStateReader.IsInBattlegrounds && settings.ShowMiniOverlay)
                                            || (GameStateReader.IsInMainMenu && settings.ShowInMainMenu);
                        _miniOverlay.Visibility = shouldBeVisible ? Visibility.Visible : Visibility.Collapsed;
                        Core.OverlayCanvas.Children.Add(_miniOverlay);
                    }
                });

                // 4. Меню плагина в HDT
                BuildMenu();

                // 5. Подписка на оперативные игровые события HDT для мгновенного обновления без задержек
                GameEvents.OnGameStart.Add(OnGameStart);
                GameEvents.OnGameEnd.Add(OnGameEnd);
                GameEvents.OnTurnStart.Add(OnTurnStart);
                GameEvents.OnPlayerPlay.Add(OnCardEvent);
                GameEvents.OnPlayerGet.Add(OnCardEvent);
                GameEvents.OnPlayerCreateInPlay.Add(OnCardEvent);
                GameEvents.OnPlayerPlayToGraveyard.Add(OnCardEvent);
                GameEvents.OnPlayerHandDiscard.Add(OnCardEvent);
                GameEvents.OnInMenu.Add(OnInMenuEvent);
                GameEvents.OnModeChanged.Add(OnModeChangedEvent);
                GameStateReader.LobbyTribesUpdated += OnLobbyTribesUpdated;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HSCompsPlugin] Ошибка OnLoad: {ex}");
            }
        }

        private void BuildMenu()
        {
            MenuItem = new MenuItem { Header = "HS Comps (Поля сражений)" };

            var settings = PluginSettings.Instance;

            _menuItemShowInBgs = new MenuItem 
            { 
                Header = "Показывать мини-HUD в бою", 
                IsCheckable = true, 
                IsChecked = settings.ShowMiniOverlay 
            };
            _menuItemShowInBgs.Click += (s, e) =>
            {
                settings.ShowMiniOverlay = _menuItemShowInBgs.IsChecked;
                settings.Save();
                ApplySettingsState();
            };
            MenuItem.Items.Add(_menuItemShowInBgs);

            _menuItemShowInMainMenu = new MenuItem 
            { 
                Header = "Показывать мини-HUD в главном меню игры", 
                IsCheckable = true, 
                IsChecked = settings.ShowInMainMenu 
            };
            _menuItemShowInMainMenu.Click += (s, e) =>
            {
                settings.ShowInMainMenu = _menuItemShowInMainMenu.IsChecked;
                settings.Save();
                ApplySettingsState();
            };
            MenuItem.Items.Add(_menuItemShowInMainMenu);

            _menuItemDontHideMiniOnDetails = new MenuItem 
            { 
                Header = "Не скрывать мини-HUD при открытии подробного окна", 
                IsCheckable = true, 
                IsChecked = settings.DontHideMiniOverlayOnDetails 
            };
            _menuItemDontHideMiniOnDetails.Click += (s, e) =>
            {
                settings.DontHideMiniOverlayOnDetails = _menuItemDontHideMiniOnDetails.IsChecked;
                settings.Save();
                ApplySettingsState();
            };
            MenuItem.Items.Add(_menuItemDontHideMiniOnDetails);

            MenuItem.Items.Add(new Separator());

            var itemSettings = new MenuItem { Header = "⚙️ Настройки плагина..." };
            itemSettings.Click += (s, e) => OpenSettingsWindow();
            MenuItem.Items.Add(itemSettings);

            var itemDetails = new MenuItem { Header = "📊 Открыть подробное окно анализа" };
            itemDetails.Click += (s, e) => ToggleDetailWindow();
            MenuItem.Items.Add(itemDetails);

            var itemDemo = new MenuItem { Header = "🧪 Запустить тестовый стенд (Демо)" };
            itemDemo.Click += (s, e) => ToggleDemoMode();
            MenuItem.Items.Add(itemDemo);
        }

        private void UpdateMenuCheckedStates()
        {
            var settings = PluginSettings.Instance;
            if (_menuItemShowInBgs != null) _menuItemShowInBgs.IsChecked = settings.ShowMiniOverlay;
            if (_menuItemShowInMainMenu != null) _menuItemShowInMainMenu.IsChecked = settings.ShowInMainMenu;
            if (_menuItemDontHideMiniOnDetails != null) _menuItemDontHideMiniOnDetails.IsChecked = settings.DontHideMiniOverlayOnDetails;
        }

        public void OpenSettingsWindow()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_settingsWindow == null || !_settingsWindow.IsLoaded)
                {
                    _settingsWindow = new PluginSettingsWindow();
                    _settingsWindow.SettingsChanged += () =>
                    {
                        UpdateMenuCheckedStates();
                        ApplySettingsState();
                    };
                    _settingsWindow.OpenDetailsRequested += ToggleDetailWindow;
                    _settingsWindow.ResetPositionRequested += ResetOverlayPosition;
                    _settingsWindow.ResetScaleRequested += ResetOverlayScale;
                }
                _settingsWindow.WindowState = WindowState.Normal;
                _settingsWindow.Show();
                _settingsWindow.Activate();
                _settingsWindow.Focus();
            });
        }

        private void ResetOverlayPosition()
        {
            if (_miniOverlay != null && Core.OverlayCanvas != null)
            {
                double left = Math.Max(50, (Core.OverlayCanvas.ActualWidth - 813) / 2);
                double top = 15;
                Canvas.SetLeft(_miniOverlay, left);
                Canvas.SetTop(_miniOverlay, top);
                PluginSettings.Instance.OverlayX = left;
                PluginSettings.Instance.OverlayY = top;
                PluginSettings.Instance.Save();
            }
        }

        private void ResetOverlayScale()
        {
            if (_miniOverlay != null)
            {
                PluginSettings.Instance.OverlayScale = 1.0;
                PluginSettings.Instance.Save();
                _miniOverlay.ApplySavedSettings();
            }
        }

        private void ApplySettingsState()
        {
            _lastLiveStateHash = -1;
            if (_miniOverlay != null)
            {
                if (_isDemoMode) return;

                var settings = PluginSettings.Instance;
                bool isDetailsOpen = _detailWindow != null && _detailWindow.IsVisible && _detailWindow.WindowState != WindowState.Minimized;
                bool hideDueToDetails = isDetailsOpen && !settings.DontHideMiniOverlayOnDetails;

                if (hideDueToDetails)
                {
                    _miniOverlay.Visibility = Visibility.Collapsed;
                    return;
                }

                if (GameStateReader.IsInBattlegrounds)
                {
                    _miniOverlay.Visibility = settings.ShowMiniOverlay ? Visibility.Visible : Visibility.Collapsed;
                }
                else if (GameStateReader.IsInMainMenu && settings.ShowInMainMenu)
                {
                    _miniOverlay.Visibility = Visibility.Visible;
                    var metaState = DemoGameStateProvider.CreateMainMenuState();
                    metaState.LockedArchetypeId = _lockedTab1BoardId ?? _lockedAlt2BoardId ?? _lockedAlt3BoardId;
                    var recs = VectorSearchEngine.Instance.FindBestComps(metaState, topN: 3);
                    _miniOverlay.UpdateRecommendations(recs);
                }
                else
                {
                    _miniOverlay.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void ToggleDetailWindow()
        {
            if (_detailWindow == null) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_detailWindow.IsVisible && _detailWindow.WindowState != WindowState.Minimized)
                {
                    _detailWindow.Hide();
                    ApplySettingsState();
                }
                else
                {
                    if (_detailWindow.RecommendationsCount == 0)
                    {
                        if (GameStateReader.IsInBattlegrounds)
                        {
                            var liveState = GameStateReader.CaptureCurrentState();
                            if (_heroBenchmarkComp != null)
                            {
                                _detailWindow.SetHeroBenchmarks(_heroBenchmarkComp, _heroTribeBenchmarks, liveState.AvailableTribes);
                            }

                            CompRecommendation tab1 = null;
                            if (!string.IsNullOrEmpty(_lockedTab1BoardId))
                            {
                                tab1 = VectorSearchEngine.Instance.EvaluateBoardById(_lockedTab1BoardId, liveState, isLocked: true);
                            }
                            else if (_heroBenchmarkComp?.Board != null)
                            {
                                tab1 = VectorSearchEngine.Instance.EvaluateBoardById(_heroBenchmarkComp.Board.Id, liveState, isLocked: false);
                            }
                            else
                            {
                                tab1 = _heroBenchmarkComp;
                            }

                            var excludeForAlt2 = new HashSet<string>();
                            if (tab1?.Board?.Id != null) excludeForAlt2.Add(tab1.Board.Id);
                            if (_heroBenchmarkComp?.Board?.Id != null) excludeForAlt2.Add(_heroBenchmarkComp.Board.Id);
                            if (!string.IsNullOrEmpty(_lockedAlt3BoardId)) excludeForAlt2.Add(_lockedAlt3BoardId);

                            var alt2 = !string.IsNullOrEmpty(_lockedAlt2BoardId)
                                ? VectorSearchEngine.Instance.EvaluateBoardById(_lockedAlt2BoardId, liveState, isLocked: true)
                                : VectorSearchEngine.Instance.FindDynamicAlternatives(liveState, excludeForAlt2, count: 1).FirstOrDefault();

                            var excludeForAlt3 = new HashSet<string>();
                            if (tab1?.Board?.Id != null) excludeForAlt3.Add(tab1.Board.Id);
                            if (_heroBenchmarkComp?.Board?.Id != null) excludeForAlt3.Add(_heroBenchmarkComp.Board.Id);
                            if (alt2?.Board?.Id != null) excludeForAlt3.Add(alt2.Board.Id);

                            var alt3 = !string.IsNullOrEmpty(_lockedAlt3BoardId)
                                ? VectorSearchEngine.Instance.EvaluateBoardById(_lockedAlt3BoardId, liveState, isLocked: true)
                                : VectorSearchEngine.Instance.FindDynamicAlternatives(liveState, excludeForAlt3, count: 1).FirstOrDefault();

                            _detailWindow.UpdateDynamicAlternatives(tab1, alt2, alt3, liveState);
                        }
                        else
                        {
                            var metaState = DemoGameStateProvider.CreateMainMenuState();
                            var recs = VectorSearchEngine.Instance.FindBestComps(metaState, topN: 3);
                            _detailWindow.UpdateRecommendations(recs, metaState.AvailableTribes);
                        }
                    }

                    _detailWindow.SetLockedBoardId(0, _lockedTab1BoardId);
                    _detailWindow.SetLockedBoardId(1, _lockedAlt2BoardId);
                    _detailWindow.SetLockedBoardId(2, _lockedAlt3BoardId);
                    _detailWindow.WindowState = WindowState.Normal;
                    _detailWindow.Show();
                    _detailWindow.Activate();
                    _detailWindow.Focus();
                    _detailWindow.Topmost = true;

                    var settings = PluginSettings.Instance;
                    if (!settings.DontHideMiniOverlayOnDetails)
                    {
                        if (_miniOverlay != null)
                        {
                            _miniOverlay.Visibility = Visibility.Collapsed;
                        }
                    }
                    else
                    {
                        if (_miniOverlay != null)
                        {
                            _miniOverlay.Visibility = Visibility.Visible;
                        }
                    }
                }
            });
        }

        public void OnUnload()
        {
            try
            {
                _searchCts?.Cancel();
                _searchCts?.Dispose();
                _searchCts = null;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_miniOverlay != null && Core.OverlayCanvas != null)
                    {
                        Core.OverlayCanvas.Children.Remove(_miniOverlay);
                    }

                    if (_detailWindow != null)
                    {
                        _detailWindow.Close();
                    }

                    if (_settingsWindow != null)
                    {
                        _settingsWindow.Close();
                    }
                });
            }
            catch { }
        }

        public void OnButtonPress()
        {
            OpenSettingsWindow();
        }

        public void OnUpdate()
        {
            try
            {
                // Адаптивный интеллектуальный троттлинг:
                // В бою: 1000 мс (состав стола зафиксирован, 0% нагрузки на CPU)
                // В таверне: 200 мс (быстрая плавная реакция в реальном времени)
                // В главном меню / вне матча: 1000 мс
                var settings = PluginSettings.Instance;
                bool inBg = GameStateReader.IsInBattlegrounds;
                bool isCombat = inBg && Hearthstone_Deck_Tracker.API.Core.Game != null && Hearthstone_Deck_Tracker.API.Core.Game.IsBattlegroundsCombatPhase;
                double minIntervalMs = settings.IsHighPerformanceMode
                    ? (isCombat ? 1500 : (inBg ? 400 : 1500))
                    : (isCombat ? 1000 : (inBg ? 200 : 1000));

                if ((DateTime.Now - _lastUpdateTime).TotalMilliseconds < minIntervalMs)
                    return;

                _lastUpdateTime = DateTime.Now;

                bool isDetailsOpen = _detailWindow != null && _detailWindow.IsVisible && _detailWindow.WindowState != WindowState.Minimized;
                bool hideDueToDetails = isDetailsOpen && !settings.DontHideMiniOverlayOnDetails;

                if (_isDemoMode)
                {
                    string currentDemoKey = $"{_lockedTab1BoardId}_{_lockedAlt2BoardId}_{_lockedAlt3BoardId}";
                    if (_lastProcessedDemoLockId != currentDemoKey)
                    {
                        _lastProcessedDemoLockId = currentDemoKey;
                        var demoState = DemoGameStateProvider.CreateDemoState();

                        CompRecommendation demoTab1 = null;
                        if (!string.IsNullOrEmpty(_lockedTab1BoardId))
                            demoTab1 = VectorSearchEngine.Instance.EvaluateBoardById(_lockedTab1BoardId, demoState, isLocked: true);

                        CompRecommendation demoAlt2 = null;
                        if (!string.IsNullOrEmpty(_lockedAlt2BoardId))
                            demoAlt2 = VectorSearchEngine.Instance.EvaluateBoardById(_lockedAlt2BoardId, demoState, isLocked: true);

                        CompRecommendation demoAlt3 = null;
                        if (!string.IsNullOrEmpty(_lockedAlt3BoardId))
                            demoAlt3 = VectorSearchEngine.Instance.EvaluateBoardById(_lockedAlt3BoardId, demoState, isLocked: true);

                        var exclude = new HashSet<string>();
                        if (demoTab1?.Board?.Id != null) exclude.Add(demoTab1.Board.Id);
                        if (demoAlt2?.Board?.Id != null) exclude.Add(demoAlt2.Board.Id);
                        if (demoAlt3?.Board?.Id != null) exclude.Add(demoAlt3.Board.Id);

                        var dynRecs = VectorSearchEngine.Instance.FindDynamicAlternatives(demoState, exclude, count: 3);

                        var hudSlots = new List<CompRecommendation>();
                        hudSlots.Add(demoTab1 ?? dynRecs.ElementAtOrDefault(0));
                        hudSlots.Add(demoAlt2 ?? dynRecs.ElementAtOrDefault(1));
                        hudSlots.Add(demoAlt3 ?? dynRecs.ElementAtOrDefault(2));

                        Application.Current?.Dispatcher?.InvokeAsync(() =>
                        {
                            _miniOverlay.Visibility = hideDueToDetails ? Visibility.Collapsed : Visibility.Visible;
                            _miniOverlay.UpdateRecommendations(hudSlots);
                            _detailWindow.UpdateDynamicAlternatives(hudSlots[0], hudSlots[1], hudSlots[2], demoState);
                        }, System.Windows.Threading.DispatcherPriority.Normal);
                    }
                    return;
                }

                if (inBg)
                {
                    if (!settings.ShowMiniOverlay || hideDueToDetails)
                    {
                        Application.Current?.Dispatcher?.InvokeAsync(() =>
                        {
                            if (_miniOverlay != null && _miniOverlay.Visibility == Visibility.Visible)
                            {
                                _miniOverlay.Visibility = Visibility.Collapsed;
                            }
                        }, System.Windows.Threading.DispatcherPriority.Background);
                    }

                    var liveState = GameStateReader.CaptureCurrentState();
                    liveState.LockedArchetypeId = null;

                    // 1. Однократная фиксация бенчмарка Героя
                    bool hasTribes = liveState.AvailableTribes != null && liveState.AvailableTribes.Count >= 3;
                    bool canLockHero = hasTribes || liveState.Turn >= 2;
                    bool needHeroBenchmark = !_isHeroLocked && canLockHero && !string.IsNullOrEmpty(liveState.HeroCardId) &&
                                             !liveState.HeroCardId.Contains("KelThuzad") &&
                                             !liveState.HeroCardId.Contains("BaconShop_HERO_KelThuzad");

                    int stateHash = HashGameState(liveState);
                    if (stateHash != _lastLiveStateHash || needHeroBenchmark)
                    {
                        _lastLiveStateHash = stateHash;

                        // Запуск асинхронного фонового расчета с отменой устаревших задач
                        _searchCts?.Cancel();
                        _searchCts = new System.Threading.CancellationTokenSource();
                        var token = _searchCts.Token;
                        int searchId = unchecked(++_activeSearchId);

                        string lockedHeroId = liveState.HeroCardId;
                        var availTribes = liveState.AvailableTribes != null ? new List<string>(liveState.AvailableTribes) : new List<string>();
                        var bannedTribes = liveState.BannedTribes != null ? new List<string>(liveState.BannedTribes) : new List<string>();
                        string lockedAlt2Id = _lockedAlt2BoardId;
                        string lockedAlt3Id = _lockedAlt3BoardId;
                        string lockedTab1Id = _lockedTab1BoardId;

                        System.Threading.Tasks.Task.Run(() =>
                        {
                            if (token.IsCancellationRequested || searchId != _activeSearchId) return;

                            CompRecommendation heroBench = _heroBenchmarkComp;
                            Dictionary<string, CompRecommendation> heroTribes = _heroTribeBenchmarks;
                            bool didLockHero = false;

                            if (needHeroBenchmark)
                            {
                                heroBench = VectorSearchEngine.Instance.FindHeroTop1Benchmark(lockedHeroId, availTribes, bannedTribes);
                                heroTribes = VectorSearchEngine.Instance.FindHeroTribeBenchmarks(lockedHeroId, availTribes, bannedTribes);
                                didLockHero = true;
                            }

                            if (token.IsCancellationRequested || searchId != _activeSearchId) return;

                            // 1. Расчет Вкладки 1 (Топ-1 рекомендация / Бенчмарк Героя / Закрепленный стол)
                            CompRecommendation tab1Comp = null;
                            if (!string.IsNullOrEmpty(lockedTab1Id))
                            {
                                tab1Comp = VectorSearchEngine.Instance.EvaluateBoardById(lockedTab1Id, liveState, isLocked: true);
                            }
                            else
                            {
                                WinningBoard targetHeroBoard = heroBench?.Board;
                                if (targetHeroBoard == null && !string.IsNullOrEmpty(lockedHeroId))
                                {
                                    heroBench = VectorSearchEngine.Instance.FindHeroTop1Benchmark(lockedHeroId, availTribes, bannedTribes);
                                    targetHeroBoard = heroBench?.Board;
                                }

                                if (targetHeroBoard != null)
                                {
                                    tab1Comp = VectorSearchEngine.Instance.EvaluateBoardById(targetHeroBoard.Id, liveState, isLocked: false);
                                }
                                else
                                {
                                    tab1Comp = heroBench;
                                }
                            }

                            if (tab1Comp == null && heroBench != null)
                            {
                                tab1Comp = heroBench;
                                tab1Comp.IsLocked = !string.IsNullOrEmpty(lockedTab1Id);
                            }

                            if (token.IsCancellationRequested || searchId != _activeSearchId) return;

                            // 2. Расчет Альтернативы 2
                            CompRecommendation alt2Comp;
                            if (!string.IsNullOrEmpty(lockedAlt2Id))
                            {
                                alt2Comp = VectorSearchEngine.Instance.EvaluateBoardById(lockedAlt2Id, liveState, isLocked: true);
                            }
                            else
                            {
                                var excludeForAlt2 = new HashSet<string>();
                                if (tab1Comp?.Board?.Id != null) excludeForAlt2.Add(tab1Comp.Board.Id);
                                if (heroBench?.Board?.Id != null) excludeForAlt2.Add(heroBench.Board.Id);
                                if (!string.IsNullOrEmpty(lockedAlt3Id)) excludeForAlt2.Add(lockedAlt3Id);

                                var alt2Recs = VectorSearchEngine.Instance.FindDynamicAlternatives(liveState, excludeForAlt2, count: 1);
                                alt2Comp = alt2Recs.Count > 0 ? alt2Recs[0] : null;
                                if (alt2Comp != null) alt2Comp.IsLocked = false;
                            }

                            if (token.IsCancellationRequested || searchId != _activeSearchId) return;

                            // 3. Расчет Альтернативы 3
                            CompRecommendation alt3Comp;
                            if (!string.IsNullOrEmpty(lockedAlt3Id))
                            {
                                alt3Comp = VectorSearchEngine.Instance.EvaluateBoardById(lockedAlt3Id, liveState, isLocked: true);
                            }
                            else
                            {
                                var excludeForAlt3 = new HashSet<string>();
                                if (tab1Comp?.Board?.Id != null) excludeForAlt3.Add(tab1Comp.Board.Id);
                                if (heroBench?.Board?.Id != null) excludeForAlt3.Add(heroBench.Board.Id);
                                if (alt2Comp?.Board?.Id != null) excludeForAlt3.Add(alt2Comp.Board.Id);

                                var alt3Recs = VectorSearchEngine.Instance.FindDynamicAlternatives(liveState, excludeForAlt3, count: 1);
                                alt3Comp = alt3Recs.Count > 0 ? alt3Recs[0] : null;
                                if (alt3Comp != null) alt3Comp.IsLocked = false;
                            }

                            if (token.IsCancellationRequested || searchId != _activeSearchId) return;

                            // Сборка 3 слотов для Mini-HUD:
                            var hudSlots = new List<CompRecommendation>();
                            if (tab1Comp != null)
                            {
                                hudSlots.Add(tab1Comp);
                            }
                            else
                            {
                                var tempTop = VectorSearchEngine.Instance.FindBestComps(liveState, topN: 1).FirstOrDefault();
                                if (tempTop != null)
                                {
                                    tempTop.IsLocked = !string.IsNullOrEmpty(lockedTab1Id);
                                    hudSlots.Add(tempTop);
                                }
                            }

                            if (alt2Comp != null) hudSlots.Add(alt2Comp);
                            if (alt3Comp != null) hudSlots.Add(alt3Comp);

                            if (token.IsCancellationRequested || searchId != _activeSearchId) return;

                            // Сохраняем в кэш плагина
                            if (didLockHero)
                            {
                                _isHeroLocked = true;
                                _lockedHeroCardId = lockedHeroId;
                                _heroBenchmarkComp = heroBench;
                                _heroTribeBenchmarks = heroTribes;
                            }
                            _alt2Comp = alt2Comp;
                            _alt3Comp = alt3Comp;

                            // Единый пакетный неблокирующий вызов Dispatcher
                            Application.Current?.Dispatcher?.InvokeAsync(() =>
                            {
                                if (token.IsCancellationRequested || searchId != _activeSearchId) return;

                                if (didLockHero && _detailWindow != null)
                                {
                                    _detailWindow.SetHeroBenchmarks(heroBench, heroTribes, availTribes);
                                }

                                if (settings.ShowMiniOverlay && _miniOverlay != null)
                                {
                                    _miniOverlay.Visibility = hideDueToDetails ? Visibility.Collapsed : Visibility.Visible;
                                    _miniOverlay.UpdateRecommendations(hudSlots);
                                }

                                if (_detailWindow != null)
                                {
                                    _detailWindow.UpdateDynamicAlternatives(tab1Comp, alt2Comp, alt3Comp, liveState);
                                }
                            }, System.Windows.Threading.DispatcherPriority.Normal);
                        }, token);
                    }
                    else if (settings.ShowMiniOverlay && !hideDueToDetails && _miniOverlay != null && _miniOverlay.Visibility != Visibility.Visible)
                    {
                        Application.Current?.Dispatcher?.InvokeAsync(() =>
                        {
                            _miniOverlay.Visibility = Visibility.Visible;
                        }, System.Windows.Threading.DispatcherPriority.Background);
                    }
                }
                else if (GameStateReader.IsInMainMenu && settings.ShowInMainMenu)
                {
                    _lastLiveStateHash = -999;
                    Application.Current?.Dispatcher?.InvokeAsync(() =>
                    {
                        if (_miniOverlay != null)
                        {
                            if (hideDueToDetails)
                            {
                                if (_miniOverlay.Visibility == Visibility.Visible)
                                    _miniOverlay.Visibility = Visibility.Collapsed;
                            }
                            else if (_miniOverlay.Visibility != Visibility.Visible)
                            {
                                _miniOverlay.Visibility = Visibility.Visible;
                                var metaState = DemoGameStateProvider.CreateMainMenuState();
                                metaState.LockedArchetypeId = _lockedTab1BoardId ?? _lockedAlt2BoardId ?? _lockedAlt3BoardId;
                                var recs = VectorSearchEngine.Instance.FindBestComps(metaState, topN: 3);
                                _miniOverlay.UpdateRecommendations(recs);
                                if (_detailWindow != null) _detailWindow.UpdateRecommendations(recs, metaState.AvailableTribes);
                            }
                        }
                    }, System.Windows.Threading.DispatcherPriority.Normal);
                }
                else
                {
                    _lastLiveStateHash = 0;
                    Application.Current?.Dispatcher?.InvokeAsync(() =>
                    {
                        if (!_isDemoMode && _miniOverlay != null && _miniOverlay.Visibility == Visibility.Visible)
                        {
                            _miniOverlay.Visibility = Visibility.Collapsed;
                        }
                    }, System.Windows.Threading.DispatcherPriority.Background);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HSCompsPlugin] Ошибка OnUpdate: {ex.Message}");
            }
        }

        private int HashGameState(PlayerGameStateSnapshot state)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + state.Turn;
                hash = hash * 31 + state.TavernTier;
                hash = hash * 31 + (_lockedTab1BoardId?.GetHashCode() ?? 0);
                hash = hash * 31 + (_lockedAlt2BoardId?.GetHashCode() ?? 0);
                hash = hash * 31 + (_lockedAlt3BoardId?.GetHashCode() ?? 0);
                hash = hash * 31 + (state.HeroCardId?.GetHashCode() ?? 0);
                if (state.AvailableTribes != null)
                {
                    hash = hash * 31 + state.AvailableTribes.Count;
                    foreach (var t in state.AvailableTribes)
                    {
                        hash = hash * 31 + (t?.GetHashCode() ?? 0);
                    }
                }
                if (state.BoardMinions != null)
                {
                    foreach (var m in state.BoardMinions)
                    {
                        hash = hash * 31 + m.DbfId;
                        hash = hash * 31 + (m.IsGolden ? 1 : 0);
                        hash = hash * 31 + m.Attack;
                        hash = hash * 31 + m.Health;
                    }
                }
                if (state.HandMinions != null)
                {
                    foreach (var m in state.HandMinions)
                    {
                        hash = hash * 31 + m.DbfId;
                        hash = hash * 31 + (m.IsGolden ? 1 : 0);
                    }
                }
                if (state.FrozenTavernMinions != null)
                {
                    foreach (var m in state.FrozenTavernMinions)
                    {
                        hash = hash * 31 + m.DbfId;
                    }
                }
                return hash;
            }
        }

        private void OnCardEvent(Hearthstone_Deck_Tracker.Hearthstone.Card card)
        {
            _lastLiveStateHash = -1;
        }

        private void OnInMenuEvent()
        {
            _searchCts?.Cancel();
            _lastLiveStateHash = -1;
            _isHeroLocked = false;
            _lockedHeroCardId = null;
            _heroBenchmarkComp = null;
            _heroTribeBenchmarks.Clear();
            _alt2Comp = null;
            _alt3Comp = null;
            _lockedTab1BoardId = null;
            _lockedAlt2BoardId = null;
            _lockedAlt3BoardId = null;
            GameStateReader.Reset();
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                _detailWindow?.ResetMatch();
                _miniOverlay?.ResetLocks();
            });
            ApplySettingsState();
        }

        private void OnModeChangedEvent(Hearthstone_Deck_Tracker.Enums.Hearthstone.Mode mode)
        {
            _lastLiveStateHash = -1;
            ApplySettingsState();
        }

        private void OnLobbyTribesUpdated(List<string> tribes)
        {
            if (tribes == null || tribes.Count < 3) return;
            try
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    _detailWindow?.SetAvailableTribes(tribes);
                });

                var liveState = GameStateReader.CaptureCurrentState();
                string currentHeroId = !string.IsNullOrEmpty(_lockedHeroCardId) ? _lockedHeroCardId : liveState?.HeroCardId;

                // Если герой уже определен, пересчитываем бенчмарки героя с актуальными племенами
                if (!string.IsNullOrEmpty(currentHeroId) &&
                    !currentHeroId.Contains("KelThuzad") &&
                    !currentHeroId.Contains("BaconShop_HERO_KelThuzad"))
                {
                    _lockedHeroCardId = currentHeroId;
                    _isHeroLocked = true;
                    _heroBenchmarkComp = VectorSearchEngine.Instance.FindHeroTop1Benchmark(currentHeroId, liveState.AvailableTribes, liveState.BannedTribes);
                    _heroTribeBenchmarks = VectorSearchEngine.Instance.FindHeroTribeBenchmarks(currentHeroId, liveState.AvailableTribes, liveState.BannedTribes);

                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        _detailWindow?.SetHeroBenchmarks(_heroBenchmarkComp, _heroTribeBenchmarks, liveState.AvailableTribes);
                    });
                }

                _lastLiveStateHash = -1;
            }
            catch { }
        }

        private void ToggleDemoMode()
        {
            _isDemoMode = !_isDemoMode;
            _lastProcessedDemoLockId = "__none__";

            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_isDemoMode)
                {
                    var demoState = DemoGameStateProvider.CreateDemoState();
                    demoState.LockedArchetypeId = _lockedTab1BoardId ?? _lockedAlt2BoardId ?? _lockedAlt3BoardId;
                    var recs = VectorSearchEngine.Instance.FindBestComps(demoState, topN: 3);
                    _miniOverlay.Visibility = Visibility.Visible;
                    _miniOverlay.UpdateRecommendations(recs);
                    _detailWindow.UpdateRecommendations(recs, demoState.AvailableTribes);
                    _detailWindow.Show();
                    _lastProcessedDemoLockId = $"{_lockedTab1BoardId}_{_lockedAlt2BoardId}_{_lockedAlt3BoardId}";
                }
                else
                {
                    _miniOverlay.Visibility = Visibility.Collapsed;
                    _detailWindow.Hide();
                }
            });
        }

        private void OnCompLockChanged(int tabIndex, string boardId, bool isLocked)
        {
            string lockedId = isLocked ? boardId : null;
            switch (tabIndex)
            {
                case 0: _lockedTab1BoardId = lockedId; break;
                case 1: _lockedAlt2BoardId = lockedId; break;
                case 2: _lockedAlt3BoardId = lockedId; break;
            }

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                _detailWindow?.SetLockedBoardId(tabIndex, lockedId);
                _miniOverlay?.SetLockedBoardId(tabIndex, lockedId);
            });

            if (_isDemoMode)
            {
                _lastProcessedDemoLockId = "__none__";
            }
            else
            {
                _lastLiveStateHash = -1;
            }
        }

        private void OnGameStart()
        {
            try
            {
                GameStateReader.Reset();
                _lockedTab1BoardId = null;
                _lockedAlt2BoardId = null;
                _lockedAlt3BoardId = null;
                _isDemoMode = false;
                _lastLiveStateHash = -1;

                _isHeroLocked = false;
                _lockedHeroCardId = null;
                _heroBenchmarkComp = null;
                _heroTribeBenchmarks.Clear();
                _alt2Comp = null;
                _alt3Comp = null;

                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    _detailWindow?.ResetMatch();
                    _miniOverlay?.ResetLocks();
                });

                if (GameStateReader.IsInBattlegrounds)
                {
                    GameStateReader.StartWaitingForTribes();

                    if (PluginSettings.Instance.ShowMiniOverlay)
                    {
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            if (_miniOverlay != null)
                            {
                                _miniOverlay.Visibility = Visibility.Visible;
                                var initialLiveState = GameStateReader.CaptureCurrentState();
                                var recs = VectorSearchEngine.Instance.FindBestComps(initialLiveState, topN: 3);
                                _miniOverlay.UpdateRecommendations(recs);
                                if (_detailWindow != null) _detailWindow.UpdateRecommendations(recs, initialLiveState.AvailableTribes);
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HSCompsPlugin] Ошибка OnGameStart: {ex.Message}");
            }
        }

        private void OnTurnStart(Hearthstone_Deck_Tracker.Enums.ActivePlayer player)
        {
            try
            {
                _lastLiveStateHash = -1;
            }
            catch { }
        }

        private void OnGameEnd()
        {
            try
            {
                GameStateReader.Reset();
                _lockedTab1BoardId = null;
                _lockedAlt2BoardId = null;
                _lockedAlt3BoardId = null;
                _isDemoMode = false;
                _isHeroLocked = false;
                _lockedHeroCardId = null;
                _heroBenchmarkComp = null;
                _heroTribeBenchmarks.Clear();
                _alt2Comp = null;
                _alt3Comp = null;

                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    _detailWindow?.ResetMatch();
                    _miniOverlay?.ResetLocks();
                    if (_miniOverlay != null && !(GameStateReader.IsInMainMenu && PluginSettings.Instance.ShowInMainMenu))
                    {
                        _miniOverlay.Visibility = Visibility.Collapsed;
                    }
                    if (_detailWindow != null)
                    {
                        _detailWindow.Hide();
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HSCompsPlugin] Ошибка OnGameEnd: {ex.Message}");
            }
        }
    }
}
