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
        private NotificationService _notificationService;
        private DispatcherTimer _steamUpdateTimer;

        public MainWindow()
        {
            InitializeComponent();

            this.Title = "Icarus Achievements";
            this.Width = 400;
            this.Height = 300;

            // Initialize Steam first
            InitializeSteam();

            // Initialize hotkey system
            InitializeHotkeys();

            // init overlay in a background thread -> allows the main window to load without blocking
            Task.Run(() => StartOverlay());
            Task.Run(() => StartLogoOverlay());
        }

        /// <summary>
        /// Initialize Steam API connection
        /// </summary>
        private void InitializeSteam()
        {
            // Initialize notification service
            _notificationService = new NotificationService();

            _steamService = new SteamService();

            // Set up status updates to show as Windows notifications
            _steamService.StatusUpdate += (message) =>
            {
                // Determine if it's an error based on the message content
                bool isError = message.Contains("Failed") || message.Contains("Error") || message.Contains("not running");
                bool isSuccess = message.Contains("successfully") || message.Contains("initialized");

                if (isError)
                {
                    _notificationService.ShowSteamStatus(message, true);
                }
                else if (isSuccess)
                {
                    _notificationService.ShowSteamStatus(message, false);
                }
                else
                {
                    _notificationService.ShowSteamStatus(message, false);
                }
            };

            // Try to connect to Steam
            bool steamConnected = _steamService.Initialize();

            if (steamConnected)
            {
                this.Title = "Icarus Achievements - Connected to Steam";

                // Set up achievement unlock detection
                _steamService.AchievementUnlocked += OnSteamAchievementUnlocked;
                _steamService.GameChanged += OnGameChanged;

                // Set up timer to regularly check Steam API
                _steamUpdateTimer = new DispatcherTimer();
                _steamUpdateTimer.Interval = TimeSpan.FromSeconds(1); // Check every second
                _steamUpdateTimer.Tick += (s, e) => _steamService.Update();
                _steamUpdateTimer.Start();

                // Test Steam integration after 5 seconds
                Task.Delay(5000).ContinueWith(_ =>
                {
                    _steamService.SimulateAchievementUnlock();
                });
            }
            else
            {
                this.Title = "Icarus Achievements - Steam Not Connected";
                // Continue without Steam (fallback mode)
            }
        }

        /// <summary>
        /// Handle Steam achievement unlocks
        /// </summary>
        private void OnSteamAchievementUnlocked(SteamAchievement achievement)
        {
            // Show the achievement in our overlay
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

            // When Shift+Tab is pressed, show the logo overlay
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
                        ? "Steam connected - Real achievements incoming"
                        : "Steam not connected - Using test mode";

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
            _notificationService?.Dispose();
            base.OnClosed(e);
        }
    }
}