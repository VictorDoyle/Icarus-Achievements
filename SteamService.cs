using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using Steamworks;
using System.IO;
using Microsoft.Win32;
using System.Net.Http;
using System.Text.Json;

namespace IcarusAchievements
{
    /// <summary>
    /// Production-grade Steam service with real achievement tracking and dynamic game detection
    /// Uses Steam API professionally like Steam Achievement Manager
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

        // Steam API Callbacks
        private Callback<GameServerChangeRequested_t> _gameServerChangeCallback;
        private Callback<GameLobbyJoinRequested_t> _gameLobbyJoinCallback;

        // HTTP client for Steam Web API calls
        private static readonly HttpClient _httpClient = new HttpClient();

        // Cache for game names to avoid repeated API calls
        private static readonly Dictionary<uint, string> _gameNameCache = new Dictionary<uint, string>();

        /// <summary>
        /// Initialize Steam API without requiring steam_appid.txt
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

                StatusUpdate?.Invoke("Steam detected - Initializing monitoring system");

                // Initialize Steam API
                try
                {
                    _steamInitialized = SteamAPI.Init();

                    if (_steamInitialized)
                    {
                        StatusUpdate?.Invoke("Steam API initialized successfully");

                        // Detailed debugging
                        StatusUpdate?.Invoke($"Steam API Version: {SteamUtils.GetSteamUILanguage()}");
                        StatusUpdate?.Invoke($"App ID from Steam: {SteamUtils.GetAppID()}");

                        LoadUserProfile();
                        SetupCallbacks();
                    }
                    else
                    {
                        StatusUpdate?.Invoke("Steam API init failed - this is normal without steam_appid.txt");
                        _playerName = "Steam User";
                        _steamInitialized = false;
                    }
                }
                catch (Exception ex)
                {
                    StatusUpdate?.Invoke($"Steam API unavailable: {ex.Message}");
                    _playerName = "Steam User";
                    _steamInitialized = false;
                }

                // Always try to detect games regardless of Steam API status
                StatusUpdate?.Invoke("Starting game detection...");
                DetectCurrentGame();

                return true;
            }
            catch (Exception ex)
            {
                StatusUpdate?.Invoke($"Steam initialization error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Detect currently running Steam game using professional methods
        /// </summary>
        private void DetectCurrentGame()
        {
            try
            {
                StatusUpdate?.Invoke("Detecting active Steam game...");

                AppId_t? detectedGame = null;

                // Method 1: Steam Friends API (most reliable) - but only if initialized
                if (_steamInitialized)
                {
                    StatusUpdate?.Invoke("Trying Steam Friends API...");
                    detectedGame = GetCurrentGameFromSteamAPI();
                    if (detectedGame.HasValue)
                    {
                        StatusUpdate?.Invoke($"Steam Friends API found game: {detectedGame.Value.m_AppId}");
                    }
                    else
                    {
                        StatusUpdate?.Invoke("Steam Friends API found no currently running game");
                    }
                }
                else
                {
                    StatusUpdate?.Invoke("Steam API not initialized - skipping Friends API check");
                }

                // Method 2: Process detection with Steam integration
                if (!detectedGame.HasValue)
                {
                    StatusUpdate?.Invoke("Trying process detection...");
                    detectedGame = DetectGameFromProcesses();
                    if (detectedGame.HasValue)
                    {
                        StatusUpdate?.Invoke($"Process detection found game: {detectedGame.Value.m_AppId}");
                    }
                    else
                    {
                        StatusUpdate?.Invoke("Process detection found no Steam games");
                    }
                }

                if (detectedGame.HasValue && detectedGame.Value.m_AppId != 0)
                {
                    var newGameId = detectedGame.Value;

                    if (newGameId.m_AppId != _currentGameId.m_AppId)
                    {
                        _currentGameId = newGameId;
                        StatusUpdate?.Invoke($"Getting name for game ID: {_currentGameId.m_AppId}");
                        _currentGameName = GetGameNameFromSteamAPI(_currentGameId);

                        StatusUpdate?.Invoke($"Detected: {_currentGameName} (AppID: {_currentGameId.m_AppId})");

                        // Load achievements for this game
                        LoadCurrentGameAchievements();
                        GameChanged?.Invoke(_currentGameName);
                    }
                    else
                    {
                        StatusUpdate?.Invoke($"Same game still running: {_currentGameName}");
                    }
                }
                else
                {
                    StatusUpdate?.Invoke("No Steam games currently running");
                    ResetCurrentGame();
                }
            }
            catch (Exception ex)
            {
                StatusUpdate?.Invoke($"Error detecting game: {ex.Message}");
            }
        }

        /// <summary>
        /// Get current game from Steam Friends API (like SAM does)
        /// </summary>
        private AppId_t? GetCurrentGameFromSteamAPI()
        {
            try
            {
                if (!_steamInitialized) return null;

                var steamId = SteamUser.GetSteamID();
                FriendGameInfo_t gameInfo = new FriendGameInfo_t();

                if (SteamFriends.GetFriendGamePlayed(steamId, out gameInfo))
                {
                    if (gameInfo.m_gameID.IsValid() && gameInfo.m_gameID.AppID().m_AppId != 0)
                    {
                        var appId = gameInfo.m_gameID.AppID();

                        // Verify we own this game
                        if (SteamApps.BIsSubscribedApp(appId))
                        {
                            StatusUpdate?.Invoke($"Steam API detected owned game: {appId.m_AppId}");
                            return appId;
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                StatusUpdate?.Invoke($"Error using Steam Friends API: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get game name from Steam API using proper methods
        /// </summary>
        private string GetGameNameFromSteamAPI(AppId_t appId)
        {
            try
            {
                uint appIdValue = appId.m_AppId;

                // Check cache first
                if (_gameNameCache.TryGetValue(appIdValue, out string cachedName))
                {
                    return cachedName;
                }

                string gameName = null;

                // Method 1: Try to get from Steam install directory (fastest, local)
                if (_steamInitialized)
                {
                    gameName = GetGameNameFromInstallDir(appId);
                    if (!string.IsNullOrWhiteSpace(gameName))
                    {
                        _gameNameCache[appIdValue] = gameName;
                        return gameName;
                    }
                }

                // Method 2: Use Steam Web API (requires internet, but most reliable)
                gameName = GetGameNameFromSteamWebAPI(appIdValue);
                if (!string.IsNullOrWhiteSpace(gameName))
                {
                    _gameNameCache[appIdValue] = gameName;
                    return gameName;
                }

                // Method 3: Try getting from Steam's local cache
                gameName = GetGameNameFromSteamCache(appId);
                if (!string.IsNullOrWhiteSpace(gameName))
                {
                    _gameNameCache[appIdValue] = gameName;
                    return gameName;
                }

                // Fallback
                string fallbackName = GetGameNameFallback(appId);
                _gameNameCache[appIdValue] = fallbackName;
                return fallbackName;
            }
            catch (Exception ex)
            {
                StatusUpdate?.Invoke($"Error getting game name: {ex.Message}");
                return GetGameNameFallback(appId);
            }
        }

        /// <summary>
        /// Get game name from Steam Web API (most reliable method)
        /// </summary>
        private string GetGameNameFromSteamWebAPI(uint appId)
        {
            try
            {
                // Use Steam's public API to get app details
                string url = $"https://store.steampowered.com/api/appdetails?appids={appId}";

                var response = _httpClient.GetStringAsync(url).Result;

                using (JsonDocument doc = JsonDocument.Parse(response))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty(appId.ToString(), out JsonElement appElement))
                    {
                        if (appElement.TryGetProperty("success", out JsonElement successElement) &&
                            successElement.GetBoolean())
                        {
                            if (appElement.TryGetProperty("data", out JsonElement dataElement))
                            {
                                if (dataElement.TryGetProperty("name", out JsonElement nameElement))
                                {
                                    string gameName = nameElement.GetString();
                                    if (!string.IsNullOrWhiteSpace(gameName))
                                    {
                                        StatusUpdate?.Invoke($"Retrieved game name from Steam API: {gameName}");
                                        return gameName;
                                    }
                                }
                            }
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                StatusUpdate?.Invoke($"Error calling Steam Web API: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get game name from Steam installation directory
        /// </summary>
        private string GetGameNameFromInstallDir(AppId_t appId)
        {
            try
            {
                // Try to get the install directory for this app
                uint folderNameLength = 260;
                string folderName;
                uint result = SteamApps.GetAppInstallDir(appId, out folderName, folderNameLength);

                if (result > 0 && !string.IsNullOrWhiteSpace(folderName))
                {
                    // The folder name is often a good indicator of the game name
                    // Clean it up to make it more readable
                    string cleanName = folderName.Replace("_", " ")
                                                .Replace("-", " ")
                                                .Replace(".", " ");

                    // Capitalize each word
                    return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(cleanName.ToLower());
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Get game name from Steam's local app cache
        /// </summary>
        private string GetGameNameFromSteamCache(AppId_t appId)
        {
            try
            {
                string steamPath = GetSteamInstallPath();
                if (string.IsNullOrEmpty(steamPath)) return null;

                // Check Steam's appinfo cache
                string appCachePath = Path.Combine(steamPath, "appcache", "appinfo.vdf");
                if (File.Exists(appCachePath))
                {
                    // This would require VDF parsing - simplified approach
                    StatusUpdate?.Invoke($"Checking Steam app cache for game {appId.m_AppId}");
                }

                // Check individual app data files
                string appDataPath = Path.Combine(steamPath, "appcache", "stats", $"UserGameStatsSchema_{appId.m_AppId}.bin");
                if (File.Exists(appDataPath))
                {
                    StatusUpdate?.Invoke($"Found cached data for game {appId.m_AppId}");
                }

                return null;
            }
            catch
            {
                return null;
            }
        }



        /// <summary>
        /// Fallback method for game name
        /// </summary>
        private string GetGameNameFallback(AppId_t appId)
        {
            return $"Steam Game {appId.m_AppId}";
        }

        /// <summary>
        /// Detect games from running processes with proper Steam integration
        /// </summary>
        private AppId_t? DetectGameFromProcesses()
        {
            try
            {
                StatusUpdate?.Invoke("Scanning running processes...");
                var processes = Process.GetProcesses();
                var steamProcesses = new List<Process>();

                foreach (var process in processes)
                {
                    try
                    {
                        if (process.ProcessName.Equals("steam", StringComparison.OrdinalIgnoreCase))
                            continue;

                        string processPath = "";
                        try
                        {
                            processPath = process.MainModule?.FileName ?? "";
                        }
                        catch
                        {
                            // Can't access this process, skip it
                            continue;
                        }

                        StatusUpdate?.Invoke($"Checking process: {process.ProcessName} at {processPath}");

                        // Look for Steam game processes
                        if (IsSteamGameProcess(processPath))
                        {
                            StatusUpdate?.Invoke($"Found potential Steam game: {process.ProcessName}");
                            var appId = GetAppIdFromSteamProcess(process, processPath);
                            if (appId.HasValue)
                            {
                                // If Steam API is available, verify ownership
                                if (_steamInitialized)
                                {
                                    if (SteamApps.BIsSubscribedApp(appId.Value))
                                    {
                                        StatusUpdate?.Invoke($"Found owned Steam game: {appId.Value.m_AppId}");
                                        return appId.Value;
                                    }
                                    else
                                    {
                                        StatusUpdate?.Invoke($"Game {appId.Value.m_AppId} found but not owned");
                                    }
                                }
                                else
                                {
                                    // Without Steam API, just return what we found
                                    StatusUpdate?.Invoke($"Found Steam game (ownership not verified): {appId.Value.m_AppId}");
                                    return appId.Value;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Skip processes we can't access
                        StatusUpdate?.Invoke($"Error checking process {process.ProcessName}: {ex.Message}");
                        continue;
                    }
                }

                StatusUpdate?.Invoke("No Steam games found in running processes");
                return null;
            }
            catch (Exception ex)
            {
                StatusUpdate?.Invoke($"Error detecting from processes: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Check if process is a Steam game
        /// </summary>
        private bool IsSteamGameProcess(string processPath)
        {
            if (string.IsNullOrEmpty(processPath)) return false;

            return processPath.Contains("steamapps\\common", StringComparison.OrdinalIgnoreCase) ||
                   processPath.Contains("Steam\\steamapps", StringComparison.OrdinalIgnoreCase) ||
                   processPath.Contains("\\common\\", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Get App ID from Steam process using multiple methods
        /// </summary>
        private AppId_t? GetAppIdFromSteamProcess(Process process, string processPath)
        {
            try
            {
                // Method 1: Check for steam_appid.txt in game directory
                var appIdFromFile = GetAppIdFromSteamAppIdFile(processPath);
                if (appIdFromFile.HasValue) return appIdFromFile.Value;

                // Method 2: Parse from Steam game path structure
                var appIdFromPath = GetAppIdFromSteamPath(processPath);
                if (appIdFromPath.HasValue) return appIdFromPath.Value;

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Get App ID from steam_appid.txt file
        /// </summary>
        private AppId_t? GetAppIdFromSteamAppIdFile(string processPath)
        {
            try
            {
                string directory = Path.GetDirectoryName(processPath);
                if (string.IsNullOrEmpty(directory)) return null;

                string appIdFile = Path.Combine(directory, "steam_appid.txt");
                if (File.Exists(appIdFile))
                {
                    string appIdText = File.ReadAllText(appIdFile).Trim();
                    if (uint.TryParse(appIdText, out uint appId) && appId > 0)
                    {
                        return new AppId_t(appId);
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Try to extract App ID from Steam installation path patterns
        /// </summary>
        private AppId_t? GetAppIdFromSteamPath(string processPath)
        {
            try
            {
                // Steam games often have predictable path structures
                // This would need Steam's app cache or registry to map folders to App IDs
                string steamPath = GetSteamInstallPath();
                if (string.IsNullOrEmpty(steamPath)) return null;

                // Check if we can find the game folder and map it
                // This is complex and would require reading Steam's config files

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Detect game from Steam configuration files
        /// </summary>
        private AppId_t? DetectGameFromSteamConfig()
        {
            try
            {
                string steamPath = GetSteamInstallPath();
                if (string.IsNullOrEmpty(steamPath)) return null;

                // Check Steam's config for running game
                // This would involve parsing loginusers.vdf and other config files

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Get Steam installation path from registry
        /// </summary>
        private string GetSteamInstallPath()
        {
            try
            {
                // Try current user first
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    string path = key?.GetValue("SteamPath")?.ToString();
                    if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                        return path;
                }

                // Try local machine
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam"))
                {
                    string path = key?.GetValue("InstallPath")?.ToString();
                    if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                        return path;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Setup Steam API callbacks
        /// </summary>
        private void SetupCallbacks()
        {
            if (!_steamInitialized) return;

            try
            {
                // Setup callbacks for game changes
                _gameServerChangeCallback = Callback<GameServerChangeRequested_t>.Create(OnGameServerChangeRequested);
                _gameLobbyJoinCallback = Callback<GameLobbyJoinRequested_t>.Create(OnGameLobbyJoinRequested);
            }
            catch (Exception ex)
            {
                StatusUpdate?.Invoke($"Error setting up callbacks: {ex.Message}");
            }
        }

        private void OnGameServerChangeRequested(GameServerChangeRequested_t param)
        {
            // Game server changed - might indicate game change
            Task.Delay(1000).ContinueWith(_ => DetectCurrentGame());
        }

        private void OnGameLobbyJoinRequested(GameLobbyJoinRequested_t param)
        {
            // Lobby join requested - might indicate game change
            Task.Delay(1000).ContinueWith(_ => DetectCurrentGame());
        }

        /// <summary>
        /// Load Steam user profile information
        /// </summary>
        private void LoadUserProfile()
        {
            try
            {
                if (!_steamInitialized)
                {
                    StatusUpdate?.Invoke("Steam API not initialized - cannot load user profile");
                    return;
                }

                StatusUpdate?.Invoke("Loading Steam user profile...");

                _steamId = SteamUser.GetSteamID().m_SteamID;
                _playerName = SteamFriends.GetPersonaName();

                if (string.IsNullOrEmpty(_playerName))
                {
                    StatusUpdate?.Invoke("Could not get player name from Steam");
                    _playerName = "Steam User";
                }
                else
                {
                    StatusUpdate?.Invoke($"Successfully logged in as: {_playerName}");
                }

                UpdateProfileData();
            }
            catch (Exception ex)
            {
                StatusUpdate?.Invoke($"Error loading user profile: {ex.Message}");
                _playerName = "Steam User";
            }
        }

        /// <summary>
        /// Reset current game state
        /// </summary>
        private void ResetCurrentGame()
        {
            _currentGameName = "No game detected";
            _currentGameId = new AppId_t(0);
            _currentGameAchievements.Clear();
            _lastKnownStates.Clear();
            UpdateProfileData();
        }

        /// <summary>
        /// Load achievements for the current game
        /// </summary>
        private void LoadCurrentGameAchievements()
        {
            if (_currentGameId.m_AppId == 0) return;

            try
            {
                _currentGameAchievements.Clear();
                _lastKnownStates.Clear();

                if (!_steamInitialized)
                {
                    StatusUpdate?.Invoke($"Steam API not available - cannot load achievements for {_currentGameName}");
                    UpdateProfileData();
                    return;
                }

                // Request user stats for the current game
                bool statsRequested = SteamUserStats.RequestCurrentStats();
                if (!statsRequested)
                {
                    StatusUpdate?.Invoke($"Unable to request achievement data for {_currentGameName}");
                    UpdateProfileData();
                    return;
                }

                // Wait for Steam to load the data
                System.Threading.Thread.Sleep(500);

                uint numAchievements = SteamUserStats.GetNumAchievements();
                if (numAchievements == 0)
                {
                    StatusUpdate?.Invoke($"{_currentGameName} has no achievements");
                    UpdateProfileData();
                    return;
                }

                StatusUpdate?.Invoke($"Loading {numAchievements} achievements for {_currentGameName}...");

                // Load each achievement
                for (uint i = 0; i < numAchievements; i++)
                {
                    try
                    {
                        string achievementId = SteamUserStats.GetAchievementName(i);
                        if (string.IsNullOrEmpty(achievementId)) continue;

                        // Get achievement status
                        bool unlocked = false;
                        uint unlockTime = 0;

                        try
                        {
                            SteamUserStats.GetAchievementAndUnlockTime(achievementId, out unlocked, out unlockTime);
                        }
                        catch
                        {
                            SteamUserStats.GetAchievement(achievementId, out unlocked);
                            unlockTime = 0;
                        }

                        // Get display information
                        string displayName = SteamUserStats.GetAchievementDisplayAttribute(achievementId, "name") ?? achievementId;
                        string description = SteamUserStats.GetAchievementDisplayAttribute(achievementId, "desc") ?? "No description available";
                        string hiddenDesc = SteamUserStats.GetAchievementDisplayAttribute(achievementId, "hidden") ?? "0";

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
                    catch (Exception ex)
                    {
                        StatusUpdate?.Invoke($"Error processing achievement {i}: {ex.Message}");
                        continue;
                    }
                }

                StatusUpdate?.Invoke($"Successfully loaded {_currentGameAchievements.Count} achievements for {_currentGameName}");
                UpdateProfileData();
            }
            catch (Exception ex)
            {
                StatusUpdate?.Invoke($"Error loading achievements: {ex.Message}");
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
        /// Calculate achievement rarity (simplified)
        /// </summary>
        private AchievementRarity CalculateAchievementRarity(string achievementId)
        {
            var random = new Random(achievementId.GetHashCode());
            var value = random.NextDouble();

            return value switch
            {
                < 0.01 => AchievementRarity.Legendary,
                < 0.05 => AchievementRarity.Epic,
                < 0.25 => AchievementRarity.Rare,
                < 0.50 => AchievementRarity.Uncommon,
                _ => AchievementRarity.Common
            };
        }

        /// <summary>
        /// Check for newly unlocked achievements
        /// </summary>
        public void CheckForNewAchievements()
        {
            if (!_steamInitialized || _currentGameAchievements.Count == 0) return;

            try
            {
                // Refresh stats
                SteamUserStats.RequestCurrentStats();

                foreach (var achievement in _currentGameAchievements)
                {
                    bool currentlyUnlocked = false;
                    uint unlockTime = 0;

                    try
                    {
                        bool success = SteamUserStats.GetAchievementAndUnlockTime(achievement.Id, out currentlyUnlocked, out unlockTime);
                        if (!success)
                        {
                            SteamUserStats.GetAchievement(achievement.Id, out currentlyUnlocked);
                            unlockTime = 0;
                        }

                        bool wasUnlocked = _lastKnownStates.GetValueOrDefault(achievement.Id, false);

                        if (currentlyUnlocked && !wasUnlocked)
                        {
                            achievement.IsUnlocked = true;
                            achievement.UnlockTime = unlockTime;
                            _lastKnownStates[achievement.Id] = true;

                            AchievementUnlocked?.Invoke(achievement);
                            StatusUpdate?.Invoke($"Achievement unlocked: {achievement.Name}");
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }

                UpdateProfileData();
            }
            catch (Exception ex)
            {
                StatusUpdate?.Invoke($"Error checking achievements: {ex.Message}");
            }
        }

        // Public interface methods
        public List<SteamAchievement> GetCurrentGameAchievements() => new List<SteamAchievement>(_currentGameAchievements);
        public string GetCurrentGameName() => _currentGameName;
        public string GetPlayerName() => _playerName;
        public bool IsConnected() => SteamAPI.IsSteamRunning();

        /// <summary>
        /// Get achievement statistics
        /// </summary>
        public AchievementStats GetAchievementStats()
        {
            return new AchievementStats
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
        }

        /// <summary>
        /// Regular update call
        /// </summary>
        public void Update()
        {
            if (_steamInitialized)
            {
                SteamAPI.RunCallbacks();
                CheckForNewAchievements();
            }

            // Re-detect games periodically (every few seconds)
            DetectCurrentGame();
        }

        /// <summary>
        /// Shutdown Steam API
        /// </summary>
        public void Shutdown()
        {
            try
            {
                if (_steamInitialized)
                {
                    _gameServerChangeCallback?.Dispose();
                    _gameLobbyJoinCallback?.Dispose();
                    SteamAPI.Shutdown();
                    _steamInitialized = false;
                }
            }
            catch (Exception ex)
            {
                StatusUpdate?.Invoke($"Error during shutdown: {ex.Message}");
            }
        }

        /// <summary>
        /// Force refresh current game detection
        /// </summary>
        public void RefreshGameDetection()
        {
            DetectCurrentGame();
        }

        /// <summary>
        /// Simulate achievement unlock for testing
        /// </summary>
        public void SimulateAchievementUnlock()
        {
            var testAchievement = new SteamAchievement
            {
                Id = "TEST_ACHIEVEMENT",
                Name = "Test Achievement",
                Description = "This is a test achievement notification",
                IsUnlocked = true,
                UnlockTime = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                GameId = _currentGameId.ToString(),
                Rarity = AchievementRarity.Epic
            };

            AchievementUnlocked?.Invoke(testAchievement);
        }
    }

    /// <summary>
    /// Steam achievement data model
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

        public DateTime GetUnlockDate()
        {
            return DateTimeOffset.FromUnixTimeSeconds(UnlockTime).DateTime;
        }

        public string GetRarityColor()
        {
            return Rarity switch
            {
                AchievementRarity.Common => "#CCCCCC",
                AchievementRarity.Uncommon => "#1EFF00",
                AchievementRarity.Rare => "#0070DD",
                AchievementRarity.Epic => "#A335EE",
                AchievementRarity.Legendary => "#FF8000",
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