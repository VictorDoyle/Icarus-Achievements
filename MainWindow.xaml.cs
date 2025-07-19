using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;

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
        private TaskbarIcon _taskbarIcon;

        // Achievement tracking
        private ObservableCollection<SteamAchievement> _achievements = new ObservableCollection<SteamAchievement>();

        public MainWindow()
        {
            InitializeComponent();

            this.Title = "Icarus Achievements";
            this.Width = 600;
            this.Height = 500;

            // Start minimized to system tray
            this.WindowState = WindowState.Minimized;
            this.ShowInTaskbar = false;

            // Initialize system tray
            InitializeSystemTray();

            // Initialize UI
            InitializeUI();

            // Initialize Steam monitoring
            InitializeSteam();

            // Initialize hotkey system
            InitializeHotkeys();

            // init overlay in a background thread
            Task.Run(() => StartOverlay());
            Task.Run(() => StartLogoOverlay());

            // Show startup notification
            ShowStartupNotification();
        }

        /// <summary>
        /// Initialize system tray icon and menu
        /// </summary>
        private void InitializeSystemTray()
        {
            _taskbarIcon = new TaskbarIcon();
            _taskbarIcon.Icon = System.Drawing.SystemIcons.Application; // You can replace with custom icon
            _taskbarIcon.ToolTipText = "Icarus Achievements - Monitoring Steam";

            // Create context menu
            var contextMenu = new System.Windows.Controls.ContextMenu();

            var openItem = new System.Windows.Controls.MenuItem();
            openItem.Header = "Open Dashboard";
            openItem.Click += (s, e) => ShowMainWindow();

            var exitItem = new System.Windows.Controls.MenuItem();
            exitItem.Header = "Exit";
            exitItem.Click += (s, e) => Application.Current.Shutdown();

            contextMenu.Items.Add(openItem);
            contextMenu.Items.Add(new System.Windows.Controls.Separator());
            contextMenu.Items.Add(exitItem);

            _taskbarIcon.ContextMenu = contextMenu;

            // Double-click to open dashboard
            _taskbarIcon.TrayMouseDoubleClick += (s, e) => ShowMainWindow();
        }

        /// <summary>
        /// Show startup notification
        /// </summary>
        private void ShowStartupNotification()
        {
            _taskbarIcon.ShowBalloonTip("Icarus Achievements",
                "Now monitoring Steam for achievement activity",
                BalloonIcon.Info);
        }

        /// <summary>
        /// Show main window from system tray
        /// </summary>
        private void ShowMainWindow()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.ShowInTaskbar = true;
            this.Activate();
        }

        /// <summary>
        /// Hide to system tray instead of closing
        /// </summary>
        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                this.Hide();
                this.ShowInTaskbar = false;
            }
            base.OnStateChanged(e);
        }

        /// <summary>
        /// Initialize UI components
        /// </summary>
        private void InitializeUI()
        {
            // Set up achievement list
            AchievementListBox.ItemsSource = _achievements;

            // Update initial UI state with placeholder data
            UpdateUI(new UserProfileData { PlayerName = "Monitoring...", CurrentGameName = "No game detected" });
        }

        /// <summary>
        /// Initialize Steam monitoring - now works as background service
        /// </summary>
        private void InitializeSteam()
        {
            _notificationService = new NotificationService();
            _steamService = new SteamService();

            // Set up status updates for system tray notifications
            _steamService.StatusUpdate += (message) =>
            {
                bool isError = message.Contains("Failed") || message.Contains("Error") || message.Contains("not running");

                if (isError)
                {
                    _taskbarIcon.ShowBalloonTip("Steam Error", message, BalloonIcon.Warning);
                }
                else if (message.Contains("Detected game") || message.Contains("initialized"))
                {
                    _taskbarIcon.ShowBalloonTip("Icarus Achievements", message, BalloonIcon.Info);
                }
            };

            // Set up profile updates
            _steamService.ProfileUpdated += OnProfileUpdated;

            // Set up achievement unlock detection
            _steamService.AchievementUnlocked += OnSteamAchievementUnlocked;
            _steamService.GameChanged += OnGameChanged;

            // Start monitoring Steam (don't require immediate connection)
            StartSteamMonitoring();
        }

        /// <summary>
        /// Start continuous Steam monitoring
        /// </summary>
        private void StartSteamMonitoring()
        {
            // Set up timer to continuously check for Steam
            _steamUpdateTimer = new DispatcherTimer();
            _steamUpdateTimer.Interval = TimeSpan.FromSeconds(5); // Check every 5 seconds
            _steamUpdateTimer.Tick += CheckSteamStatus;
            _steamUpdateTimer.Start();

            // Initial check
            CheckSteamStatus(null, null);
        }

        /// <summary>
        /// Continuously monitor Steam status
        /// </summary>
        private void CheckSteamStatus(object sender, EventArgs e)
        {
            if (!_steamService.IsConnected())
            {
                // Try to connect to Steam
                bool connected = _steamService.Initialize();

                if (connected)
                {
                    // Steam just connected
                    _taskbarIcon.ShowBalloonTip("Steam Detected",
                        "Steam connection established. Now monitoring for games.",
                        BalloonIcon.Info);

                    LoadAchievementData();
                }
            }
            else
            {
                // Steam is connected, update normally
                _steamService.Update();
            }
        }

        /// <summary>
        /// Load achievement data from Steam
        /// </summary>
        private void LoadAchievementData()
        {
            if (_steamService?.IsConnected() != true) return;

            Task.Run(() =>
            {
                var achievements = _steamService.GetCurrentGameAchievements();

                Dispatcher.Invoke(() =>
                {
                    _achievements.Clear();

                    // Add achievements sorted by unlock status and rarity
                    var sortedAchievements = achievements
                        .OrderByDescending(a => a.IsUnlocked)
                        .ThenByDescending(a => a.Rarity)
                        .ThenBy(a => a.Name);

                    foreach (var achievement in sortedAchievements)
                    {
                        _achievements.Add(achievement);
                    }
                });
            });
        }

        /// <summary>
        /// Handle Steam profile updates
        /// </summary>
        private void OnProfileUpdated(UserProfileData profileData)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateUI(profileData);
            });
        }

        /// <summary>
        /// Update UI with profile data
        /// </summary>
        private void UpdateUI(UserProfileData profileData)
        {
            // Update header elements (these exist in XAML)
            PlayerNameText.Text = $"Player: {profileData.PlayerName}";
            CurrentGameText.Text = $"Game: {profileData.CurrentGameName}";

            // Update stats elements (these exist in XAML)
            ProgressText.Text = $"{profileData.UnlockedAchievements}/{profileData.TotalAchievements} ({profileData.CompletionPercentage:F1}%)";
            UnlockedText.Text = profileData.UnlockedAchievements.ToString();
            RemainingText.Text = (profileData.TotalAchievements - profileData.UnlockedAchievements).ToString();

            // Update progress bar (this exists in XAML)
            ProgressBar.Maximum = profileData.TotalAchievements;
            ProgressBar.Value = profileData.UnlockedAchievements;

            // Update window title with completion
            if (profileData.TotalAchievements > 0)
            {
                this.Title = $"Icarus Achievements - {profileData.CurrentGameName} ({profileData.CompletionPercentage:F1}%)";
            }
            else
            {
                this.Title = $"Icarus Achievements - {profileData.CurrentGameName}";
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

            // Update achievement in list
            Dispatcher.Invoke(() =>
            {
                var existingAchievement = _achievements.FirstOrDefault(a => a.Id == achievement.Id);
                if (existingAchievement != null)
                {
                    existingAchievement.IsUnlocked = true;
                    existingAchievement.UnlockTime = achievement.UnlockTime;
                }

                // Refresh the list to show updated status
                LoadAchievementData();
            });
        }

        /// <summary>
        /// Handle game changes with user-friendly notifications
        /// </summary>
        private void OnGameChanged(string gameName)
        {
            // Show system tray notification for game detection
            _taskbarIcon.ShowBalloonTip("Steam Game Detected",
                $"Loading achievements for {gameName}...",
                BalloonIcon.Info);

            Dispatcher.Invoke(() =>
            {
                // Update UI title
                this.Title = $"Icarus Achievements - {gameName}";

                // Reload achievement data for new game
                LoadAchievementData();
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
                        ShowMainWindow();
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

        /// <summary>
        /// Test achievement button click
        /// </summary>
        private void TestAchievementButton_Click(object sender, RoutedEventArgs e)
        {
            _steamService?.SimulateAchievementUnlock();
        }

        /// <summary>
        /// Refresh data button click
        /// </summary>
        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadAchievementData();
        }

        protected override void OnClosed(EventArgs e)
        {
            _steamUpdateTimer?.Stop();
            _steamService?.Shutdown();
            _overlayWindow?.Stop();
            _logoOverlay?.Stop();
            _hotkeyManager?.Dispose();
            _notificationService?.Dispose();
            _taskbarIcon?.Dispose();
            base.OnClosed(e);
        }
    }
}