using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Playnite.SDK;

namespace YgpPlaytimeSync
{
    /// <summary>
    /// Guarda o último tempo (em minutos) já tratado de cada jogo, para só reenviar quando o tempo realmente subir.
    /// Chaves: "&lt;id do jogo no Playnite&gt;" e "ygp:&lt;id no site&gt;|&lt;plataforma&gt;" (evita enviar a mesma
    /// cópia duplicada do jogo com horas diferentes).
    /// </summary>
    public class SyncedPlaytimeStore
    {
        private readonly ILogger _logger;
        private readonly string _file;
        private readonly object _lock = new object();
        private Dictionary<string, int> _minutes = new Dictionary<string, int>();

        /// <summary>True quando o arquivo ainda não existia (primeira execução desta versão).</summary>
        public bool WasEmpty { get; private set; }

        public SyncedPlaytimeStore(ILogger logger, string dataPath)
        {
            _logger = logger;
            _file = Path.Combine(dataPath, "synced-playtime.json");
            WasEmpty = !File.Exists(_file);

            if (!WasEmpty)
            {
                try
                {
                    var loaded = JsonConvert.DeserializeObject<Dictionary<string, int>>(File.ReadAllText(_file));
                    if (loaded != null) _minutes = loaded;
                }
                catch (Exception ex)
                {
                    _logger.Warn($"[YGP] Não foi possível ler {_file}: {ex.Message}");
                }
            }
        }

        private void SaveUnlocked()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_file));
                File.WriteAllText(_file, JsonConvert.SerializeObject(_minutes));
            }
            catch (Exception ex)
            {
                _logger.Warn($"[YGP] Não foi possível salvar {_file}: {ex.Message}");
            }
        }

        public int GetMinutes(string key)
        {
            lock (_lock)
            {
                int value;
                return _minutes.TryGetValue(key, out value) ? value : 0;
            }
        }

        /// <summary>True se o tempo atual tem pelo menos 1 minuto e é maior que o último já tratado.</summary>
        public bool HasIncreased(string key, int totalSeconds)
        {
            var minutes = totalSeconds / 60;
            return minutes >= 1 && minutes > GetMinutes(key);
        }

        /// <summary>True se o valor já registrado cobre (é igual ou maior que) o tempo informado.</summary>
        public bool Covers(string key, int totalSeconds)
        {
            return GetMinutes(key) >= totalSeconds / 60;
        }

        public void Record(string key, int totalSeconds)
        {
            var minutes = totalSeconds / 60;
            lock (_lock)
            {
                int old;
                if (!_minutes.TryGetValue(key, out old) || minutes > old)
                {
                    _minutes[key] = minutes;
                    SaveUnlocked();
                }
            }
        }

        /// <summary>Na primeira execução, aproveita as sessões já enviadas em versões anteriores.</summary>
        public void SeedFrom(IEnumerable<GameSession> syncedSessions)
        {
            lock (_lock)
            {
                foreach (var s in syncedSessions.Where(x => !string.IsNullOrEmpty(x.PlayniteGameId)))
                {
                    var minutes = s.TotalPlaytimeSeconds / 60;
                    int old;
                    if (minutes > 0 && (!_minutes.TryGetValue(s.PlayniteGameId, out old) || minutes > old))
                    {
                        _minutes[s.PlayniteGameId] = minutes;
                    }
                }
                SaveUnlocked();
            }
        }

        /// <summary>Soma (em minutos) do tempo de todos os jogos já tratados (só chaves de jogos do Playnite).</summary>
        public int TotalMinutes()
        {
            lock (_lock)
            {
                return _minutes.Where(kv => !kv.Key.StartsWith("ygp:", StringComparison.Ordinal)).Sum(kv => kv.Value);
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _minutes.Clear();
                SaveUnlocked();
            }
        }

        public int Count
        {
            get { lock (_lock) { return _minutes.Count; } }
        }
    }
}
