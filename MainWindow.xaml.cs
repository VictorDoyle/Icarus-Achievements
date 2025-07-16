using System;
using System.Collections.ObjectModel;
using System.Linq;
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

        // Achievement tracking
        private ObservableCollection<SteamAchievement> _achievements = new ObservableCollection<SteamAchievement>();

        public MainWindow()
        {
            InitializeComponent();

            this.Title = "Icarus Achievements";
            this.Width = 600;
            this.Height = 500;

            // Initialize UI
            InitializeUI();

            // Initialize Steam first
            InitializeSteam();

            // Initialize hotkey system
            InitializeHotkeys();

            // init overlay in a background thread -> allows the main window to load without blocking
            Task.Run(() => StartOverlay());
            Task.Run(() => StartLogoOverlay());
        }

        /// <summary>
        /// Initialize UI components
        /// </summary>
        private void InitializeUI()
        {
            // Set up achievement list
            AchievementListBox.ItemsSource = _achievements;

            // Update initial UI state
            UpdateUI(new UserProfileData { PlayerName = "Loading..." });
        }

        /// <summary>
        /// Initialize Steam API connection
        /// </summary>
        private void InitializeSteam()
        {
            // Initialize notification service
            _notificationService = new NotificationService();

            _steamService = new SteamService();

            // Set up status updates to show as notifications
            _steamService.StatusUpdate += (message) =>
            {
                bool isError = message.Contains("Failed") || message.Contains("Error") || message.Contains("not running");
                _notificationService.ShowSteamStatus(message, isError);
            };

            // Set up profile updates
            _steamService.ProfileUpdated += OnProfileUpdated;

            // Set up achievement unlock detection
            _steamService.AchievementUnlocked += OnSteamAchievementUnlocked;
            _steamService.GameChanged += OnGameChanged;

            // Try to connect to Steam
            bool steamConnected = _steamService.Initialize();

            if (steamConnected)
            {
                this.Title = "Icarus Achievements - Connected to Steam";

                // Set up timer to regularly check Steam API
                _steamUpdateTimer = new DispatcherTimer();
                _steamUpdateTimer.Interval = TimeSpan.FromSeconds(2); // Check every 2 seconds
                _steamUpdateTimer.Tick += (s, e) => _steamService.Update();
                _steamUpdateTimer.Start();

                // Load initial achievement data
                LoadAchievementData();

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
            // Update header
            PlayerNameText.Text = $"Player: {profileData.PlayerName}";
            CurrentGameText.Text = $"Game: {profileData.CurrentGameName}";

            // Update stats
            ProgressText.Text = $"{profileData.UnlockedAchievements}/{profileData.TotalAchievements} ({profileData.CompletionPercentage:F1}%)";
            UnlockedText.Text = profileData.UnlockedAchievements.ToString();
            RemainingText.Text = (profileData.TotalAchievements - profileData.UnlockedAchievements).ToString();

            // Update progress bar
            ProgressBar.Maximum = profileData.TotalAchievements;
            ProgressBar.Value = profileData.UnlockedAchievements;

            // Update window title with completion
            if (profileData.TotalAchievements > 0)
            {
                this.Title = $"Icarus Achievements - {profileData.CurrentGameName} ({profileData.CompletionPercentage:F1}%)";
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
        /// Handle game changes
        /// </summary>
        private void OnGameChanged(string gameName)
        {
            Dispatcher.Invoke(() =>
            {
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
            base.OnClosed(e);
        }
    }
}