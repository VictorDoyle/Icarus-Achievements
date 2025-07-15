using System;
using System.Threading.Tasks;
using GameOverlay.Drawing;
using GameOverlay.Windows;
using Graphics = GameOverlay.Drawing.Graphics;

namespace IcarusAchievements
{
    /// <summary>
    /// Small logo overlay that appears when Shift+Tab is pressed
    /// Click to open main app, or it auto-hides after 10 seconds
    /// </summary>
    public class LogoOverlay
    {
        private GraphicsWindow _window;
        private Graphics _graphics;

        private SolidBrush _backgroundBrush;
        private SolidBrush _logoBrush;
        private SolidBrush _textBrush;
        private Font _logoFont;

        private bool _isVisible = false;
        private bool _isHovered = false;

        private const float LOGO_SIZE = 60;
        private const float LOGO_X = 50;   // 50px from left edge
        private const float LOGO_Y = 50;   // 50px from top edge

        public event Action LogoClicked;

        public LogoOverlay()
        {
            _window = new GraphicsWindow(0, 0, 1920, 1080)
            {
                FPS = 60,
                IsTopmost = true,
                IsVisible = true
            };


            _window.SetupGraphics += Window_SetupGraphics;
            _window.DrawGraphics += Window_DrawGraphics;
            _window.DestroyGraphics += Window_DestroyGraphics;
        }

        private void Window_SetupGraphics(object sender, SetupGraphicsEventArgs e)
        {
            var gfx = e.Graphics;

            _backgroundBrush = gfx.CreateSolidBrush(20, 20, 20, 200);
            _logoBrush = gfx.CreateSolidBrush(100, 150, 255, 255);
            _textBrush = gfx.CreateSolidBrush(255, 255, 255, 255);
            _logoFont = gfx.CreateFont("Arial", 24, true);
        }

        private void Window_DrawGraphics(object sender, DrawGraphicsEventArgs e)
        {
            var gfx = e.Graphics;
            gfx.ClearScene();

            if (_isVisible)
            {
                DrawLogoBox(gfx);
            }
        }

        /// <summary>
        /// Draw the popup logo box on Shift+Tab rpess
        /// </summary>
        private void DrawLogoBox(Graphics gfx)
        {
            float hoverSize = _isHovered ? 5 : 0;

            gfx.FillRoundedRectangle(_backgroundBrush,
                LOGO_X - hoverSize,
                LOGO_Y - hoverSize,
                LOGO_X + LOGO_SIZE + hoverSize,
                LOGO_Y + LOGO_SIZE + hoverSize,
                15);

            gfx.DrawText(_logoFont, _logoBrush, LOGO_X + 8, LOGO_Y + 15, "IA");
            gfx.DrawText(_logoFont, _textBrush, LOGO_X + 8, LOGO_Y + 35, "🏆");

            // Optional: Draw a subtle border when hovered
            if (_isHovered)
            {
                // TODO: border effect n styling etc 
            }
        }

        /// <summary>
        /// Show the logo overlay
        /// </summary>
        public void Show()
        {
            _isVisible = true;

            // hide after 10s, maybe user wants to use steam overlay instead or other.
            Task.Delay(10000).ContinueWith(_ =>
            {
                _isVisible = false;
            });
        }

        /// <summary>
        /// Hide the logo overlay immediately, load it later during Shift + Tab
        /// </summary>
        public void Hide()
        {
            _isVisible = false;
        }

        /// <summary>
        /// Check if mouse is over the logo (for click detection)
        /// </summary>
        public bool IsMouseOverLogo(float mouseX, float mouseY)
        {
            return _isVisible &&
                   mouseX >= LOGO_X &&
                   mouseX <= LOGO_X + LOGO_SIZE &&
                   mouseY >= LOGO_Y &&
                   mouseY <= LOGO_Y + LOGO_SIZE;
        }


        public void OnMouseClick(float mouseX, float mouseY)
        {
            if (IsMouseOverLogo(mouseX, mouseY))
            {
                LogoClicked?.Invoke();
                Hide();
            }
        }

        public void Start()
        {
            _window.Create();
            _window.Join();
        }

        public void Stop()
        {
            _window?.Dispose();
        }

        private void Window_DestroyGraphics(object sender, DestroyGraphicsEventArgs e)
        {
            _backgroundBrush?.Dispose();
            _logoBrush?.Dispose();
            _textBrush?.Dispose();
            _logoFont?.Dispose();
        }
    }
}