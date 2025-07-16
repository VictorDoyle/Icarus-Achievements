using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Steamworks;

namespace IcarusAchievements
{
    /// <summary>
    /// Enhanced Steam service with real achievement tracking and user profile data
    /// </summary>
    public class SteamService
    {
        private bool _steamInitialized = false;
        private AppId_t _currentGameId;
        private string _currentGameName = "";
        private string _playerName = "";
        private ulong _steamId = 0;

        // Achievement tracking
        private List<SteamAchievement> _currentGameAchievements = new List<SteamAchievement>();
        private Dictionary<string, bool> _lastKnownStates = new Dictionary<string, bool>();

        // Events for achievement unlocks and status updates
        public event Action<SteamAchievement> AchievementUnlocked;
        public event Action<string> GameChanged;
        public event Action<string> StatusUpdate;
        public event Action<UserProfileData> ProfileUpdated;

        /// <summary>
        /// init connection to Steam
        /// </summary>
        public bool Initialize()
        {
            try
            {
                StatusUpdate?.Invoke("Checking if Steam is running...");

                if (!SteamAPI.IsSteamRunning())
                {
                    StatusUpdate?.Invoke("Steam is not running - Start Steam first");
                    return false;
                }

                StatusUpdate?.Invoke("Steam detected - Initializing API");

                _steamInitialized = SteamAPI.Init();

                if (_steamInitialized)
                {
                    StatusUpdate?.Invoke("Steam API initialized successfully");

                    // Get user profile data
                    LoadUserProfile();

                    SetupCallbacks();

                    // current game info
                    UpdateCurrentGame();

                    return true;
                }
                else
                {
                    StatusUpdate?.Invoke("Failed to initialize Steam API - Missing steam_api64.dll file");
                    return false;
                }
            }
            catch (Exception ex)
            {
                StatusUpdate?.Invoke($"Steam initialization error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Load Steam user profile information
        /// </summary>
        private void LoadUserProfile()
        {
            try
            {
                // Get Steam user info
                _steamId = SteamUser.GetSteamID().m_SteamID;
                _playerName = SteamFriends.GetPersonaName();

                StatusUpdate?.Invoke($"Logged in as: {_playerName}");

                // Fire profile update event
                UpdateProfileData();
            }
            catch (Exception ex)
            {
                StatusUpdate?.Invoke($"Error loading user profile: {ex.Message}");
            }
        }

        /// <summary>
        /// Update and broadcast current profile data
        /// </summary>
        private void UpdateProfileData()
        {
            var profileData = new UserProfileData
            {
                PlayerName = _playerName,
                SteamId = _steamId,
                CurrentGameName = _currentGameName,
                CurrentGameId = _currentGameId.ToString(),
                TotalAchievements = _currentGameAchievements.Count,
                UnlockedAchievements = _currentGameAchievements.Count(a => a.IsUnlocked),
                CompletionPercentage = CalculateCompletionPercentage(),
                LastUpdated = DateTime.Now
            };

            ProfileUpdated?.Invoke(profileData);
        }

        /// <summary>
        /// Calculate achievement completion percentage
        /// </summary>
        private double CalculateCompletionPercentage()
        {
            if (_currentGameAchievements.Count == 0) return 0;

            int unlocked = _currentGameAchievements.Count(a => a.IsUnlocked);
            return (double)unlocked / _currentGameAchievements.Count * 100.0;
        }

        /// <summary>
        /// Steam callbacks for achievement notifications
        /// </summary>
        private void SetupCallbacks()
        {
            // TODO: Real-time achievement callbacks will be implemented here
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

                // Get game name from Steam
                _currentGameName = GetGameName(_currentGameId);

                GameChanged?.Invoke(_currentGameName);
                StatusUpdate?.Invoke($"Detected game: {_currentGameName} (ID: {_currentGameId})");

                // Load achievements for this game
                LoadCurrentGameAchievements();
            }
            catch (Exception ex)
            {
                StatusUpdate?.Invoke($"Error getting current game: {ex.Message}");
            }
        }

        /// <summary>
        /// Get game name from Steam (simplified version)
        /// </summary>
        private string GetGameName(AppId_t appId)
        {
            // For now, return a formatted name
            // In a full implementation, you'd query Steam's app info
            return appId.m_AppId switch
            {
                480 => "Spacewar",
                730 => "Counter-Strike 2",
                440 => "Team Fortress 2",
                570 => "Dota 2",
                _ => $"Steam Game {appId.m_AppId}"
            };
        }

        /// <summary>
        /// Load and track achievements for the current game
        /// </summary>
        private void LoadCurrentGameAchievements()
        {
            if (!_steamInitialized) return;

            try
            {
                // Clear previous game's achievements
                _currentGameAchievements.Clear();
                _lastKnownStates.Clear();

                // Try different approaches for requesting stats
                bool statsRequested = false;

                try
                {
                    // Method 1: Direct call (most common)
                    statsRequested = SteamUserStats.RequestCurrentStats();
                }
                catch
                {
                    try
                    {
                        // Method 2: Alternative for older SDK versions
                        StatusUpdate?.Invoke("Trying alternative stats request method...");

                        // Just proceed without explicit request - some games auto-load stats
                        statsRequested = true;
                    }
                    catch (Exception ex)
                    {
                        StatusUpdate?.Invoke($"Stats request failed: {ex.Message}");
                        return;
                    }
                }

                if (!statsRequested)
                {
                    StatusUpdate?.Invoke("Unable to request stats from Steam");
                    return;
                }

                // Small delay to let Steam load the stats
                System.Threading.Thread.Sleep(100);

                // Get number of achievements for current game
                uint numAchievements = 0;
                try
                {
                    numAchievements = SteamUserStats.GetNumAchievements();
                }
                catch (Exception ex)
                {
                    StatusUpdate?.Invoke($"Error getting achievement count: {ex.Message}");
                    return;
                }

                StatusUpdate?.Invoke($"Loading {numAchievements} achievements...");

                for (uint i = 0; i < numAchievements; i++)
                {
                    try
                    {
                        // get achievement info
                        string achievementId = SteamUserStats.GetAchievementName(i);

                        if (!string.IsNullOrEmpty(achievementId))
                        {
                            // is achievement unlocked - try different methods
                            bool unlocked = false;
                            uint unlockTime = 0;

                            try
                            {
                                // Method 1: Get achievement with unlock time
                                if (SteamUserStats.GetAchievementAndUnlockTime(achievementId, out unlocked, out unlockTime))
                                {
                                    // Success - continue with this method
                                }
                                else
                                {
                                    // Method 2: Just get unlock status
                                    unlocked = SteamUserStats.GetAchievement(achievementId, out unlocked) && unlocked;
                                    unlockTime = 0;
                                }
                            }
                            catch
                            {
                                // Method 3: Fallback - assume locked
                                unlocked = false;
                                unlockTime = 0;
                            }

                            // achievement display info - with error handling
                            string displayName = achievementId;
                            string description = "No description available";
                            string hiddenDesc = "0";

                            try
                            {
                                displayName = SteamUserStats.GetAchievementDisplayAttribute(achievementId, "name") ?? achievementId;
                                description = SteamUserStats.GetAchievementDisplayAttribute(achievementId, "desc") ?? "No description available";
                                hiddenDesc = SteamUserStats.GetAchievementDisplayAttribute(achievementId, "hidden") ?? "0";
                            }
                            catch
                            {
                                // Use defaults if display attributes fail
                            }

                            var achievement = new SteamAchievement
                            {
                                Id = achievementId,
                                Name = displayName,
                                Description = description,
                                IsUnlocked = unlocked,
                                UnlockTime = unlockTime,
                                GameId = _currentGameId.ToString(),
                                IsHidden = hiddenDesc == "1",
                                Rarity = CalculateAchievementRarity(achievementId)
                            };

                            _currentGameAchievements.Add(achievement);
                            _lastKnownStates[achievementId] = unlocked;
                        }
                    }
                    catch (Exception ex)
                    {
                        StatusUpdate?.Invoke($"Error processing achievement {i}: {ex.Message}");
                        continue; // Skip this achievement and continue with others
                    }
                }

                StatusUpdate?.Invoke($"Successfully loaded {_currentGameAchievements.Count} achievements");

                // Update profile with new data
                UpdateProfileData();
            }
            catch (Exception ex)
            {
                StatusUpdate?.Invoke($"Error loading achievements: {ex.Message}");
            }
        }

        /// <summary>
        /// Calculate achievement rarity based on global stats (simplified)
        /// </summary>
        private AchievementRarity CalculateAchievementRarity(string achievementId)
        {
            // In a real implementation, you'd get global achievement percentages
            // For now, return random rarity for demonstration
            var random = new Random(achievementId.GetHashCode());
            var value = random.NextDouble();

            return value switch
            {
                < 0.01 => AchievementRarity.Legendary,  // 1%
                < 0.05 => AchievementRarity.Epic,       // 5%
                < 0.25 => AchievementRarity.Rare,       // 25%
                < 0.50 => AchievementRarity.Uncommon,   // 50%
                _ => AchievementRarity.Common            // 50%+
            };
        }

        /// <summary>
        /// Check for newly unlocked achievements
        /// Call this regularly to detect new unlocks
        /// </summary>
        public void CheckForNewAchievements()
        {
            if (!_steamInitialized || _currentGameAchievements.Count == 0) return;

            try
            {
                // Try to refresh stats - but don't fail if it doesn't work
                try
                {
                    SteamUserStats.RequestCurrentStats();
                }
                catch
                {
                    // Ignore if this fails - we'll still check current state
                }

                // Check each achievement for status changes
                foreach (var achievement in _currentGameAchievements)
                {
                    bool currentlyUnlocked = false;
                    uint unlockTime = 0;

                    try
                    {
                        // Try different methods to get achievement status
                        bool success = false;
                        try
                        {
                            success = SteamUserStats.GetAchievementAndUnlockTime(achievement.Id, out currentlyUnlocked, out unlockTime);
                        }
                        catch
                        {
                            // Fallback method
                            success = SteamUserStats.GetAchievement(achievement.Id, out currentlyUnlocked);
                            unlockTime = 0;
                        }

                        if (success)
                        {
                            bool wasUnlocked = _lastKnownStates.GetValueOrDefault(achievement.Id, false);

                            // Check if achievement was just unlocked
                            if (currentlyUnlocked && !wasUnlocked)
                            {
                                // Achievement just unlocked!
                                achievement.IsUnlocked = true;
                                achievement.UnlockTime = unlockTime;

                                _lastKnownStates[achievement.Id] = true;

                                // Fire achievement unlocked event
                                AchievementUnlocked?.Invoke(achievement);

                                StatusUpdate?.Invoke($"Achievement unlocked: {achievement.Name}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Skip this achievement if there's an error
                        continue;
                    }
                }

                // Update profile data
                UpdateProfileData();
            }
            catch (Exception ex)
            {
                StatusUpdate?.Invoke($"Error checking achievements: {ex.Message}");
            }
        }

        /// <summary>
        /// Get current game achievements
        /// </summary>
        public List<SteamAchievement> GetCurrentGameAchievements()
        {
            return new List<SteamAchievement>(_currentGameAchievements);
        }

        /// <summary>
        /// Get achievement statistics
        /// </summary>
        public AchievementStats GetAchievementStats()
        {
            var stats = new AchievementStats
            {
                TotalAchievements = _currentGameAchievements.Count,
                UnlockedAchievements = _currentGameAchievements.Count(a => a.IsUnlocked),
                LockedAchievements = _currentGameAchievements.Count(a => !a.IsUnlocked),
                CompletionPercentage = CalculateCompletionPercentage(),
                CommonAchievements = _currentGameAchievements.Count(a => a.Rarity == AchievementRarity.Common),
                RareAchievements = _currentGameAchievements.Count(a => a.Rarity == AchievementRarity.Rare),
                EpicAchievements = _currentGameAchievements.Count(a => a.Rarity == AchievementRarity.Epic),
                LegendaryAchievements = _currentGameAchievements.Count(a => a.Rarity == AchievementRarity.Legendary)
            };

            return stats;
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
                Description = "Successfully connected to Steam API and loaded achievements",
                IsUnlocked = true,
                UnlockTime = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                GameId = _currentGameId.ToString(),
                Rarity = AchievementRarity.Epic
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
        /// Get player name
        /// </summary>
        public string GetPlayerName()
        {
            return _playerName;
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

                // Check for achievement changes every update
                CheckForNewAchievements();
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

    /// <summary>
    /// Enhanced Steam achievement with rarity and additional metadata
    /// </summary>
    public class SteamAchievement
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public bool IsUnlocked { get; set; }
        public uint UnlockTime { get; set; }
        public string GameId { get; set; } = "";
        public string IconUrl { get; set; } = "";
        public bool IsHidden { get; set; }
        public AchievementRarity Rarity { get; set; } = AchievementRarity.Common;

        /// <summary>
        /// Convert unlock time to readable date
        /// </summary>
        public DateTime GetUnlockDate()
        {
            return DateTimeOffset.FromUnixTimeSeconds(UnlockTime).DateTime;
        }

        /// <summary>
        /// Get rarity color for UI
        /// </summary>
        public string GetRarityColor()
        {
            return Rarity switch
            {
                AchievementRarity.Common => "#CCCCCC",      // Gray
                AchievementRarity.Uncommon => "#1EFF00",    // Green  
                AchievementRarity.Rare => "#0070DD",       // Blue
                AchievementRarity.Epic => "#A335EE",       // Purple
                AchievementRarity.Legendary => "#FF8000",  // Orange
                _ => "#CCCCCC"
            };
        }
    }

    /// <summary>
    /// Achievement rarity levels
    /// </summary>
    public enum AchievementRarity
    {
        Common,
        Uncommon,
        Rare,
        Epic,
        Legendary
    }

    /// <summary>
    /// User profile data from Steam
    /// </summary>
    public class UserProfileData
    {
        public string PlayerName { get; set; } = "";
        public ulong SteamId { get; set; }
        public string CurrentGameName { get; set; } = "";
        public string CurrentGameId { get; set; } = "";
        public int TotalAchievements { get; set; }
        public int UnlockedAchievements { get; set; }
        public double CompletionPercentage { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    /// <summary>
    /// Achievement statistics for current game
    /// </summary>
    public class AchievementStats
    {
        public int TotalAchievements { get; set; }
        public int UnlockedAchievements { get; set; }
        public int LockedAchievements { get; set; }
        public double CompletionPercentage { get; set; }
        public int CommonAchievements { get; set; }
        public int RareAchievements { get; set; }
        public int EpicAchievements { get; set; }
        public int LegendaryAchievements { get; set; }
    }
}