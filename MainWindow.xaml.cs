using System;
using System.Threading.Tasks;
using System.Windows;

namespace IcarusAchievements
{
    public partial class MainWindow : Window
    {
        private OverlayWindow _overlayWindow;

        public MainWindow()
        {
            InitializeComponent();

            this.Title = "Icarus Achievements";
            this.Width = 400;
            this.Height = 300;

            // init overlay in a background thread -> allows the main window to load without blocking
            Task.Run(() => StartOverlay());
        }

        /// <summary>
        /// begin overlay system in a background thread
        /// </summary>
        private void StartOverlay()
        {
            try
            {
                _overlayWindow = new OverlayWindow();

                // shiow a test achievement after 3 seconds
                Task.Delay(3000).ContinueWith(_ =>
                {
                    _overlayWindow.ShowAchievement(
                        "Overlay Working",
                        "Your overlay system is successfully running..."
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
        protected override void OnClosed(EventArgs e)
        {
            _overlayWindow?.Stop();
            base.OnClosed(e);
        }
    }
}