using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Playnite.SDK;
using Playnite.SDK.Models;
using Newtonsoft.Json;

namespace YgpPlaytimeSync
{
    /// <summary>
    /// Resolves game identities between Playnite and Your Gamer Profile.
    /// Priority: SourceId > Steam AppID > GOG ID > Epic ID > name matching > manual mapping.
    /// </summary>
    public class GameIdentityResolver
    {
        private readonly ILogger _logger;
        private readonly GameMappingStorage _mappingStorage;
        private readonly Dictionary<string, GameIdentity> _identityCache;

        public GameIdentityResolver(ILogger logger, string dataPath)
        {
            _logger = logger;
            _mappingStorage = new GameMappingStorage(logger, dataPath);
            _identityCache = new Dictionary<string, GameIdentity>();
        }

        /// <summary>
        /// Resolves the YGP game identity for a Playnite game.
        /// </summary>
        public async Task<GameIdentityResolution> ResolveAsync(Game game)
        {
            if (game == null)
                throw new ArgumentNullException(nameof(game));

            var result = new GameIdentityResolution
            {
                PlayniteGameId = game.Id.ToString(),
                GameName = game.Name,
                Source = game.Source?.Name ?? "Unknown",
                SourceId = game.SourceId.ToString(),
                Confidence = IdentityConfidence.None,
                Status = IdentityStatus.Unknown
            };

            // 1. Check manual mapping first
            var manualMapping = await _mappingStorage.GetMappingAsync(game.Id.ToString());
            if (manualMapping != null)
            {
                result.YgpGameId = manualMapping.YgpGameId;
                result.YgpGameName = manualMapping.YgpGameName;
                result.Confidence = IdentityConfidence.Manual;
                result.Status = IdentityStatus.Mapped;
                result.MappingType = MappingType.Manual;
                _logger.Debug($"Manual mapping found for {game.Name}: {manualMapping.YgpGameName}");
                return result;
            }

            // 2. Try source-specific ID resolution
            if (game.SourceId != Guid.Empty)
            {
                var identity = await TryResolveByIdAsync(game);
                if (identity != null)
                {
                    result.YgpGameId = identity.YgpGameId;
                    result.YgpGameName = identity.YgpGameName;
                    result.Confidence = IdentityConfidence.High;
                    result.Status = IdentityStatus.Mapped;
                    result.MappingType = MappingType.SourceId;
                    return result;
                }
            }

            // 3. Try name-based matching
            var nameMatch = await TryResolveByNameAsync(game);
            if (nameMatch != null)
            {
                result.YgpGameId = nameMatch.YgpGameId;
                result.YgpGameName = nameMatch.YgpGameName;
                result.Confidence = IdentityConfidence.Medium;
                result.Status = IdentityStatus.Mapped;
                result.MappingType = MappingType.NameMatch;
                return result;
            }

            // 4. No match found
            result.Status = IdentityStatus.Unmapped;
            result.Confidence = IdentityConfidence.None;
            result.RequiresManualMapping = true;
            _logger.Warn($"No identity resolution found for {game.Name}, manual mapping required");

            return result;
        }

        private async Task<GameIdentity> TryResolveByIdAsync(Game game)
        {
            // A resolução por busca no site é feita pelo YgpProvider; aqui só valem os mapeamentos já salvos.
            _logger.Debug($"Trying ID-based resolution for {game.Name} with SourceId: {game.SourceId}");
            
            return null;
        }

        private async Task<GameIdentity> TryResolveByNameAsync(Game game)
        {
            _logger.Debug($"Trying name-based resolution for {game.Name}");
            
            return null;
        }

        /// <summary>
        /// Creates a manual mapping between Playnite and YGP games.
        /// </summary>
        public async Task CreateManualMappingAsync(string playniteGameId, string ygpGameId, string ygpGameName)
        {
            if (string.IsNullOrEmpty(playniteGameId))
                throw new ArgumentException("Playnite game ID is required");
            if (string.IsNullOrEmpty(ygpGameId))
                throw new ArgumentException("YGP game ID is required");

            await _mappingStorage.SaveMappingAsync(new GameMapping
            {
                PlayniteGameId = playniteGameId,
                YgpGameId = ygpGameId,
                YgpGameName = ygpGameName,
                CreatedAtUtc = DateTime.UtcNow,
                MappingType = MappingType.Manual
            });

            _logger.Info($"Created manual mapping: Playnite={playniteGameId} -> YGP={ygpGameId}");
        }

        /// <summary>
        /// Gets a mapping by Playnite game ID.
        /// </summary>
        public async Task<GameMapping> GetMappingAsync(string playniteGameId)
        {
            return await _mappingStorage.GetMappingAsync(playniteGameId);
        }

        /// <summary>
        /// Removes a manual mapping.
        /// </summary>
        public async Task RemoveMappingAsync(string playniteGameId)
        {
            await _mappingStorage.RemoveMappingAsync(playniteGameId);
            _logger.Info($"Removed mapping for Playnite game: {playniteGameId}");
        }

        /// <summary>
        /// Gets all manual mappings.
        /// </summary>
        public async Task<IEnumerable<GameMapping>> GetAllMappingsAsync()
        {
            return await _mappingStorage.GetAllMappingsAsync();
        }

        /// <summary>
        /// Clears all mappings.
        /// </summary>
        public async Task ClearAllMappingsAsync()
        {
            await _mappingStorage.ClearAllMappingsAsync();
            _logger.Info("Cleared all game mappings");
        }
    }

    /// <summary>
    /// Result of game identity resolution.
    /// </summary>
    public class GameIdentityResolution
    {
        public string PlayniteGameId { get; set; }
        public string GameName { get; set; }
        public string Source { get; set; }
        public string SourceId { get; set; }
        public string YgpGameId { get; set; }
        public string YgpGameName { get; set; }
        public IdentityConfidence Confidence { get; set; }
        public IdentityStatus Status { get; set; }
        public MappingType MappingType { get; set; }
        public bool RequiresManualMapping { get; set; }
    }

    public enum IdentityConfidence
    {
        None,
        Low,
        Medium,
        High,
        Manual
    }

    public enum IdentityStatus
    {
        Unknown,
        Unmapped,
        Mapped
    }

    public enum MappingType
    {
        None,
        SourceId,
        NameMatch,
        Manual
    }

    /// <summary>
    /// Represents a game identity with platform-specific identifiers.
    /// </summary>
    public class GameIdentity
    {
        public string YgpGameId { get; set; }
        public string YgpGameName { get; set; }
        public string SteamAppId { get; set; }
        public string GogId { get; set; }
        public string EpicId { get; set; }
        public string XboxId { get; set; }
        public string PlayStationId { get; set; }
        public string NintendoId { get; set; }
        public int? ReleaseYear { get; set; }
        public string Platform { get; set; }
    }

    /// <summary>
    /// Manual mapping between Playnite and YGP games.
    /// </summary>
    public class GameMapping
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string PlayniteGameId { get; set; }
        public string PlayniteGameName { get; set; }
        public string YgpGameId { get; set; }
        public string YgpGameName { get; set; }
        public MappingType MappingType { get; set; } = MappingType.Manual;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? LastUsedAtUtc { get; set; }
    }

    /// <summary>
    /// Storage for game mappings using JSON.
    /// </summary>
    public class GameMappingStorage
    {
        private readonly ILogger _logger;
        private readonly string _mappingsFile;
        private List<GameMapping> _mappings;

        public GameMappingStorage(ILogger logger, string dataPath)
        {
            _logger = logger;
            _mappingsFile = System.IO.Path.Combine(dataPath, "game_mappings.json");
            _mappings = new List<GameMapping>();
            LoadMappings();
        }

        private void LoadMappings()
        {
            try
            {
                if (System.IO.File.Exists(_mappingsFile))
                {
                    var json = System.IO.File.ReadAllText(_mappingsFile);
                    _mappings = JsonConvert.DeserializeObject<List<GameMapping>>(json) ?? new List<GameMapping>();
                    _logger.Info($"Loaded {_mappings.Count} game mappings");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to load game mappings: {ex.Message}");
                _mappings = new List<GameMapping>();
            }
        }

        private void SaveMappings()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_mappings, Formatting.Indented);
                System.IO.File.WriteAllText(_mappingsFile, json);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to save game mappings: {ex.Message}");
            }
        }

        public async Task<GameMapping> GetMappingAsync(string playniteGameId)
        {
            await Task.Yield();
            return _mappings.FirstOrDefault(m => m.PlayniteGameId == playniteGameId);
        }

        public async Task SaveMappingAsync(GameMapping mapping)
        {
            var existing = _mappings.FirstOrDefault(m => m.PlayniteGameId == mapping.PlayniteGameId);
            if (existing != null)
            {
                _mappings.Remove(existing);
            }
            _mappings.Add(mapping);
            SaveMappings();
            await Task.CompletedTask;
        }

        public async Task RemoveMappingAsync(string playniteGameId)
        {
            var mapping = _mappings.FirstOrDefault(m => m.PlayniteGameId == playniteGameId);
            if (mapping != null)
            {
                _mappings.Remove(mapping);
                SaveMappings();
            }
            await Task.CompletedTask;
        }

        public async Task<IEnumerable<GameMapping>> GetAllMappingsAsync()
        {
            await Task.Yield();
            return _mappings.ToList();
        }

        public async Task ClearAllMappingsAsync()
        {
            _mappings.Clear();
            SaveMappings();
            await Task.CompletedTask;
        }
    }
}
