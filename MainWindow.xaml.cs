using System;
using System.Threading.Tasks;
using System.Windows;

namespace IcarusAchievements
{
    public partial class MainWindow : Window
    {
        private OverlayWindow _overlayWindow;
        private LogoOverlay _logoOverlay;
        private HotkeyManager _hotkeyManager;

        public MainWindow()
        {
            InitializeComponent();

            this.Title = "Icarus Achievements";
            this.Width = 400;
            this.Height = 300;

            InitializeHotkeys();

            // init overlay in a background thread -> allows the main window to load without blocking
            Task.Run(() => StartOverlay());
            Task.Run(() => StartLogoOverlay());
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
                    _overlayWindow.ShowAchievement(
                        "Overlay Working",
                        "Press Shift+Tab to open Icarus Achievements"
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
                        this.Topmost = false;
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
            _overlayWindow?.Stop();
            _logoOverlay?.Stop();
            _hotkeyManager?.Dispose();
            base.OnClosed(e);
        }
    }
}