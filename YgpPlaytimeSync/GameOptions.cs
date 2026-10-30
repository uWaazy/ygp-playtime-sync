using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Playnite.SDK;

namespace YgpPlaytimeSync
{
    /// <summary>Datas (de calendário, no fuso local) que serão enviadas ao Your Gamer Profile.</summary>
    public class YgpPlayDates
    {
        /// <summary>Primeiro dia em que o jogo foi jogado ("Início"). Null = não enviar.</summary>
        public DateTime? Start { get; set; }

        /// <summary>Último dia jogado, enviado como "Conclusão" só se o usuário ativou a opção para o jogo.</summary>
        public DateTime? Completed { get; set; }

        /// <summary>True quando o envio foi forçado por uma mudança de opção (mesmo sem aumento de horas).</summary>
        public bool Force { get; set; }
    }

    /// <summary>Opções do plugin escolhidas pelo usuário para UM jogo (menu "YGP: Settings").</summary>
    public class GameOptions
    {
        /// <summary>Null = usa o padrão global (enviar data de início).</summary>
        public bool? SendStartDate { get; set; }

        /// <summary>Envia o último dia jogado como data de "Conclusão".</summary>
        public bool SetCompletedAtLastPlayed { get; set; }

        /// <summary>Reenviar mesmo que as horas não tenham aumentado (a opção mudou).</summary>
        public bool ForceResend { get; set; }

        /// <summary>Último dia jogado (data) para o qual a conclusão automática já foi enviada ao site.</summary>
        public DateTime? AutoCompletedFor { get; set; }
    }

    public class GameOptionsStore
    {
        private readonly ILogger _logger;
        private readonly string _file;
        private readonly object _lock = new object();
        private Dictionary<string, GameOptions> _items = new Dictionary<string, GameOptions>();

        public GameOptionsStore(ILogger logger, string dataPath)
        {
            _logger = logger;
            _file = Path.Combine(dataPath, "ygp-game-options.json");

            try
            {
                if (File.Exists(_file))
                {
                    var loaded = JsonConvert.DeserializeObject<Dictionary<string, GameOptions>>(File.ReadAllText(_file));
                    if (loaded != null) _items = loaded;
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"[YGP] Não foi possível ler {_file}: {ex.Message}");
            }
        }

        public GameOptions Get(string gameId)
        {
            lock (_lock)
            {
                GameOptions o;
                if (!_items.TryGetValue(gameId, out o)) return new GameOptions();
                return new GameOptions { SendStartDate = o.SendStartDate, SetCompletedAtLastPlayed = o.SetCompletedAtLastPlayed, ForceResend = o.ForceResend, AutoCompletedFor = o.AutoCompletedFor };
            }
        }

        public void Update(string gameId, Action<GameOptions> change)
        {
            lock (_lock)
            {
                GameOptions o;
                if (!_items.TryGetValue(gameId, out o))
                {
                    o = new GameOptions();
                    _items[gameId] = o;
                }
                change(o);

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_file));
                    File.WriteAllText(_file, JsonConvert.SerializeObject(_items));
                }
                catch (Exception ex)
                {
                    _logger.Warn($"[YGP] Não foi possível salvar {_file}: {ex.Message}");
                }
            }
        }
    }

    public class PlayDateEntry
    {
        public DateTime? First { get; set; }
        public DateTime? Last { get; set; }
    }

    /// <summary>
    /// Registro próprio do plugin: primeiro e último dia em que cada jogo foi iniciado pelo Playnite
    /// (vale a partir da instalação; o histórico anterior vem do GameActivity, se instalado).
    /// </summary>
    public class PlayDatesStore
    {
        private readonly ILogger _logger;
        private readonly string _file;
        private readonly object _lock = new object();
        private Dictionary<string, PlayDateEntry> _items = new Dictionary<string, PlayDateEntry>();

        public PlayDatesStore(ILogger logger, string dataPath)
        {
            _logger = logger;
            _file = Path.Combine(dataPath, "ygp-play-dates.json");

            try
            {
                if (File.Exists(_file))
                {
                    var loaded = JsonConvert.DeserializeObject<Dictionary<string, PlayDateEntry>>(File.ReadAllText(_file));
                    if (loaded != null) _items = loaded;
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"[YGP] Não foi possível ler {_file}: {ex.Message}");
            }
        }

        private void SaveUnlocked()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_file));
                File.WriteAllText(_file, JsonConvert.SerializeObject(_items));
            }
            catch (Exception ex)
            {
                _logger.Warn($"[YGP] Não foi possível salvar {_file}: {ex.Message}");
            }
        }

        public void RecordSession(string gameId, DateTime utcNow)
        {
            lock (_lock)
            {
                PlayDateEntry e;
                if (!_items.TryGetValue(gameId, out e))
                {
                    e = new PlayDateEntry();
                    _items[gameId] = e;
                }
                if (!e.First.HasValue) e.First = utcNow;
                e.Last = utcNow;
                SaveUnlocked();
            }
        }

        public PlayDateEntry Get(string gameId)
        {
            lock (_lock)
            {
                PlayDateEntry e;
                if (!_items.TryGetValue(gameId, out e)) return null;
                return new PlayDateEntry { First = e.First, Last = e.Last };
            }
        }
    }

    /// <summary>
    /// Lê o histórico de sessões do plugin GameActivity (se instalado) para descobrir o primeiro e o último dia jogado.
    /// Procura o arquivo "&lt;id do jogo&gt;.json" dentro da pasta de dados do GameActivity e coleta todos os campos "DateSession".
    /// </summary>
    public static class GameActivityReader
    {
        private const string GameActivityPluginId = "afbb1a0d-04a1-4d0c-9afa-c6e42ca855b4";

        public static bool TryGetRange(string extensionsDataPath, Guid gameId, out DateTime first, out DateTime last)
        {
            first = DateTime.MaxValue;
            last = DateTime.MinValue;

            try
            {
                if (string.IsNullOrEmpty(extensionsDataPath)) return false;

                var root = Path.Combine(extensionsDataPath, GameActivityPluginId);
                if (!Directory.Exists(root)) return false;

                var dates = new List<DateTime>();
                foreach (var file in Directory.GetFiles(root, gameId.ToString() + ".json", SearchOption.AllDirectories))
                {
                    try { Collect(JToken.Parse(File.ReadAllText(file)), dates); }
                    catch { }
                }

                if (dates.Count == 0) return false;

                first = dates.Min();
                last = dates.Max();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void Collect(JToken token, List<DateTime> dates)
        {
            var obj = token as JObject;
            if (obj != null)
            {
                foreach (var prop in obj.Properties())
                {
                    if (string.Equals(prop.Name, "DateSession", StringComparison.OrdinalIgnoreCase))
                    {
                        DateTime parsed;
                        if (prop.Value.Type == JTokenType.Date)
                        {
                            parsed = (DateTime)prop.Value;
                            dates.Add(parsed.Kind == DateTimeKind.Utc ? parsed.ToLocalTime() : parsed);
                        }
                        else if (prop.Value.Type == JTokenType.String &&
                                 DateTime.TryParse((string)prop.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsed))
                        {
                            dates.Add(parsed);
                        }
                    }
                    else
                    {
                        Collect(prop.Value, dates);
                    }
                }
                return;
            }

            var arr = token as JArray;
            if (arr != null)
            {
                foreach (var item in arr) Collect(item, dates);
            }
        }
    }
}
