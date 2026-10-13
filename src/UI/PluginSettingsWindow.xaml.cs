using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using HSCompsPlugin.Services;

namespace HSCompsPlugin.UI
{
    public partial class PluginSettingsWindow : Window
    {
        public event Action SettingsChanged;
        public event Action OpenDetailsRequested;
        public event Action ResetPositionRequested;
        public event Action ResetScaleRequested;

        public PluginSettingsWindow()
        {
            InitializeComponent();
            LoadSettings();
            RefreshDatabaseInfo();
            RefreshPluginVersionInfo();
        }

        private void LoadSettings()
        {
            var settings = PluginSettings.Instance;
            ChkShowInBattlegrounds.IsChecked = settings.ShowMiniOverlay;
            ChkShowInMainMenu.IsChecked = settings.ShowInMainMenu;
            ChkDontHideMiniOverlayOnDetails.IsChecked = settings.DontHideMiniOverlayOnDetails;
            ChkHighPerformanceMode.IsChecked = settings.IsHighPerformanceMode;
        }

        public void RefreshDatabaseInfo()
        {
            try
            {
                var info = DatabaseUpdateService.Instance.GetDatabaseInfo();
                if (info.Exists)
                {
                    TxtDbStatusSummary.Text = $"База данных активна: {info.BoardCount} победных столов";
                    string dateStr = info.LastModified.HasValue
                        ? info.LastModified.Value.ToString("dd.MM.yyyy HH:mm")
                        : "неизвестно";
                    string fileName = !string.IsNullOrEmpty(info.FilePath) ? Path.GetFileName(info.FilePath) : "comps_vector_db.json";
                    TxtDbFileDetails.Text = $"Файл: {fileName} • Обновлено: {dateStr} • Размер: {info.FileSizeKb:0.0} КБ";
                }
                else
                {
                    TxtDbStatusSummary.Text = "База данных столов не найдена на диске";
                    TxtDbFileDetails.Text = "Нажмите «Обновить базу из Firestone API» для начальной загрузки";
                }
            }
            catch (Exception ex)
            {
                TxtDbStatusSummary.Text = "Статус базы: ошибка чтения";
                TxtDbFileDetails.Text = ex.Message;
            }
        }

        private async void OnUpdateDatabaseClick(object sender, RoutedEventArgs e)
        {
            BtnUpdateDb.IsEnabled = false;
            TxtBtnUpdateDb.Text = "Обновление базы Firestone...";
            PbUpdateProgress.Visibility = Visibility.Visible;
            TxtUpdateStatus.Visibility = Visibility.Visible;
            TxtUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(138, 226, 255));
            TxtUpdateStatus.Text = "Подключение к API Firestone (ZeroToHeroes)...";

            TxtUpdateLog.Visibility = Visibility.Visible;
            BtnToggleLog.Content = "Скрыть лог";
            TxtUpdateLog.Clear();

            try
            {
                var result = await DatabaseUpdateService.Instance.RunUpdateAsync(msg =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        TxtUpdateStatus.Text = msg;
                        TxtUpdateLog.AppendText(msg + Environment.NewLine);
                        TxtUpdateLog.ScrollToEnd();
                    });
                });

                if (result.Success)
                {
                    TxtUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(174, 234, 125));
                    TxtUpdateStatus.Text = $"✅ {result.Message}";
                    RefreshDatabaseInfo();
                    MessageBox.Show(result.Message, "HS Comps — Обновление базы", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    TxtUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 140, 140));
                    TxtUpdateStatus.Text = $"❌ {result.Message}";
                    MessageBox.Show(result.Message, "HS Comps — Ошибка обновления", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                TxtUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 140, 140));
                TxtUpdateStatus.Text = $"❌ Ошибка: {ex.Message}";
                MessageBox.Show(ex.Message, "HS Comps — Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnUpdateDb.IsEnabled = true;
                TxtBtnUpdateDb.Text = "Обновить базу из Firestone API";
                PbUpdateProgress.Visibility = Visibility.Collapsed;
            }
        }

        private void OnToggleLogClick(object sender, RoutedEventArgs e)
        {
            if (TxtUpdateLog.Visibility == Visibility.Visible)
            {
                TxtUpdateLog.Visibility = Visibility.Collapsed;
                BtnToggleLog.Content = "Показать лог";
            }
            else
            {
                TxtUpdateLog.Visibility = Visibility.Visible;
                BtnToggleLog.Content = "Скрыть лог";
            }
        }

        private void OnSaveAndCloseClick(object sender, RoutedEventArgs e)
        {
            var settings = PluginSettings.Instance;
            settings.ShowMiniOverlay = ChkShowInBattlegrounds.IsChecked == true;
            settings.ShowInMainMenu = ChkShowInMainMenu.IsChecked == true;
            settings.DontHideMiniOverlayOnDetails = ChkDontHideMiniOverlayOnDetails.IsChecked == true;
            settings.IsHighPerformanceMode = ChkHighPerformanceMode.IsChecked == true;
            settings.Save();

            SettingsChanged?.Invoke();
            Close();
        }

        private void OnResetPositionClick(object sender, RoutedEventArgs e)
        {
            ResetPositionRequested?.Invoke();
            MessageBox.Show("Позиция мини-HUD сброшена по умолчанию (по центру).", "HS Comps", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnResetScaleClick(object sender, RoutedEventArgs e)
        {
            ResetScaleRequested?.Invoke();
            MessageBox.Show("Масштаб мини-HUD сброшен на 100%.", "HS Comps", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnOpenDetailsClick(object sender, RoutedEventArgs e)
        {
            OpenDetailsRequested?.Invoke();
        }

        private void RefreshPluginVersionInfo()
        {
            try
            {
                var ver = Assembly.GetExecutingAssembly().GetName().Version;
                string verStr = ver != null ? $"v{ver.Major}.{ver.Minor}.{ver.Build}" : "v3.0.0";
                TxtPluginVersionSummary.Text = $"Текущая версия плагина: {verStr} (ZadroTube)";
            }
            catch
            {
                TxtPluginVersionSummary.Text = "Текущая версия плагина: v3.0.0 (ZadroTube)";
            }
        }

        private void OnRunUpdaterClick(object sender, RoutedEventArgs e)
        {
            try
            {
                string updaterExePath = null;

                // 1. Проверяем папку плагина в AppData
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string pluginDirUpdater = Path.Combine(appData, "HearthstoneDeckTracker", "Plugins", "HSCompsPlugin", "HSCompsUpdater.exe");
                if (File.Exists(pluginDirUpdater))
                {
                    updaterExePath = pluginDirUpdater;
                }

                // 2. Проверяем базовую директорию сборки
                if (updaterExePath == null)
                {
                    string baseDirUpdater = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HSCompsUpdater.exe");
                    if (File.Exists(baseDirUpdater))
                    {
                        updaterExePath = baseDirUpdater;
                    }
                }

                // 3. Проверяем локальный билд репозитория
                if (updaterExePath == null)
                {
                    string repoUpdater = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "updater", "bin", "Release", "HSCompsUpdater.exe");
                    if (File.Exists(repoUpdater))
                    {
                        updaterExePath = repoUpdater;
                    }
                }

                if (!string.IsNullOrEmpty(updaterExePath) && File.Exists(updaterExePath))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = updaterExePath,
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                }
                else
                {
                    var res = MessageBox.Show(
                        "Файл автономного апдейтера HSCompsUpdater.exe не найден локально.\n\nОткрыть страницу релизов на GitHub для скачивания свежей версии?",
                        "HS Comps — Авто-апдейтер",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (res == MessageBoxResult.Yes)
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "https://github.com/ZadroTube/HS_Comps_Plugin/releases/latest",
                            UseShellExecute = true
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка запуска апдейтера: {ex.Message}", "HS Comps", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
