using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace IcarusAchievements
{
    public partial class MainWindow : Window
    {
        private OverlayWindow _overlayWindow;
        private LogoOverlay _logoOverlay;
        private HotkeyManager _hotkeyManager;
        private SteamService _steamService;
        private DispatcherTimer _steamUpdateTimer;

        public MainWindow()
        {
            InitializeComponent();

            this.Title = "Icarus Achievements";
            this.Width = 400;
            this.Height = 300;

            InitializeSteam();
            InitializeHotkeys();

            // init overlay in a background thread -> allows the main window to load without blocking
            Task.Run(() => StartOverlay());
            Task.Run(() => StartLogoOverlay());
        }

        /// <summary>
        /// init Steam API 
        /// </summary>
        private void InitializeSteam()
        {
            _steamService = new SteamService();

            bool steamConnected = _steamService.Initialize();

            if (steamConnected)
            {
                this.Title = "Icarus Achievements - Connected to Steam";

                _steamService.AchievementUnlocked += OnSteamAchievementUnlocked;
                _steamService.GameChanged += OnGameChanged;

                _steamUpdateTimer = new DispatcherTimer();
                _steamUpdateTimer.Interval = TimeSpan.FromSeconds(1); // TODO: start with 1s but change later for imapct
                _steamUpdateTimer.Tick += (s, e) => _steamService.Update();
                _steamUpdateTimer.Start();

                // quick test Steam integration after 5 seconds
                Task.Delay(5000).ContinueWith(_ =>
                {
                    _steamService.SimulateAchievementUnlock();
                });
            }
            else
            {
                this.Title = "Icarus Achievements - Steam Not Connected";
                // continue without Steam (fallback mode). This will use the desktop app with cached info etc. access to guides etc
            }
        }

        /// <summary>
        /// Handle Steam achievement unlocks
        /// </summary>
        private void OnSteamAchievementUnlocked(SteamAchievement achievement)
        {
            _overlayWindow?.ShowAchievement(
                achievement.Name,
                achievement.Description
            );
        }

        /// <summary>
        /// Handle game changes
        /// </summary>
        private void OnGameChanged(string gameName)
        {
            Dispatcher.Invoke(() =>
            {
                this.Title = $"Icarus Achievements - {gameName}";
            });
        }

        /// <summary>
        /// Set up hotkey detection for Shift+Tab
        /// </summary>
        private void InitializeHotkeys()
        {
            _hotkeyManager = new HotkeyManager();

            // Shift+Tab  presse show the logo overlay
            _hotkeyManager.ShiftTabPressed += () =>
            {
                _logoOverlay?.Show();
            };
        }

        /// <summary>
        /// begin overlay system in a background thread
        /// </summary>
        private void StartOverlay()
        {
            try
            {
                _overlayWindow = new OverlayWindow();

                // show a test achievement after 3 seconds
                Task.Delay(3000).ContinueWith(_ =>
                {
                    string message = _steamService?.IsConnected() == true
                        ? "Steam connected. Real achievements incoming..."
                        : "Steam not connected - using debug/dev mode";

                    _overlayWindow.ShowAchievement(
                        "Icarus Achievements Ready",
                        message
                    );
                });

                _overlayWindow.Start();
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Overlay failed to start: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        /// <summary>
        /// Start the logo overlay system
        /// </summary>
        private void StartLogoOverlay()
        {
            try
            {
                _logoOverlay = new LogoOverlay();

                // When logo is clicked, bring main window to front
                _logoOverlay.LogoClicked += () =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        this.Show();
                        this.WindowState = WindowState.Normal;
                        this.Activate();
                        this.Topmost = true;
                        this.Topmost = false; // Flash to get attention
                    });
                };

                _logoOverlay.Start();
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Logo overlay failed: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _steamUpdateTimer?.Stop();
            _steamService?.Shutdown();
            _overlayWindow?.Stop();
            _logoOverlay?.Stop();
            _hotkeyManager?.Dispose();
            base.OnClosed(e);
        }
    }
}