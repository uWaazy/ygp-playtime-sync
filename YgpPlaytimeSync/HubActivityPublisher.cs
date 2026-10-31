using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace YgpPlaytimeSync
{
    /// <summary>
    /// OPTIONAL and OFF by default. When the user enables it and fills in THEIR OWN hub URL + secret
    /// (stored only in their local Playnite settings, never in this repository), it tells that hub which
    /// game is being played right now (game name, platform, start time, total hours). No cookies, no
    /// account data, no local paths. Ignored games/apps are never published.
    /// </summary>
    public class HubActivityPublisher : IDisposable
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        private static readonly TimeSpan Heartbeat = TimeSpan.FromMinutes(4); // hub expires "playing" after 10 min

        private readonly PluginSettings _settings;
        private readonly ILogger _logger;
        private readonly Func<Game, bool> _isEligible;
        private Timer _beat;
        private Game _game;
        private DateTime _startedUtc;

        public HubActivityPublisher(PluginSettings settings, ILogger logger, Func<Game, bool> isEligible)
        {
            _settings = settings;
            _logger = logger;
            _isEligible = isEligible;
        }

        private Uri Endpoint()
        {
            if (_settings == null || !_settings.HubPublishEnabled) return null;
            if (string.IsNullOrWhiteSpace(_settings.HubUrl) || string.IsNullOrWhiteSpace(_settings.HubToken)) return null;
            Uri u;
            if (!Uri.TryCreate(_settings.HubUrl.Trim(), UriKind.Absolute, out u) || u.Scheme != Uri.UriSchemeHttps) return null; // HTTPS only
            return u.AbsolutePath.Trim('/').Length == 0 ? new Uri(u, "/api/activity") : u;
        }

        public void GameStarted(Game game)
        {
            try
            {
                StopBeat();
                if (game == null || Endpoint() == null || !_isEligible(game)) return;
                _game = game;
                _startedUtc = DateTime.UtcNow;
                Send("playing", game);
                _beat = new Timer(_ => { var g = _game; if (g != null) Send("playing", g); }, null, Heartbeat, Heartbeat);
            }
            catch (Exception ex) { _logger.Warn("[YGP] Hub: " + ex.Message); }
        }

        public void GameStopped(Game game)
        {
            try
            {
                StopBeat();
                if (game == null || Endpoint() == null || !_isEligible(game)) return;
                Send("idle", game);
            }
            catch (Exception ex) { _logger.Warn("[YGP] Hub: " + ex.Message); }
        }

        private void StopBeat()
        {
            try { _beat?.Dispose(); } catch { }
            _beat = null;
            _game = null;
        }

        private void Send(string state, Game g)
        {
            var endpoint = Endpoint();
            if (endpoint == null) return;
            var token = _settings.HubToken.Trim();
            var json = JsonConvert.SerializeObject(new
            {
                state = state,
                name = g.Name,
                source = g.Source != null ? g.Source.Name : null,
                sourceId = g.GameId,
                startedAt = _startedUtc.ToString("o"),
                totalHours = Math.Round(g.Playtime / 3600.0, 1)
            });

            Task.Run(async () =>
            {
                try
                {
                    var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
                    req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
                    req.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    var res = await Http.SendAsync(req).ConfigureAwait(false);
                    if (!res.IsSuccessStatusCode) _logger.Warn("[YGP] Hub respondeu " + (int)res.StatusCode); // never log the token
                }
                catch (Exception ex) { _logger.Warn("[YGP] Hub indisponível: " + ex.Message); }
            });
        }

        public void Dispose() { StopBeat(); }
    }
}
