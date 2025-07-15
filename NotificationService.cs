using System;
using System.Windows;

namespace IcarusAchievements
{
    /// <summary>
    /// Simple notification service using MessageBox
    /// Reliable and works without any external dependencies
    /// </summary>
    public class NotificationService
    {
        /// <summary>
        /// Show a Steam-related notification
        /// </summary>
        public void ShowSteamStatus(string message, bool isError = false)
        {
            string title = isError ? "Steam Connection Error" : "Steam Status";
            MessageBoxImage icon = isError ? MessageBoxImage.Error : MessageBoxImage.Information;

            MessageBox.Show(message, title, MessageBoxButton.OK, icon);
        }

        /// <summary>
        /// Cleanup notifications
        /// </summary>
        public void Dispose()
        {
            // Nothing to dispose
        }
    }
}