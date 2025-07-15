using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Steamworks;

namespace IcarusAchievements
{
    /// <summary>
    /// Handles all Steam API;achievement data, detects current game, tracks progress
    /// </summary>
    public class SteamService
    {
        private bool _steamInitialized = false;
        private AppId_t _currentGameId;
        private string _currentGameName = "";

        // Events for achievement unlocks
        public event Action<SteamAchievement> AchievementUnlocked;
        public event Action<string> GameChanged;

        /// <summary>
        /// init connection to Steam
        /// </summary>
        public bool Initialize()
        {
            try
            {
                // Check if Steam is running
                if (!SteamAPI.IsSteamRunning())
                {
                    Console.WriteLine("Steam is not running");
                    return false;
                }

                _steamInitialized = SteamAPI.Init();

                if (_steamInitialized)
                {
                    Console.WriteLine("Steam API initialized successfully");

                    SetupCallbacks();

                    // current game info
                    UpdateCurrentGame();

                    return true;
                }
                else
                {
                    Console.WriteLine("Failed to initialize Steam API");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Steam initialization error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Steam callbacks for achievement notifications
        /// </summary>
        private void SetupCallbacks()
        {
            // TODO: listen for achievement unlock events
        }

        /// <summary>
        /// Update current game information
        /// </summary>
        private void UpdateCurrentGame()
        {
            if (!_steamInitialized) return;

            try
            {
                // Get the current running app ID
                _currentGameId = SteamUtils.GetAppID();

                // Get game name (this is simplified - normally you'd look this up)
                _currentGameName = $"Steam App {_currentGameId}";

                GameChanged?.Invoke(_currentGameName);
                Console.WriteLine($"Detected app: {_currentGameName} (ID: {_currentGameId})");
            }

            catch (Exception ex)
            {
                Console.WriteLine($"Error getting current game: {ex.Message}");
            }
        }

        /// <summary>
        /// achievements for the current game
        /// </summary>
        public List<SteamAchievement> GetCurrentGameAchievements()
        {
            var achievements = new List<SteamAchievement>();

            if (!_steamInitialized) return achievements;

            try
            {
                // get number of achievements for current game
                uint numAchievements = SteamUserStats.GetNumAchievements();

                for (uint i = 0; i < numAchievements; i++)
                {
                    // get achievement info
                    string achievementId = SteamUserStats.GetAchievementName(i);

                    if (!string.IsNullOrEmpty(achievementId))
                    {
                        // is achivement unlocked
                        bool unlocked = false;
                        uint unlockTime = 0;

                        if (SteamUserStats.GetAchievementAndUnlockTime(achievementId, out unlocked, out unlockTime))
                        {
                            // achievement display info
                            string displayName = SteamUserStats.GetAchievementDisplayAttribute(achievementId, "name");
                            string description = SteamUserStats.GetAchievementDisplayAttribute(achievementId, "desc");

                            var achievement = new SteamAchievement
                            {
                                Id = achievementId,
                                Name = displayName ?? achievementId,
                                Description = description ?? "",
                                IsUnlocked = unlocked,
                                UnlockTime = unlockTime,
                                GameId = _currentGameId.ToString()
                            };

                            achievements.Add(achievement);
                        }
                    }
                }

                Console.WriteLine($"Found {achievements.Count} achievements for current game");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting achievements: {ex.Message}");
            }

            return achievements;
        }

        /// <summary>
        /// Check for newly unlocked achievements
        /// Call this regularly to detect new unlocks
        /// </summary>
        public void CheckForNewAchievements()
        {
            if (!_steamInitialized) return;

            try
            {
                // req fresh achievement data from Steam
                SteamUserStats.RequestCurrentStats();

                // TODO: V1 will need to check cached state instead of always requesting
                // then compare against previous and hit new unlock route
                var currentAchievements = GetCurrentGameAchievements();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking achievements: {ex.Message}");
            }
        }

        /// <summary>
        /// Test and simulate an achievement unlock for Debug
        /// </summary>
        public void SimulateAchievementUnlock()
        {
            var testAchievement = new SteamAchievement
            {
                Id = "TEST_ACHIEVEMENT",
                Name = "Steam Integration Working!",
                Description = "Successfully connected to Steam API",
                IsUnlocked = true,
                UnlockTime = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                GameId = _currentGameId.ToString()
            };

            AchievementUnlocked?.Invoke(testAchievement);
        }

        /// <summary>
        /// Get current game name
        /// </summary>
        public string GetCurrentGameName()
        {
            return _currentGameName;
        }

        /// <summary>
        /// Check if Steam is connected and working
        /// </summary>
        public bool IsConnected()
        {
            return _steamInitialized && SteamAPI.IsSteamRunning();
        }

        /// <summary>
        /// Regular update call - processes Steam callbacks
        /// </summary>
        public void Update()
        {
            if (_steamInitialized)
            {
                SteamAPI.RunCallbacks();
            }
        }
        public void Shutdown()
        {
            if (_steamInitialized)
            {
                SteamAPI.Shutdown();
                _steamInitialized = false;
            }
        }
    }

    public class SteamAchievement
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public bool IsUnlocked { get; set; }
        public uint UnlockTime { get; set; }
        public string GameId { get; set; } = "";
        public string IconUrl { get; set; } = "";

        /// <summary>
        /// Convert unlock time to readable date
        /// </summary>
        public DateTime GetUnlockDate()
        {
            return DateTimeOffset.FromUnixTimeSeconds(UnlockTime).DateTime;
        }
    }
}