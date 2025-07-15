using System;
using System.Windows;
using System.Windows.Media;
using GameOverlay.Drawing;
using GameOverlay.Windows;
using Graphics = GameOverlay.Drawing.Graphics;

namespace IcarusAchievements
{
    /// <summary>
    /// the transparent overlay window that appears over games
    /// leverages the GameOverlay.Net for hardware-accelerated rendering
    /// </summary>
    public class OverlayWindow
    {
        private GraphicsWindow _window;

        // drawnig resources for graphical interface
        private Graphics _graphics;
        private SolidBrush _blackBrush;
        private SolidBrush _whiteBrush;
        private SolidBrush _backgroundBrush;
        private Font _titleFont;
        private Font _descriptionFont;

        // test achievements with data
        private string _achievementTitle = "Test Achievement";
        private string _achievementDescription = "This is a test achievement overlay";
        private bool _isVisible = false;

        public OverlayWindow()
        {
            // this will be thesee thru click-through window that appears over everything
            _window = new GraphicsWindow(0, 0, 1920, 1080)
            {
                FPS = 60,
                IsTopmost = true,
                IsVisible = true
            };

            _window.DestroyGraphics += Window_DestroyGraphics;
            _window.DrawGraphics += Window_DrawGraphics;
            _window.SetupGraphics += Window_SetupGraphics;
        }

        /// <summary>
        /// When Overlay Window begins, we setup the graphic drawing resources 
        /// </summary>
        private void Window_SetupGraphics(object sender, SetupGraphicsEventArgs e)
        {
            var gfx = e.Graphics;

            _blackBrush = gfx.CreateSolidBrush(0, 0, 0, 255);        // black text
            _whiteBrush = gfx.CreateSolidBrush(255, 255, 255, 255);  //white text

            _backgroundBrush = gfx.CreateSolidBrush(20, 20, 20, 200); // dark grey semi-transparent like steam ish

            // FIXME: update font, MVP uses Arial but nicer font later
            _titleFont = gfx.CreateFont("Arial", 16, true);
            _descriptionFont = gfx.CreateFont("Arial", 12, false);
        }

        /// <summary>
        /// 60 * second, achievement is drawn here
        /// </summary>
        private void Window_DrawGraphics(object sender, DrawGraphicsEventArgs e)
        {
            var gfx = e.Graphics;

            gfx.ClearScene();

            // only start drawing if we need to show an achievemnet
            if (_isVisible)
            {
                DrawAchievementNotification(gfx);
            }
        }

        /// <summary>
        /// Draw a Steam-style achievement notification
        /// </summary>
        private void DrawAchievementNotification(Graphics gfx)
        {
            // Position in top-right corner (steam does bottom right)
            float x = 1920 - 350; // 350 px from right edge
            float y = 50;          // 50 px from top
            float width = 300;
            float height = 80;

            gfx.FillRoundedRectangle(_backgroundBrush, x, y, x + width, y + height, 8);

            gfx.DrawText(_titleFont, _whiteBrush, x + 10, y + 10, _achievementTitle);

            gfx.DrawText(_descriptionFont, _whiteBrush, x + 10, y + 35, _achievementDescription);

            // progress bar (just for testing)
            float progressWidth = width - 20;
            float progressHeight = 4;
            float progressX = x + 10;
            float progressY = y + height - 15;

            // dark backgr of progress bar 
            gfx.FillRectangle(_blackBrush, progressX, progressY, progressX + progressWidth, progressY + progressHeight);

            // progress in progressbar will have white fill (white, 75% complete for testing)
            float fillWidth = progressWidth * 0.75f;
            gfx.FillRectangle(_whiteBrush, progressX, progressY, progressX + fillWidth, progressY + progressHeight);
        }

        /// <summary>
        /// Clean up resources when overlay closes
        /// </summary>
        private void Window_DestroyGraphics(object sender, DestroyGraphicsEventArgs e)
        {
            // dispose/detach alldrawing resources to prevent memory leaks
            _blackBrush?.Dispose();
            _whiteBrush?.Dispose();
            _backgroundBrush?.Dispose();
            _titleFont?.Dispose();
            _descriptionFont?.Dispose();
        }

        /// <summary>
        /// Show the achievement notification
        /// </summary>
        public void ShowAchievement(string title, string description)
        {
            _achievementTitle = title;
            _achievementDescription = description;
            _isVisible = true;

            // Auto-hide after 5 seconds - play around with timer
            var timer = new System.Windows.Threading.DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(5);
            timer.Tick += (s, e) =>
            {
                _isVisible = false;
                timer.Stop();
            };
            timer.Start();
        }

        /// <summary>
        /// Start the overlay window
        /// </summary>
        public void Start()
        {
            _window.Create();
            _window.Join(); // keep overlay running
        }

        /// <summary>
        /// Stop the overlay window
        /// </summary>
        public void Stop()
        {
            _window?.Dispose();
        }
    }
}