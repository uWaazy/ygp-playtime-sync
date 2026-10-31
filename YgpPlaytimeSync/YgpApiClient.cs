using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;

namespace YgpPlaytimeSync
{
    /// <summary>
    /// Cliente do site Your Gamer Profile (yourgamerprofile.com).
    /// Todas as chamadas passam por um navegador offscreen do Playnite (mesma sessão/cookies do login),
    /// o que mantém a compatibilidade com o Cloudflare do site.
    /// O navegador é criado uma única vez por lote de sincronização e descartado ao final (EndBatchAsync),
    /// em vez de ser recriado e recarregar o site a cada jogo/teste como antes.
    /// </summary>
    public class YgpApiClient
    {
        private const string BaseUrl = "https://yourgamerprofile.com";

        private readonly IPlayniteAPI _playniteApi;
        private readonly PluginSettings _settings;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _browserLock = new SemaphoreSlim(1, 1);

        private IWebView _view;
        private DateTime _viewLoadedAtUtc = DateTime.MinValue;
        private bool _sessionVerified;

        /// <summary>Chamado quando o cliente altera dados persistentes (login expirado, nome de usuário, etc.).</summary>
        public Action SettingsChanged { get; set; }

        /// <summary>Mensagem detalhada do último teste de conexão (para exibir na tela de configurações).</summary>
        public string LastTestMessage { get; private set; } = "";

        /// <summary>Pasta onde o arquivo de diagnóstico (ygp-diagnostico.txt) é gravado.</summary>
        public string DiagnosticsDirectory { get; set; }

        public string DiagnosticsFilePath
        {
            get
            {
                return string.IsNullOrEmpty(DiagnosticsDirectory)
                    ? null
                    : System.IO.Path.Combine(DiagnosticsDirectory, "ygp-diagnostico.txt");
            }
        }

        private Dictionary<string, string> _discovered = new Dictionary<string, string>();
        private bool _discoveryDone;

        public YgpApiClient(IPlayniteAPI playniteApi, PluginSettings settings, ILogger logger)
        {
            _playniteApi = playniteApi;
            _settings = settings;
            _logger = logger;
        }

        #region Utilitários de thread/UI

        private void RunOnUIThread(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(action);
            }
            else
            {
                action();
            }
        }

        private void NotifySettingsChanged()
        {
            try { SettingsChanged?.Invoke(); }
            catch (Exception ex) { _logger.Debug($"[YGP] Falha ao persistir configurações: {ex.Message}"); }
        }

        #endregion

        #region Cookies / conta

        public bool IsAuthenticated()
        {
            return _settings.IsConnected && !string.IsNullOrWhiteSpace(_settings.AuthCookiesJson);
        }

        private bool HasStoredCookies()
        {
            return !string.IsNullOrWhiteSpace(_settings.AuthCookiesJson);
        }

        public List<SerializableCookie> GetStoredCookies()
        {
            if (string.IsNullOrWhiteSpace(_settings.AuthCookiesJson))
                return new List<SerializableCookie>();

            try
            {
                return JsonConvert.DeserializeObject<List<SerializableCookie>>(_settings.AuthCookiesJson)
                       ?? new List<SerializableCookie>();
            }
            catch (Exception ex)
            {
                _logger.Warn($"[YGP] Erro ao deserializar cookies: {ex.Message}");
                return new List<SerializableCookie>();
            }
        }

        private static bool IsSiteCookie(HttpCookie c)
        {
            return c != null && c.Domain != null && c.Domain.IndexOf("yourgamerprofile", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasSessionCookie(IEnumerable<HttpCookie> cookies)
        {
            if (cookies == null) return false;
            return cookies.Any(c => IsSiteCookie(c)
                                    && c.Name != null
                                    && c.Name.IndexOf("session", StringComparison.OrdinalIgnoreCase) >= 0
                                    && !string.IsNullOrEmpty(c.Value));
        }

        private static List<HttpCookie> SafeGetCookies(IWebView view)
        {
            try { return view.GetCookies() ?? new List<HttpCookie>(); }
            catch { return new List<HttpCookie>(); }
        }

        public void SaveCookies(List<HttpCookie> cookies)
        {
            if (cookies == null || cookies.Count == 0) return;

            var serializable = cookies
                .Where(IsSiteCookie)
                .Select(SerializableCookie.FromHttpCookie)
                .ToList();

            if (serializable.Count == 0)
            {
                serializable = cookies.Select(SerializableCookie.FromHttpCookie).ToList();
            }

            _settings.AuthCookiesJson = JsonConvert.SerializeObject(serializable);
            _settings.IsConnected = serializable.Count > 0;

            // Tenta extrair os dados da conta do cookie do better-auth (opcional; o teste de conexão também preenche).
            var sessionDataCookie = cookies.FirstOrDefault(c => c.Name != null && c.Name.EndsWith("better-auth.session_data", StringComparison.OrdinalIgnoreCase));
            if (sessionDataCookie != null && !string.IsNullOrWhiteSpace(sessionDataCookie.Value))
            {
                try
                {
                    string raw = WebUtility.UrlDecode(sessionDataCookie.Value);
                    byte[] bytes = Convert.FromBase64String(raw);
                    string json = Encoding.UTF8.GetString(bytes);
                    var parsed = JObject.Parse(json);
                    var user = parsed["user"] as JObject;
                    if (user != null)
                    {
                        FillProfileFromUser(user);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug($"[YGP] Cookie de sessão não decodificável (normal): {ex.Message}");
                }
            }
        }

        private void FillProfileFromUser(JObject user)
        {
            string name = JsonString(user, "username") ?? JsonString(user, "displayUsername") ?? JsonString(user, "name");
            string id = JsonString(user, "id");
            string image = JsonString(user, "image");

            if (!string.IsNullOrWhiteSpace(name)) _settings.Username = name;
            if (!string.IsNullOrWhiteSpace(id)) _settings.UserId = id;
            if (!string.IsNullOrWhiteSpace(image)) _settings.AvatarUrl = image;
        }

        private static string JsonString(JObject obj, string key)
        {
            JToken tok;
            if (obj == null || !obj.TryGetValue(key, out tok) || tok == null) return null;
            if (tok.Type == JTokenType.String || tok.Type == JTokenType.Integer || tok.Type == JTokenType.Guid)
            {
                var s = (string)tok;
                return string.IsNullOrWhiteSpace(s) ? null : s;
            }
            return null;
        }

        /// <summary>Recoloca no navegador os cookies salvos, caso o navegador não tenha mais a sessão.</summary>
        private void RestoreCookies(IWebView view)
        {
            if (HasSessionCookie(SafeGetCookies(view))) return;

            var stored = GetStoredCookies();
            int applied = 0;
            foreach (var c in stored)
            {
                try
                {
                    if (c.Expires.HasValue && c.Expires.Value.ToUniversalTime() < DateTime.UtcNow) continue;
                    view.SetCookies(BaseUrl, c.ToHttpCookie());
                    applied++;
                }
                catch (Exception ex)
                {
                    _logger.Debug($"[YGP] Erro ao restaurar cookie {c.Name}: {ex.Message}");
                }
            }
            _logger.Info($"[YGP] Sessão ausente no navegador interno; {applied} cookie(s) restaurado(s).");
        }

        #endregion

        #region Mapeamento de plataforma e slugs

        /// <summary>
        /// Mapeia o nome da plataforma do Playnite (incluindo emuladores) para a lista exata aceita no Your Gamer Profile.
        /// </summary>
        public static string MapPlatform(string playnitePlatform)
        {
            if (string.IsNullOrWhiteSpace(playnitePlatform))
                return "PC (Microsoft Windows)";

            var p = playnitePlatform.Trim().ToLowerInvariant();

            // PC / Windows
            if (p.Contains("windows") || p.Contains("pc") || p.Contains("dos"))
                return "PC (Microsoft Windows)";

            // Xbox
            if (p.Contains("series"))
                return "Xbox Series X|S";
            if (p.Contains("xbox one"))
                return "Xbox One";
            if (p.Contains("xbox 360") || p.Contains("xbox360") || p.Contains("xbox"))
                return "Xbox Series X|S";

            // PlayStation
            if (p.Contains("ps5") || p.Contains("playstation 5"))
                return "PlayStation 5";
            if (p.Contains("ps4") || p.Contains("playstation 4"))
                return "PlayStation 4";
            if (p.Contains("ps3") || p.Contains("ps2") || p.Contains("ps1") || p.Contains("playstation") || p.Contains("psp") || p.Contains("vita"))
                return "PlayStation 4";

            // Nintendo
            if (p.Contains("switch 2"))
                return "Nintendo Switch 2";
            if (p.Contains("switch") || p.Contains("nintendo") || p.Contains("wii") || p.Contains("gamecube") || p.Contains("snes") || p.Contains("nes") || p.Contains("game boy") || p.Contains("gba") || p.Contains("ds") || p.Contains("3ds"))
                return "Nintendo Switch";

            // Apple / Mobile
            if (p.Contains("mac") || p.Contains("apple") || p.Contains("macos") || p.Contains("osx"))
                return "Mac";
            if (p.Contains("android"))
                return "Android";
            if (p.Contains("ios") || p.Contains("iphone") || p.Contains("ipad"))
                return "iOS";

            return "PC (Microsoft Windows)";
        }

        /// <summary>
        /// Mapeia nomes de emuladores conhecidos para as plataformas aceitas pelo Your Gamer Profile.
        /// Jogos rodados via emulador recebem a plataforma de console correspondente.
        /// </summary>
        public static string MapPlatformWithEmulator(string emulator)
        {
            if (string.IsNullOrWhiteSpace(emulator))
                return "PC (Microsoft Windows)";

            var e = emulator.Trim().ToLowerInvariant();

            // Emuladores de PlayStation (PS1, PS2, PS3, PSP, Vita)
            if (e.Contains("rpcs3") || e.Contains("ppsspp") || e.Contains("duckstation") || e.Contains("epsxe") ||
                e.Contains("dualshock") || e.Contains("ds4") || e.Contains("retropie") || e.Contains("batocera") ||
                (e.Contains("retroarch") && (e.Contains("ps1") || e.Contains("ps2") || e.Contains("ps3") || e.Contains("psp") || e.Contains("psx") || e.Contains("vita"))))
                return "PlayStation 4";

            // Emuladores de Xbox (Original, 360, One, Series)
            if (e.Contains("xenia") || e.Contains("cxbx") || e.Contains("xboxemuhd") || e.Contains("xbian") || e.Contains("emuhawk"))
                return "Xbox Series X|S";

            // Emuladores de Nintendo (Switch, Wii, Wii U, GameCube, 3DS, DS, GBA, SNES, NES, GB)
            if (e.Contains("cemu") || e.Contains("dolphin") || e.Contains("yuzu") || e.Contains("ryujinx") ||
                e.Contains("suyu") || e.Contains("citra") || e.Contains("melonstone") || e.Contains("desmume") ||
                e.Contains("mgba") || e.Contains("snes9x") || e.Contains("higan") || e.Contains("mupen64") ||
                e.Contains("visualboyadvance") || e.Contains("bsnes") || e.Contains("fceumm") ||
                (e.Contains("retroarch") && (e.Contains("nds") || e.Contains("snes") || e.Contains("gba") ||
                    e.Contains("nes") || e.Contains("gb") || e.Contains("gbc") || e.Contains("ngc") ||
                    e.Contains("gc") || e.Contains("gamecube") || e.Contains("wii") || e.Contains("switch") || e.Contains("n64"))))
                return "Nintendo Switch";

            // Emuladores de PC/MS-DOS
            if (e.Contains("dosbox") || e.Contains("scummvm") || e.Contains("vba"))
                return "PC (Microsoft Windows)";

            return "PC (Microsoft Windows)";
        }

        /// <summary>
        /// Gera variações inteligentes de slug para localizar a página correta do jogo no Your Gamer Profile.
        /// Ex: "Space Engineers 2" -> ["space-engineers-2", "spaceengineers2", "space-engineers-ii", "space_engineers_2"]
        /// Ex: "SpaceEngine" -> ["spaceengine", "space-engine"]
        /// Inclui normalização de acentos (ex: "São" -> "sa") e variante em underline.
        /// </summary>
        public static List<string> GetCandidateSlugs(string gameName)
        {
            var slugs = new List<string>();
            if (string.IsNullOrWhiteSpace(gameName)) return slugs;

            string raw = gameName.Trim().ToLowerInvariant();

            // Normalização de caracteres acentuados para aumentar as chances de encontrar o slug (YGP usa ASCII)
            var normRaw = raw
                .Replace("á", "a").Replace("à", "a").Replace("ã", "a").Replace("â", "a").Replace("ä", "a")
                .Replace("é", "e").Replace("è", "e").Replace("ê", "e").Replace("ë", "e")
                .Replace("í", "i").Replace("ì", "i").Replace("î", "i").Replace("ï", "i")
                .Replace("ó", "o").Replace("ò", "o").Replace("õ", "o").Replace("ô", "o").Replace("ö", "o")
                .Replace("ú", "u").Replace("ù", "u").Replace("û", "u").Replace("ü", "u")
                .Replace("ç", "c");

            // 1. Slug padrão com traços (ex: "space-engineers-2")
            string dashSlug = Regex.Replace(normRaw, @"[^a-z0-9]+", "-").Trim('-');
            if (!string.IsNullOrEmpty(dashSlug)) slugs.Add(dashSlug);

            // 2. Slug compacto sem traços (ex: "spaceengine")
            string compactSlug = Regex.Replace(normRaw, @"[^a-z0-9]", "");
            if (!string.IsNullOrEmpty(compactSlug) && !slugs.Contains(compactSlug)) slugs.Add(compactSlug);

            // 3. Slug com underlines (ex: "space_engineers_2")
            string underscoreSlug = Regex.Replace(normRaw, @"[^\w]+", "_").Trim('_');
            if (!string.IsNullOrEmpty(underscoreSlug) && !slugs.Contains(underscoreSlug)) slugs.Add(underscoreSlug);

            // 4. Conversão de números para algarismos romanos e vice-versa (ex: 2 <-> ii)
            if (Regex.IsMatch(normRaw, @"\b2\b"))
            {
                string roman2 = Regex.Replace(normRaw, @"\b2\b", "ii");
                string s = Regex.Replace(roman2, @"[^a-z0-9]+", "-").Trim('-');
                if (!slugs.Contains(s)) slugs.Add(s);
            }
            else if (Regex.IsMatch(normRaw, @"\bii\b"))
            {
                string arabic2 = Regex.Replace(normRaw, @"\bii\b", "2");
                string s = Regex.Replace(arabic2, @"[^a-z0-9]+", "-").Trim('-');
                if (!slugs.Contains(s)) slugs.Add(s);
            }

            // 5. Remover subtítulos ou anos entre parênteses (ex: "Call of Duty (2003)" -> "call-of-duty")
            if (normRaw.Contains("(") || normRaw.Contains(":"))
            {
                string withoutParens = Regex.Replace(normRaw, @"\(.*?\)", "").Trim();
                string cleanBase = Regex.Replace(withoutParens, @"[^a-z0-9]+", "-").Trim('-');
                if (!string.IsNullOrEmpty(cleanBase) && !slugs.Contains(cleanBase)) slugs.Add(cleanBase);

                string beforeColon = normRaw.Split(':')[0].Trim();
                string cleanColon = Regex.Replace(beforeColon, @"[^a-z0-9]+", "-").Trim('-');
                if (!string.IsNullOrEmpty(cleanColon) && !slugs.Contains(cleanColon)) slugs.Add(cleanColon);
            }

            return slugs;
        }

        #endregion

        #region Login

        /// <summary>
        /// Abre a janela de login do Playnite (WebView). OpenDialog() é BLOQUEANTE: o evento LoadingChanged
        /// precisa ser assinado ANTES de abrir, e a janela é fechada automaticamente quando a sessão aparece.
        /// Se o usuário fechar a janela manualmente, os cookies são conferidos mesmo assim.
        /// </summary>
        public Task<bool> LoginViaWebViewAsync()
        {
            var tcs = new TaskCompletionSource<bool>();

            RunOnUIThread(() =>
            {
                try
                {
                    tcs.TrySetResult(RunLoginDialog());
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"[YGP] Erro durante login no WebView: {ex.Message}");
                    tcs.TrySetResult(false);
                }
            });

            return tcs.Task;
        }

        private bool RunLoginDialog()
        {
            _logger.Info("[YGP] Abrindo janela de login do Your Gamer Profile...");

            var webViewSettings = new WebViewSettings
            {
                WindowWidth = 950,
                WindowHeight = 720,
                JavaScriptEnabled = true
            };

            using (var view = _playniteApi.WebViews.CreateView(webViewSettings))
            {
                // IMPORTANTE: nada de ler cookies/endereço dentro do evento LoadingChanged do navegador.
                // Esse evento roda na thread interna do CEF e chamar a API do Playnite dali pode travar o app inteiro
                // (principalmente durante os redirecionamentos de Google/Discord). Um temporizador na thread da
                // interface faz a verificação com segurança.
                var closing = false;
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2000) };
                timer.Tick += (s, e) =>
                {
                    if (closing) return;

                    try
                    {
                        var address = view.GetCurrentAddress() ?? "";
                        if (!IsSiteAddress(address) || IsAuthPage(address)) return;

                        if (HasSessionCookie(SafeGetCookies(view)))
                        {
                            closing = true;
                            timer.Stop();
                            _logger.Info("[YGP] Sessão detectada; fechando janela de login.");
                            view.Close();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug($"[YGP] Erro ao verificar login: {ex.Message}");
                    }
                };

                view.Navigate(BaseUrl + "/login");
                timer.Start();
                view.OpenDialog();
                timer.Stop();

                var cookies = SafeGetCookies(view);
                if (!HasSessionCookie(cookies))
                {
                    _logger.Warn("[YGP] Janela de login fechada sem sessão ativa.");
                    return false;
                }

                SaveCookies(cookies);
                _settings.IsConnected = true;
                _settings.LastAuthTestUtc = DateTime.UtcNow;
                _sessionVerified = false;
                _logger.Info($"[YGP] Login concluído. Cookies salvos: {cookies.Count(IsSiteCookie)}.");
                NotifySettingsChanged();
                return true;
            }
        }

        private static SerializableCookie MakeCookie(string name, string value)
        {
            return new SerializableCookie
            {
                Name = name,
                Value = value,
                Domain = "yourgamerprofile.com",
                Path = "/",
                HttpOnly = true,
                Secure = true
            };
        }

        /// <summary>
        /// Alternativa ao login embutido (o Google costuma bloquear navegadores embutidos): o usuário faz login no
        /// navegador normal e cola o valor do cookie de sessão (ou vários cookies no formato nome=valor; nome2=valor2).
        /// </summary>
        public bool ImportCookieText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            text = text.Trim().Trim('"');
            if (text.StartsWith("Cookie:", StringComparison.OrdinalIgnoreCase)) text = text.Substring(7).Trim();

            var cookies = new List<SerializableCookie>();
            bool pairs = text.Contains(";") || text.StartsWith("__Secure-", StringComparison.Ordinal) ||
                         text.StartsWith("__Host-", StringComparison.Ordinal) || text.StartsWith("better-auth", StringComparison.Ordinal);

            if (pairs)
            {
                foreach (var part in text.Split(';'))
                {
                    var kv = part.Trim();
                    var idx = kv.IndexOf('=');
                    if (idx <= 0) continue;
                    cookies.Add(MakeCookie(kv.Substring(0, idx).Trim(), kv.Substring(idx + 1).Trim()));
                }
            }
            else
            {
                cookies.Add(MakeCookie("__Secure-better-auth.session_token", text));
            }

            if (!cookies.Any(c => c.Name.IndexOf("session", StringComparison.OrdinalIgnoreCase) >= 0 && !string.IsNullOrEmpty(c.Value)))
            {
                return false;
            }

            _settings.AuthCookiesJson = JsonConvert.SerializeObject(cookies);
            _settings.IsConnected = true;
            _settings.LastAuthTestUtc = DateTime.UtcNow;
            _sessionVerified = false;
            _logger.Info($"[YGP] Cookie(s) de sessão importado(s) manualmente: {cookies.Count}.");
            NotifySettingsChanged();
            return true;
        }

        private static bool IsSiteAddress(string address)
        {
            return address.IndexOf("yourgamerprofile.com", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsAuthPage(string address)
        {
            var a = address.ToLowerInvariant();
            return a.Contains("/login") || a.Contains("/signin") || a.Contains("/sign-in") ||
                   a.Contains("/signup") || a.Contains("/sign-up") || a.Contains("/register") || a.Contains("/auth/");
        }

        #endregion

        #region Navegador offscreen

        private static async Task<bool> NavigateAsync(IWebView webView, string url, int timeoutMs)
        {
            var tcs = new TaskCompletionSource<bool>();
            EventHandler<WebViewLoadingChangedEventArgs> handler = (s, e) =>
            {
                if (!e.IsLoading) tcs.TrySetResult(true);
            };

            webView.LoadingChanged += handler;
            try
            {
                webView.Navigate(url);
                var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
                return completed == tcs.Task;
            }
            finally
            {
                try { webView.LoadingChanged -= handler; } catch { }
            }
        }

        private void DisposeViewInternal()
        {
            var v = _view;
            _view = null;
            _viewLoadedAtUtc = DateTime.MinValue;
            _sessionVerified = false;
            if (v == null) return;

            RunOnUIThread(() =>
            {
                try { v.Dispose(); }
                catch (Exception ex) { _logger.Debug($"[YGP] Erro ao descartar navegador: {ex.Message}"); }
            });
        }

        /// <summary>Descarta o navegador interno ao final de um lote de sincronização.</summary>
        public async Task EndBatchAsync()
        {
            await _browserLock.WaitAsync();
            try { DisposeViewInternal(); }
            finally { _browserLock.Release(); }
        }

        /// <summary>
        /// Garante um navegador offscreen com a sessão aplicada e o site carregado (uma vez por lote).
        /// Deve ser chamado com _browserLock adquirido. Retorna null se estiver tudo certo, ou a mensagem de erro.
        /// </summary>
        private async Task<string> EnsureBrowserAsync(bool forceReload)
        {
            if (_view == null)
            {
                RunOnUIThread(() => { _view = _playniteApi.WebViews.CreateOffscreenView(); });
                if (_view == null) return L.T("api.noBrowser");
                _viewLoadedAtUtc = DateTime.MinValue;
            }

            RestoreCookies(_view);

            if (forceReload || (DateTime.UtcNow - _viewLoadedAtUtc).TotalMinutes > 15)
            {
                var loaded = await NavigateAsync(_view, BaseUrl + "/", 25000);
                var address = "";
                try { address = _view.GetCurrentAddress() ?? ""; } catch { }

                if (!loaded || !IsSiteAddress(address))
                {
                    DisposeViewInternal();
                    return L.T("api.timeout");
                }

                if (await WaitCloudflareAsync())
                {
                    DisposeViewInternal();
                    return L.T("api.cloudflare");
                }

                _viewLoadedAtUtc = DateTime.UtcNow;
                _sessionVerified = false;
            }

            return null;
        }

        /// <summary>Retorna true se a página continuar presa na verificação do Cloudflare.</summary>
        private async Task<bool> WaitCloudflareAsync()
        {
            for (int i = 0; i < 20; i++)
            {
                string title = "";
                try
                {
                    dynamic res = await _view.EvaluateScriptAsync("document.title");
                    title = res?.Result as string ?? "";
                }
                catch { }

                var t = title.ToLowerInvariant();
                if (!(t.Contains("just a moment") || t.Contains("checking your browser") || t.Contains("attention required")))
                    return false;

                await Task.Delay(1000);
            }
            return true;
        }

        /// <summary>
        /// Executa um corpo de função JavaScript assíncrona (com "return") na página atual e devolve o resultado.
        /// Usa polling de uma variável global para NÃO depender de o CefSharp resolver Promises.
        /// </summary>
        private async Task<JObject> RunJsAsync(string functionBody, int timeoutMs)
        {
            var view = _view;
            if (view == null) return null;

            var key = "__ygp" + Guid.NewGuid().ToString("N");
            var start = "(function(){window." + key + "=null;(async function(){try{var __r=await (async function(){"
                        + functionBody
                        + "})();window." + key + "=JSON.stringify({ok:true,data:__r});}catch(e){window." + key
                        + "=JSON.stringify({ok:false,error:String(e)});}})();return 'started';})();";

            await view.EvaluateScriptAsync(start);

            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                await Task.Delay(200);
                dynamic res = await view.EvaluateScriptAsync("window." + key);
                string s = res?.Result as string;
                if (!string.IsNullOrEmpty(s))
                {
                    try { return JObject.Parse(s); }
                    catch { return null; }
                }
            }

            return null;
        }

        #endregion

        #region tRPC

        private class TrpcResult
        {
            public int Status;
            public string Raw = "";
            public bool Ok;
            public JToken Data;
            public string Code;
            public string Message;
            public bool IsAuth;
            public bool IsBlocked;
            public bool ProcedureMissing;
        }

        private async Task<TrpcResult> TrpcAsync(string procedure, JToken input, bool mutation)
        {
            var result = await TrpcOnceAsync(procedure, input, mutation);
            if (result.Status == 429)
            {
                _logger.Warn($"[YGP] Site limitou as requisições (429) em {procedure}; aguardando 6s para tentar de novo.");
                await Task.Delay(6000);
                result = await TrpcOnceAsync(procedure, input, mutation);
            }
            return result;
        }

        private async Task<TrpcResult> TrpcOnceAsync(string procedure, JToken input, bool mutation)
        {
            var result = new TrpcResult();
            var procJson = JsonConvert.SerializeObject(procedure);
            var inputJson = input == null ? "null" : input.ToString(Formatting.None);

            string body;
            if (mutation)
            {
                body = "var r=await fetch('/trpc/'+" + procJson + "+'?batch=1',{method:'POST',credentials:'include',"
                     + "headers:{'Content-Type':'application/json'},body:JSON.stringify({'0':" + inputJson + "})});"
                     + "var t=await r.text();return {status:r.status,text:t.substring(0,6000)};";
            }
            else
            {
                body = "var u='/trpc/'+" + procJson + "+'?batch=1&input='+encodeURIComponent(JSON.stringify({'0':" + inputJson + "}));"
                     + "var r=await fetch(u,{credentials:'include'});"
                     + "var t=await r.text();return {status:r.status,text:t.substring(0,300000)};";
            }

            var wrapper = await RunJsAsync(body, 30000);
            if (wrapper == null)
            {
                result.Message = L.T("api.respTimeout");
                return result;
            }

            if ((bool?)wrapper["ok"] != true)
            {
                result.Message = L.T("api.browserError", (string)wrapper["error"]);
                return result;
            }

            var data = wrapper["data"];
            result.Status = (int?)data["status"] ?? 0;
            result.Raw = (string)data["text"] ?? "";

            var lower = result.Raw.ToLowerInvariant();
            if (lower.Contains("just a moment") || lower.Contains("cf-chl") || (lower.Contains("cloudflare") && lower.Contains("<html")))
            {
                result.IsBlocked = true;
                result.Message = L.T("api.cloudflareBlocked");
                return result;
            }

            JToken parsed;
            try { parsed = JToken.Parse(result.Raw); }
            catch
            {
                result.Message = L.T("api.unexpectedResp", result.Status, Trim(result.Raw, 200));
                result.IsAuth = result.Status == 401;
                return result;
            }

            JToken first = parsed is JArray && ((JArray)parsed).Count > 0 ? ((JArray)parsed)[0] : parsed;

            try
            {
                var error = first["error"];
                if (error != null && error.Type != JTokenType.Null)
                {
                    var inner = error["json"] ?? error;
                    result.Message = (string)inner["message"] ?? Trim(error.ToString(Formatting.None), 300);
                    result.Code = (string)(inner["data"] != null ? inner["data"]["code"] : null);
                    result.IsAuth = result.Status == 401 || result.Code == "UNAUTHORIZED" || result.Code == "FORBIDDEN";
                    result.ProcedureMissing = result.Code == "NOT_FOUND" &&
                                              (result.Message ?? "").IndexOf("No procedure", StringComparison.OrdinalIgnoreCase) >= 0;
                    return result;
                }

                var resultNode = first["result"];
                JToken dataNode = resultNode != null ? resultNode["data"] : null;
                var dataObj = dataNode as JObject;
                if (dataObj != null && dataObj["json"] != null && dataObj.Count <= 2)
                {
                    dataNode = dataObj["json"];
                }

                result.Data = dataNode;
                result.Ok = result.Status >= 200 && result.Status < 300;
                if (!result.Ok)
                {
                    result.IsAuth = result.Status == 401;
                    result.Message = $"HTTP {result.Status}: {Trim(result.Raw, 200)}";
                }
            }
            catch (Exception ex)
            {
                result.Message = L.T("api.parseFail", ex.Message);
            }

            return result;
        }

        private static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }

        #endregion

        #region Diagnóstico e descoberta de rotas

        private void AppendDiagnostic(string text)
        {
            try
            {
                var path = DiagnosticsFilePath;
                if (path == null) return;

                System.IO.Directory.CreateDirectory(DiagnosticsDirectory);
                var info = new System.IO.FileInfo(path);
                if (info.Exists && info.Length > 300000) info.Delete();
                System.IO.File.AppendAllText(path, "[" + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss") + "] " + text + Environment.NewLine);
            }
            catch { }
        }

        /// <summary>
        /// Lê os scripts públicos do site (os mesmos que o navegador baixa) e extrai os nomes dos procedimentos tRPC
        /// que o próprio site usa (ex.: game.search, unverified.addGame). Não envia nada, apenas lê.
        /// </summary>
        private const string DiscoveryScript = @"
var urls=[];
document.querySelectorAll('script[src]').forEach(function(s){urls.push(s.src);});
document.querySelectorAll('link[rel=modulepreload]').forEach(function(l){urls.push(l.href);});
var queue=[];
urls.forEach(function(u){if(u.indexOf(location.origin)===0&&queue.indexOf(u)<0)queue.push(u);});
var seen={},found={},apis={},n=0;
var re=/\.([A-Za-z0-9_]{2,40})\.([A-Za-z0-9_]{2,40})(?:\.([A-Za-z0-9_]{2,40}))?\.(useQuery|useSuspenseQuery|useInfiniteQuery|queryOptions|infiniteQueryOptions|useMutation|mutationOptions|query|mutate|mutateAsync)\b/g;
var chunkRe=/[\x22\x27\x60]([A-Za-z0-9_\-.\/]+\.js)[\x22\x27\x60]/g;
var apiRe=/[\x22\x27\x60](\/(?:api|trpc)\/[A-Za-z0-9_\-.\/]*)[\x22\x27\x60]/g;
while(queue.length&&n<160){
  var batch=queue.splice(0,8).filter(function(u){ if(seen[u])return false; seen[u]=1; return true; });
  n+=batch.length;
  var texts=await Promise.all(batch.map(function(u){
    return fetch(u,{credentials:'include'}).then(function(r){return r.text();}).catch(function(){return '';});
  }));
  for(var bi=0;bi<batch.length;bi++){
    var u=batch[bi], t=texts[bi], m, c, a;
    re.lastIndex=0;
    while((m=re.exec(t))!==null){
      var p=m[3]?(m[1]+'.'+m[2]+'.'+m[3]):(m[1]+'.'+m[2]);
      found[p]=/mutat/i.test(m[4])?'mutation':'query';
    }
    apiRe.lastIndex=0;
    while((a=apiRe.exec(t))!==null){ if(a[1].indexOf('/api/auth')!==0) apis[a[1]]=1; }
    chunkRe.lastIndex=0;
    while((c=chunkRe.exec(t))!==null){
      var ref=c[1], cu=null;
      try{
        if(ref.indexOf('/')===0) cu=location.origin+ref;
        else if(ref.indexOf('static/')===0) cu=location.origin+'/_next/'+ref;
        else cu=new URL(ref,u).href;
      }catch(e){}
      if(cu&&cu.indexOf(location.origin)===0&&!seen[cu]&&queue.indexOf(cu)<0&&queue.length<500) queue.push(cu);
    }
  }
}
return {scripts:n,found:found,apis:apis};";

        // Teste sem efeito colateral: GET em cada nome. Rota inexistente -> "No procedure found";
        // consulta existente -> 200/400; mutação existente -> 405 (tRPC recusa GET em mutação).
        private const string ProbeScriptTemplate = @"
var names=__NAMES__;
var out={};
async function one(n){
  try{
    var u='/trpc/'+n+'?batch=1&input='+encodeURIComponent(JSON.stringify({'0':{}}));
    var r=await fetch(u,{credentials:'include'});
    var t=await r.text();
    if(r.status!==429 && /[\x22](result|error)[\x22]/.test(t) && !/No procedure found/i.test(t)) out[n]={s:r.status,t:t.substring(0,300)};
  }catch(e){}
}
for(var k=0;k<names.length;k+=10){
  await Promise.all(names.slice(k,k+10).map(one));
  await new Promise(function(res){setTimeout(res,120);});
}
return out;";

        private async Task ProbeRoutesAsync()
        {
            var routers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "unverified", "game", "games", "library", "userGame", "userGames", "entry", "gameEntry", "search", "catalog",
                "platform", "playtime", "tracker", "import", "userLibrary", "gameLibrary", "igdb", "steam"
            };
            foreach (var key in _discovered.Keys)
            {
                var seg = key.Split('.');
                if (seg.Length >= 2) routers.Add(seg[seg.Length - 2]);
            }
            routers.Remove("trpc");

            var procs = new[]
            {
                "search", "searchGames", "searchGame", "searchCatalog", "searchByName", "searchByTitle", "searchTitles", "find", "findGames",
                "findGame", "lookup", "autocomplete", "suggest", "suggestions", "getGames", "list", "all", "query", "global", "games",
                "updateGame", "editGame", "update", "edit", "updatePlaytime", "setPlaytime", "savePlaytime", "updateEntry", "editEntry",
                "updateUserGame", "editUserGame", "upsertGame", "upsert", "patchGame", "modifyGame", "changePlaytime", "updateGamePlaytime",
                "editPlaytime", "logPlaytime", "updateProgress", "saveGame", "setGame", "addPlaytime", "updateLibraryGame", "editLibraryGame",
                "updateStatus", "addGame", "addUserGame", "addToLibrary"
            };

            var names = new List<string>();
            foreach (var r in routers)
            {
                foreach (var p in procs) names.Add(r + "." + p);
            }
            names = names.Where(n => !_discovered.ContainsKey(n)).Distinct().Take(1000).ToList();
            if (names.Count == 0) return;

            _logger.Info($"[YGP] Testando {names.Count} nomes de rota possíveis no site (sem alterar nada)...");
            var script = ProbeScriptTemplate.Replace("__NAMES__", JsonConvert.SerializeObject(names));
            var wrapper = await RunJsAsync(script, 240000);

            if (wrapper == null || (bool?)wrapper["ok"] != true)
            {
                AppendDiagnostic("SONDAGEM falhou: " + (wrapper == null ? "tempo esgotado" : (string)wrapper["error"]));
                return;
            }

            var results = wrapper["data"] as JObject;
            if (results == null) return;

            int count = 0;
            foreach (var prop in results.Properties())
            {
                var status = (int?)prop.Value["s"] ?? 0;
                var text = (string)prop.Value["t"] ?? "";
                var kind = (status == 405 || text.IndexOf("METHOD_NOT_SUPPORTED", StringComparison.OrdinalIgnoreCase) >= 0) ? "mutation" : "query";

                _discovered[prop.Name] = kind;
                count++;
                AppendDiagnostic($"SONDAGEM existe: {prop.Name} ({kind}) HTTP {status}: {Trim(text, 200)}");
            }

            _logger.Info($"[YGP] Sondagem: {count} rota(s) existente(s) encontrada(s): " +
                         Trim(string.Join(", ", results.Properties().Select(p => p.Name)), 2500));
        }

        private async Task EnsureDiscoveryAsync(bool force = false, bool probe = false)
        {
            if (_discoveryDone && !force) return;
            _discoveryDone = true;

            _logger.Info("[YGP] Descobrindo as rotas tRPC do site (leitura dos scripts públicos)...");
            var wrapper = await RunJsAsync(DiscoveryScript, 240000);

            if (wrapper == null || (bool?)wrapper["ok"] != true)
            {
                var why = wrapper == null ? "tempo esgotado" : (string)wrapper["error"];
                _logger.Warn("[YGP] Descoberta de rotas falhou: " + why);
                AppendDiagnostic("DESCOBERTA falhou: " + why);
                return;
            }

            var found = wrapper["data"]["found"] as JObject;
            var scripts = (int?)wrapper["data"]["scripts"] ?? 0;
            _discovered = new Dictionary<string, string>();

            if (found != null)
            {
                foreach (var prop in found.Properties().Take(1500))
                {
                    var routeName = prop.Name;
                    if (routeName.StartsWith("trpc.", StringComparison.Ordinal)) routeName = routeName.Substring(5);
                    _discovered[routeName] = (string)prop.Value ?? "query";
                }
            }

            var apiPaths = (wrapper["data"]["apis"] as JObject);
            if (apiPaths != null && apiPaths.Count > 0)
            {
                var list = string.Join(", ", apiPaths.Properties().Select(p => p.Name).OrderBy(n => n).Take(80));
                _logger.Info("[YGP] Caminhos /api e /trpc citados nos scripts do site: " + Trim(list, 3000));
                AppendDiagnostic("CAMINHOS /api|/trpc encontrados nos scripts: " + list);
            }

            if (probe) await ProbeRoutesAsync();

            _logger.Info($"[YGP] Descoberta: {scripts} script(s) lido(s), {_discovered.Count} rota(s) candidata(s).");
            AppendDiagnostic($"DESCOBERTA: {scripts} script(s) lido(s), {_discovered.Count} rota(s) candidata(s).");

            var interesting = _discovered
                .Where(kv =>
                {
                    var l = kv.Key.ToLowerInvariant();
                    return l.Contains("search") || l.Contains("game") || l.Contains("unverified") || l.Contains("library") ||
                           l.Contains("playtime") || l.Contains("profile");
                })
                .OrderBy(kv => kv.Key)
                .Take(300);

            foreach (var kv in interesting)
            {
                AppendDiagnostic("  rota: " + kv.Key + " (" + kv.Value + ")");
            }

            var mutations = _discovered.Where(kv => kv.Value == "mutation").Select(kv => kv.Key).OrderBy(k => k).ToList();
            var queries = _discovered.Where(kv => kv.Value != "mutation").Select(kv => kv.Key).OrderBy(k => k).ToList();
            _logger.Info("[YGP] Rotas de ESCRITA do site (" + mutations.Count + "): " + Trim(string.Join(", ", mutations), 3500));
            _logger.Info("[YGP] Rotas de CONSULTA do site (" + queries.Count + "): " + Trim(string.Join(", ", queries), 3500));
        }

        /// <summary>
        /// Botão "Diagnosticar site": descobre as rotas, testa a busca com um jogo de exemplo e grava tudo em ygp-diagnostico.txt.
        /// </summary>
        public async Task<string> RunDiagnosticsAsync()
        {
            if (!HasStoredCookies())
            {
                return L.T("api.linkFirstDiag");
            }

            await _browserLock.WaitAsync();
            try
            {
                AppendDiagnostic("===== DIAGNÓSTICO MANUAL =====");
                var error = await EnsureBrowserAsync(true);
                if (error != null)
                {
                    AppendDiagnostic("Navegador: " + error);
                    return error;
                }

                var state = await CheckSessionAsync();
                AppendDiagnostic("Sessão: " + state);

                await EnsureDiscoveryAsync(true, true);

                var outcome = await SearchAsync("Hades");
                var summary = new StringBuilder();
                summary.AppendLine(L.T("diag.routes", _discovered.Count));
                summary.AppendLine(L.T("diag.searchRoute", string.IsNullOrEmpty(_settings.SearchProcedure) ? L.T("diag.none") : _settings.SearchProcedure));

                if (outcome.Error != null)
                {
                    summary.AppendLine(L.T("diag.testErr", outcome.Error));
                }
                else
                {
                    summary.AppendLine(L.T("diag.testOk", outcome.Candidates.Count));
                    foreach (var c in outcome.Candidates.Take(3))
                    {
                        summary.AppendLine($"  - {c.Title} (id: {(string.IsNullOrEmpty(c.Id) ? L.T("diag.noId") : c.Id)})");
                    }
                }

                AppendDiagnostic("Resumo: " + summary.ToString().Replace(Environment.NewLine, " | "));
                summary.AppendLine(L.T("diag.file", DiagnosticsFilePath));
                return summary.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                AppendDiagnostic("Exceção no diagnóstico: " + ex);
                return L.T("api.diagError", ex.Message);
            }
            finally
            {
                DisposeViewInternal();
                _browserLock.Release();
            }
        }

        private class SearchOutcome
        {
            public List<GameCandidate> Candidates = new List<GameCandidate>();
            public string Error;
            public string Raw = "";
        }

        private static JToken ReplacePlaceholder(JToken token, string query)
        {
            if (token.Type == JTokenType.String && (string)token == "$Q") return new JValue(query);

            var obj = token as JObject;
            if (obj != null)
            {
                var clone = new JObject();
                foreach (var prop in obj.Properties()) clone[prop.Name] = ReplacePlaceholder(prop.Value, query);
                return clone;
            }

            var arr = token as JArray;
            if (arr != null)
            {
                var copy = new JArray();
                foreach (var item in arr) copy.Add(ReplacePlaceholder(item, query));
                return copy;
            }

            return token.DeepClone();
        }

        private static List<JToken> SearchInputTemplates()
        {
            var list = new List<JToken>();
            foreach (var key in new[] { "query", "q", "search", "term", "name", "title", "text", "keyword", "searchTerm", "searchQuery" })
            {
                list.Add(new JObject { { key, "$Q" } });
            }
            list.Add(new JValue("$Q"));
            return list;
        }

        /// <summary>Quando o site responde "campo obrigatório", monta um novo formato de entrada a partir do erro (zod).</summary>
        private static JToken TemplateFromIssues(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return null;
            try
            {
                var issues = JToken.Parse(message) as JArray;
                if (issues == null) return null;

                var obj = new JObject();
                foreach (var issue in issues.OfType<JObject>())
                {
                    var path = issue["path"] as JArray;
                    if (path == null || path.Count == 0) continue;

                    var name = (string)path[0];
                    var expected = (string)issue["expected"];
                    if (expected == "string") obj[name] = "$Q";
                    else if (expected == "number") obj[name] = 10;
                    else if (expected == "boolean") obj[name] = false;
                    else if (expected == "array") obj[name] = new JArray();
                }

                return obj.Count > 0 ? obj : null;
            }
            catch
            {
                return null;
            }
        }

        private readonly HashSet<string> _skipProcs = new HashSet<string>();
        private bool _catalogSearchGaveUp;
        private bool _userSearchGaveUp;

        // Rotas cujo nome indica busca DENTRO de dados do usuário (ex.: screenshot.searchUserGames), e não no catálogo.
        private static readonly string[] UserScopeWords =
        {
            "user", "screenshot", "friend", "follow", "profile", "review", "guide", "tags", "achievement", "insignia",
            "badge", "activity", "comment", "notification", "wishlist", "member", "player", "leaderboard", "gallery"
        };

        private static bool IsUserScopedRoute(string proc)
        {
            if (string.IsNullOrEmpty(proc)) return false;
            return UserScopeWords.Any(w => proc.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private List<string> SearchProcedureCandidates(bool userScoped)
        {
            var scored = new List<KeyValuePair<string, int>>();
            foreach (var kv in _discovered)
            {
                if (kv.Value == "mutation") continue;

                var key = kv.Key;
                var l = key.ToLowerInvariant();
                bool isSearch = l.Contains("search") || l.Contains("autocomplete") || l.Contains("lookup") || l.Contains("findgame") || l.Contains("suggest");
                if (!isSearch) continue;
                if (IsUserScopedRoute(key) != userScoped) continue;

                int score = 0;
                if (l.Contains("game")) score += 4;
                if (l.Contains("search")) score += 2;
                if (l.StartsWith("search.") || l.StartsWith("game.") || l.StartsWith("games.")) score += 1;
                if (key.Split('.').Length == 2) score += 1;
                scored.Add(new KeyValuePair<string, int>(key, score));
            }

            var list = scored.OrderByDescending(x => x.Value).ThenBy(x => x.Key).Select(x => x.Key).ToList();

            var shorter = new List<string>();
            foreach (var p in list)
            {
                var seg = p.Split('.');
                if (seg.Length > 2) shorter.Add(string.Join(".", seg.Skip(seg.Length - 2)));
            }
            foreach (var p in shorter) if (!list.Contains(p)) list.Add(p);

            if (!userScoped)
            {
                foreach (var f in new[] { "game.search", "games.search", "search.games", "search.game", "search.query", "search.all", "unverified.searchGames", "unverified.search", "catalog.search", "igdb.search", "game.find", "games.find" })
                {
                    if (!list.Contains(f)) list.Add(f);
                }
            }

            return list.Take(16).ToList();
        }

        /// <summary>1 = candidatos; 0 = chamada ok sem candidatos; -1 = rota inexistente; -2 = erro duro (outcome.Error); -3 = recusou a entrada.</summary>
        private async Task<int> CallSearchAsync(string proc, JToken template, string query, SearchOutcome outcome, List<JToken> adaptTo)
        {
            var r = await TrpcAsync(proc, ReplacePlaceholder(template, query), false);

            if (r.IsAuth) { outcome.Error = AuthFailure().ErrorMessage; return -2; }
            if (r.IsBlocked) { outcome.Error = r.Message; return -2; }
            if (r.ProcedureMissing) return -1;

            if (r.Ok)
            {
                var found = ExtractCandidates(r.Data);
                outcome.Raw = r.Raw;
                AppendDiagnostic($"BUSCA {proc} {template.ToString(Formatting.None)} '{query}' -> OK, {found.Count} candidato(s).");
                if (found.Count > 0)
                {
                    outcome.Candidates = found;
                    return 1;
                }
                return 0;
            }

            AppendDiagnostic($"BUSCA {proc} {template.ToString(Formatting.None)} -> HTTP {r.Status} {r.Code}: {Trim(r.Message ?? r.Raw, 500)}");

            if (r.Code == "METHOD_NOT_SUPPORTED") return -1;

            if (r.Code == "BAD_REQUEST" && adaptTo != null)
            {
                var adapted = TemplateFromIssues(r.Message);
                if (adapted != null) adaptTo.Add(adapted);
            }
            return -3;
        }

        private async Task<int> TryCachedSearchAsync(string proc, string templateText, string query, SearchOutcome outcome)
        {
            JToken template;
            try { template = JToken.Parse(templateText); }
            catch { return -3; }

            return await CallSearchAsync(proc, template, query, outcome, null);
        }

        private async Task<int> DiscoverSearchRouteAsync(string query, bool userScoped, SearchOutcome outcome)
        {
            var tried = new HashSet<string>();

            foreach (var proc in SearchProcedureCandidates(userScoped))
            {
                var templates = SearchInputTemplates();

                for (int i = 0; i < templates.Count; i++)
                {
                    var template = templates[i];
                    var templateText = template.ToString(Formatting.None);
                    if (!tried.Add(proc + "|" + templateText)) continue;

                    var adapt = new List<JToken>();
                    var code = await CallSearchAsync(proc, template, query, outcome, adapt);

                    if (code == -2) return -2;
                    if (code == -1) break;

                    if (code == 1)
                    {
                        if (userScoped)
                        {
                            _settings.UserSearchProcedure = proc;
                            _settings.UserSearchInputTemplate = templateText;
                        }
                        else
                        {
                            _settings.SearchProcedure = proc;
                            _settings.SearchInputTemplate = templateText;
                        }

                        NotifySettingsChanged();
                        var kind = userScoped ? "busca nos jogos do perfil" : "busca no catálogo";
                        _logger.Info($"[YGP] Rota de {kind} descoberta: {proc} com entrada {templateText}");
                        AppendDiagnostic($"ROTA ({kind}) em uso: {proc} {templateText}");
                        return 1;
                    }

                    foreach (var a in adapt) templates.Add(a);
                }
            }

            return 0;
        }

        /// <summary>
        /// Faz a busca de jogos: primeiro no CATÁLOGO do site; se não achar, nos jogos que já estão no perfil.
        /// As rotas descobertas ficam guardadas nas configurações.
        /// </summary>
        private async Task<SearchOutcome> SearchAsync(string query)
        {
            var outcome = new SearchOutcome();

            // Migra o cache antigo: uma rota "de usuário" nunca deve ser a rota principal de catálogo.
            if (IsUserScopedRoute(_settings.SearchProcedure))
            {
                _settings.UserSearchProcedure = _settings.SearchProcedure;
                _settings.UserSearchInputTemplate = _settings.SearchInputTemplate;
                _settings.SearchProcedure = "";
                _settings.SearchInputTemplate = "";
                NotifySettingsChanged();
            }

            // 1) rota de catálogo já conhecida
            if (!string.IsNullOrEmpty(_settings.SearchProcedure))
            {
                var code = await TryCachedSearchAsync(_settings.SearchProcedure, _settings.SearchInputTemplate, query, outcome);
                if (code == 1 || code == -2) return outcome;
                if (code == -1)
                {
                    _settings.SearchProcedure = "";
                    _settings.SearchInputTemplate = "";
                    NotifySettingsChanged();
                }
            }

            // 2) descobrir a rota de catálogo (uma vez por sessão do Playnite)
            if (string.IsNullOrEmpty(_settings.SearchProcedure) && !_catalogSearchGaveUp)
            {
                await EnsureDiscoveryAsync();
                var code = await DiscoverSearchRouteAsync(query, false, outcome);
                if (code == 1 || code == -2) return outcome;
                if (string.IsNullOrEmpty(_settings.SearchProcedure)) _catalogSearchGaveUp = true;
            }

            // 3) alternativa: procurar entre os jogos que já estão no perfil
            if (!string.IsNullOrEmpty(_settings.UserSearchProcedure))
            {
                var code = await TryCachedSearchAsync(_settings.UserSearchProcedure, _settings.UserSearchInputTemplate, query, outcome);
                if (code == 1 || code == -2) return outcome;
                if (code == -1)
                {
                    _settings.UserSearchProcedure = "";
                    _settings.UserSearchInputTemplate = "";
                    NotifySettingsChanged();
                }
            }
            else if (!_userSearchGaveUp)
            {
                await EnsureDiscoveryAsync();
                var code = await DiscoverSearchRouteAsync(query, true, outcome);
                if (code == 1 || code == -2) return outcome;
                if (string.IsNullOrEmpty(_settings.UserSearchProcedure)) _userSearchGaveUp = true;
            }

            if (_catalogSearchGaveUp && _userSearchGaveUp &&
                string.IsNullOrEmpty(_settings.SearchProcedure) && string.IsNullOrEmpty(_settings.UserSearchProcedure))
            {
                outcome.Error = "[ABORT] " + L.T("err.noSearchRoute");
            }

            return outcome;
        }

        #endregion

        #region Modo captura (aprender rotas reais do site)

        private string _captureJson = "";

        private const string CaptureHookScript = @"
(function(){
  if (window.__ygpHooked) return 'ok';
  window.__ygpHooked = true; window.__ygpLog = window.__ygpLog || [];
  function keep(u){ return u.indexOf('/trpc/')>=0 || (u.indexOf('/api/')>=0 && u.indexOf('/api/auth')<0); }
  function rec(e){ try{ var l=window.__ygpLog; l.push(e); if(l.length>300) l.shift(); }catch(x){} }
  var of=window.fetch;
  if(of){ window.fetch=function(input,init){
    var url=(typeof input==='string')?input:((input&&input.url)||'');
    var method=(init&&init.method)||(input&&input.method)||'GET';
    var body=(init&&typeof init.body==='string')?init.body:'';
    var p=of.apply(this,arguments);
    if(keep(url)){ p.then(function(r){ try{ r.clone().text().then(function(t){ rec({u:url,m:method,b:body.substring(0,4000),s:r.status,r:t.substring(0,8000)}); }); }catch(x){} }).catch(function(){}); }
    return p; }; }
  var xo=XMLHttpRequest.prototype.open, xs=XMLHttpRequest.prototype.send;
  XMLHttpRequest.prototype.open=function(m,u){ this.__ygp={m:m,u:String(u)}; return xo.apply(this,arguments); };
  XMLHttpRequest.prototype.send=function(b){ var self=this; try{ if(this.__ygp&&keep(this.__ygp.u)){ this.addEventListener('loadend',function(){ rec({u:self.__ygp.u,m:self.__ygp.m,b:(typeof b==='string'?b:'').substring(0,4000),s:self.status,r:(self.responseText||'').substring(0,8000)}); }); } }catch(x){} return xs.apply(this,arguments); };
  return 'hooked';
})();";

        private string RunCaptureDialog()
        {
            _captureJson = "";

            var webViewSettings = new WebViewSettings
            {
                WindowWidth = 1150,
                WindowHeight = 800,
                JavaScriptEnabled = true
            };

            using (var view = _playniteApi.WebViews.CreateView(webViewSettings))
            {
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
                timer.Tick += (s, e) =>
                {
                    try
                    {
                        view.EvaluateScriptAsync(CaptureHookScript);

                        var read = view.EvaluateScriptAsync("JSON.stringify(window.__ygpLog||[])");
                        read.ContinueWith(t =>
                        {
                            try
                            {
                                dynamic r = t.Result;
                                string text = r?.Result as string;
                                if (!string.IsNullOrEmpty(text) && text.Length > 2) _captureJson = text;
                            }
                            catch { }
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug($"[YGP] Captura: {ex.Message}");
                    }
                };

                view.Navigate(BaseUrl + "/");
                timer.Start();
                view.OpenDialog();
                timer.Stop();
            }

            return _captureJson;
        }

        private class CapturedCall
        {
            public string Proc;
            public bool Mutation;
            public JToken Input;
            public int Status;
            public string Response;
        }

        private static JToken TryParse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            try { return JToken.Parse(text); }
            catch { return null; }
        }

        private static List<CapturedCall> ParseCapture(string json)
        {
            var calls = new List<CapturedCall>();
            var arr = TryParse(json) as JArray;
            if (arr == null) return calls;

            foreach (var e in arr.OfType<JObject>())
            {
                var url = (string)e["u"] ?? "";
                var method = ((string)e["m"] ?? "GET").ToUpperInvariant();
                var body = (string)e["b"] ?? "";
                var status = (int?)e["s"] ?? 0;
                var resp = (string)e["r"] ?? "";

                var idx = url.IndexOf("/trpc/", StringComparison.Ordinal);
                if (idx < 0)
                {
                    calls.Add(new CapturedCall { Proc = "REST " + method + " " + Trim(url, 200), Mutation = method != "GET", Status = status, Response = resp, Input = TryParse(body) });
                    continue;
                }

                var rest = url.Substring(idx + 6);
                var q = rest.IndexOf('?');
                var procPart = q >= 0 ? rest.Substring(0, q) : rest;
                var procs = procPart.Split(',');

                JToken inputs = null;
                if (method == "GET" && q >= 0)
                {
                    var m = Regex.Match(rest.Substring(q), @"[?&]input=([^&]*)");
                    if (m.Success) inputs = TryParse(Uri.UnescapeDataString(m.Groups[1].Value));
                }
                else
                {
                    inputs = TryParse(body);
                }

                var respArr = TryParse(resp) as JArray;
                var inputObj = inputs as JObject;

                for (int i = 0; i < procs.Length; i++)
                {
                    JToken input = inputObj != null ? inputObj[i.ToString()] : null;
                    var inputAsObject = input as JObject;
                    if (inputAsObject != null && inputAsObject["json"] != null && inputAsObject.Count <= 2) input = inputAsObject["json"];

                    JToken one = (respArr != null && respArr.Count > i) ? respArr[i] : null;
                    calls.Add(new CapturedCall
                    {
                        Proc = procs[i],
                        Mutation = method != "GET",
                        Input = input,
                        Status = status,
                        Response = one != null ? one.ToString(Formatting.None) : resp
                    });
                }
            }

            return calls;
        }

        private static JToken BuildSearchTemplate(JToken input)
        {
            var obj = input as JObject;
            if (obj == null) return null;

            var clone = (JObject)obj.DeepClone();

            // prefere o campo que parece ser o texto digitado (search/query/q/term/name/title), mesmo vazio
            foreach (var prop in clone.Properties().ToList())
            {
                var n = prop.Name.ToLowerInvariant();
                if (prop.Value.Type == JTokenType.String && (n == "search" || n == "query" || n == "q" || n == "term" || n == "name" || n == "title"))
                {
                    clone[prop.Name] = "$Q";
                    return clone;
                }
            }

            foreach (var prop in clone.Properties().ToList())
            {
                if (prop.Value.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string)prop.Value))
                {
                    clone[prop.Name] = "$Q";
                    return clone;
                }
            }
            return null;
        }

        private static bool LooksLikeSearchCall(CapturedCall c)
        {
            var l = c.Proc.ToLowerInvariant();
            if (l.Contains("search") || l.Contains("find") || l.Contains("lookup") || l.Contains("autocomplete") || l.Contains("suggest")) return true;

            var obj = c.Input as JObject;
            return obj != null && obj.Properties().Any(p =>
            {
                var n = p.Name.ToLowerInvariant();
                return n == "query" || n == "q" || n == "search" || n == "term" || n == "searchterm" || n == "keyword" || n == "text";
            });
        }

        /// <summary>
        /// Abre o site numa janela, registra as chamadas que o PRÓPRIO site faz (busca de jogos, edição de horas)
        /// e guarda as rotas reais. Tudo vai para o ygp-diagnostico.txt.
        /// </summary>
        public async Task<string> LearnFromSiteAsync()
        {
            if (!HasStoredCookies()) return L.T("api.linkFirstLearn");

            string json = "";
            RunOnUIThread(() =>
            {
                try { json = RunCaptureDialog(); }
                catch (Exception ex) { _logger.Error(ex, $"[YGP] Erro no modo captura: {ex.Message}"); }
            });

            await Task.Yield();

            var calls = ParseCapture(json);
            AppendDiagnostic("===== CAPTURA (modo aprender): " + calls.Count + " chamada(s) =====");
            foreach (var c in calls)
            {
                AppendDiagnostic($"CAPTURA {(c.Mutation ? "POST" : "GET")} {c.Proc} entrada={Trim(c.Input != null ? c.Input.ToString(Formatting.None) : "", 500)} HTTP {c.Status} resposta={Trim(c.Response, 600)}");
            }

            var summary = new StringBuilder();
            summary.AppendLine(L.T("learn.calls", calls.Count));

            // 1) busca no catálogo
            string searchProc = null;
            JToken searchTemplate = null;
            foreach (var c in calls.Where(x => !x.Mutation && !x.Proc.StartsWith("REST ", StringComparison.Ordinal)))
            {
                if (!LooksLikeSearchCall(c) || IsUserScopedRoute(c.Proc)) continue;

                var parsedObj = TryParse(c.Response) as JObject;
                var res = parsedObj != null ? parsedObj["result"] : null;
                JToken data = res != null ? res["data"] : null;
                var dataObj = data as JObject;
                if (dataObj != null && dataObj["json"] != null && dataObj.Count <= 2) data = dataObj["json"];

                bool listShape = data is JArray;
                var asObj = data as JObject;
                if (!listShape && asObj != null) listShape = new[] { "games", "results", "items", "hits", "data" }.Any(n => asObj[n] is JArray);
                if (!listShape) continue;

                var candidates = ExtractCandidates(data);
                if (candidates.Count == 0 || !candidates.Any(x => !string.IsNullOrEmpty(x.Id))) continue;

                var template = BuildSearchTemplate(c.Input);
                if (template == null) continue;

                searchProc = c.Proc;
                searchTemplate = template;
            }

            if (searchProc != null)
            {
                _settings.SearchProcedure = searchProc;
                _settings.SearchInputTemplate = searchTemplate.ToString(Formatting.None);
                _catalogSearchGaveUp = false;
                summary.AppendLine(L.T("learn.searchOk", searchProc));
            }
            else
            {
                summary.AppendLine(L.T("learn.searchNone"));
            }

            // 2) edição de horas
            CapturedCall update = null;
            foreach (var c in calls.Where(x => x.Mutation && !x.Proc.StartsWith("REST ", StringComparison.Ordinal)))
            {
                if (c.Proc == "unverified.addGame") continue;
                if (c.Status < 200 || c.Status >= 300) continue;

                var obj = c.Input as JObject;
                if (obj == null) continue;

                bool hasPlaytime = obj.Properties().Any(p => p.Name.IndexOf("playtime", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                                              (p.Value.Type == JTokenType.Integer || p.Value.Type == JTokenType.Float));
                bool hasGameId = obj.Properties().Any(p => p.Name.Equals("gameId", StringComparison.OrdinalIgnoreCase) ||
                                                            p.Name.Equals("game_id", StringComparison.OrdinalIgnoreCase));
                if (hasPlaytime && hasGameId) update = c;
            }

            if (update != null)
            {
                _settings.LearnedUpdateProcedure = update.Proc;
                _settings.LearnedUpdateTemplate = update.Input.ToString(Formatting.None);
                summary.AppendLine(L.T("learn.editOk", update.Proc));
            }
            else
            {
                var mutations = calls.Where(x => x.Mutation).Select(x => x.Proc).Distinct().ToList();
                summary.AppendLine(L.T("learn.editNone") +
                                   (mutations.Count > 0 ? L.T("learn.seenWrites", string.Join(", ", mutations.Take(6))) : ""));
            }

            NotifySettingsChanged();
            summary.AppendLine(L.T("learn.details", DiagnosticsFilePath));
            return summary.ToString().TrimEnd();
        }

        #endregion

        #region Busca de jogos

        private class GameCandidate
        {
            public string Id;
            public string Title;
            public string Slug;
        }

        private static string FirstString(JObject obj, params string[] keys)
        {
            foreach (var k in keys)
            {
                var v = JsonString(obj, k);
                if (!string.IsNullOrEmpty(v)) return v;
            }
            return null;
        }

        private static List<GameCandidate> ExtractCandidates(JToken data)
        {
            var list = new List<GameCandidate>();
            if (data == null) return list;

            JArray arr = data as JArray;
            var asObject = data as JObject;
            if (arr == null && asObject != null)
            {
                foreach (var name in new[] { "games", "results", "items", "hits", "data" })
                {
                    var p = asObject[name] as JArray;
                    if (p != null) { arr = p; break; }
                }
                if (arr == null)
                {
                    foreach (var prop in asObject.Properties())
                    {
                        var p = prop.Value as JArray;
                        if (p != null) { arr = p; break; }
                    }
                }
            }
            if (arr == null) return list;

            foreach (var item in arr.OfType<JObject>())
            {
                var title = FirstString(item, "title", "name", "displayName", "label");
                var slug = FirstString(item, "slug", "gameSlug");
                var id = FirstString(item, "id", "gameId");

                if (string.IsNullOrEmpty(slug))
                {
                    var url = FirstString(item, "url", "path", "href") ?? "";
                    var m = Regex.Match(url, @"/games/([a-z0-9\-_]+)", RegexOptions.IgnoreCase);
                    if (m.Success) slug = m.Groups[1].Value;
                }

                if (!string.IsNullOrEmpty(title) || !string.IsNullOrEmpty(slug))
                {
                    list.Add(new GameCandidate { Id = id, Title = title, Slug = slug });
                }
            }

            return list;
        }

        private static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalized.Length);
            foreach (var ch in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        private static readonly Dictionary<string, string> RomanTokens = new Dictionary<string, string>
        {
            { "ii", "2" }, { "iii", "3" }, { "iv", "4" }, { "v", "5" }, { "vi", "6" }, { "vii", "7" }, { "viii", "8" }, { "ix", "9" }
        };

        // Palavras que não identificam o jogo (edições, marcas de publisher, artigos).
        private static readonly HashSet<string> NoiseTokens = new HashSet<string>
        {
            "the", "a", "an", "s", "edition", "remastered", "remaster", "definitive", "complete", "deluxe", "ultimate",
            "goty", "game", "preview", "early", "access", "demo", "playtest", "beta", "showcase", "lite", "founders",
            "anniversary", "enhanced", "collection", "hd", "digital", "standard", "ea", "sports", "tom", "clancy",
            "marvel", "aberto", "do", "da", "de", "for", "windows", "pc", "bedrock", "java"
        };

        // Palavras extras que indicam OUTRO produto (versão mobile, demo, trilha sonora...).
        private static readonly HashSet<string> BadExtras = new HashSet<string>
        {
            "mobile", "demo", "playtest", "beta", "soundtrack", "dlc", "ost", "prologue", "trial", "server", "tool",
            "tools", "expansion", "pass", "bundle", "pack", "artbook", "arcade"
        };

        private static string NormalizeTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return "";
            var t = RemoveDiacritics(title).ToLowerInvariant().Replace("&", " and ");
            t = Regex.Replace(t, @"[^a-z0-9]+", " ").Trim();
            var tokens = t.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                          .Select(tok => RomanTokens.ContainsKey(tok) ? RomanTokens[tok] : tok);
            return string.Join(" ", tokens);
        }

        private static string CompareKey(string title)
        {
            var noParens = Regex.Replace(title ?? "", @"\(.*?\)|\[.*?\]", " ");
            var norm = NormalizeTitle(noParens);
            var tokens = norm.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Where(t => !NoiseTokens.Contains(t));
            return string.Join(" ", tokens);
        }

        /// <summary>Chave de comparação de títulos (ignora marcas, edições e pontuação) para detectar jogos duplicados.</summary>
        /// <summary>True se o título do site ainda corresponde ao jogo do Playnite (valida mapeamentos antigos).</summary>
        public static bool TitlesMatch(string wanted, string found)
        {
            return ScoreMatch(wanted, found, true) >= 50;
        }

        public static string DuplicateKey(string title)
        {
            return CompareKey(title);
        }

        private static bool IsNumberToken(string t)
        {
            return !string.IsNullOrEmpty(t) && t.All(char.IsDigit);
        }

        private static int ScoreMatch(string wanted, string found, bool relaxed)
        {
            var wn = NormalizeTitle(wanted);
            var fn = NormalizeTitle(found);
            if (wn.Length == 0 || fn.Length == 0) return 0;
            if (wn == fn) return 100;

            var wk = CompareKey(wanted);
            var fk = CompareKey(found);
            if (wk.Length == 0 || fk.Length == 0) return 0;
            if (wk == fk) return 95;

            var wt = wk.Split(' ');
            var ft = fk.Split(' ');

            // o site tem o pedido + 1 ou 2 palavras (ex.: "Rainbow Six Siege" -> "Rainbow Six Siege X")
            if (ft.Length > wt.Length && ft.Length - wt.Length <= 2 && wk.Length >= 4 && fk.StartsWith(wk + " ", StringComparison.Ordinal))
            {
                var extras = ft.Skip(wt.Length).ToList();
                if (extras.Any(e => BadExtras.Contains(e) || IsNumberToken(e))) return 0;
                return 75;
            }

            if (!relaxed) return 0;

            // jogo com subtítulo/expansão ("Jogo: Expansão") e o site só tem o jogo base
            var baseTitle = Regex.Split(Regex.Replace(wanted ?? "", @"\(.*?\)|\[.*?\]", " "), @":|\s[-–—]\s")[0];
            var bk = CompareKey(baseTitle);
            if (bk.Length >= 4 && bk == fk) return 55;

            // quase iguais: mesmos números e pelo menos 80% das palavras em comum
            var wset = new HashSet<string>(wt);
            var fset = new HashSet<string>(ft);
            var inter = wset.Intersect(fset).Count();
            var union = wset.Union(fset).Count();
            var wnum = new HashSet<string>(wt.Where(IsNumberToken));
            var fnum = new HashSet<string>(ft.Where(IsNumberToken));
            if (union >= 3 && wnum.SetEquals(fnum) && (double)inter / union >= 0.8) return 50;

            return 0;
        }

        private static GameCandidate PickBest(string wanted, List<GameCandidate> candidates, bool relaxed)
        {
            GameCandidate best = null;
            int bestScore = 0;
            foreach (var c in candidates)
            {
                var s = ScoreMatch(wanted, c.Title, relaxed);
                if (s > bestScore) { best = c; bestScore = s; }
            }
            return bestScore >= (relaxed ? 50 : 75) ? best : null;
        }

        private static List<string> BuildQueries(string gameName)
        {
            var queries = new List<string>();
            Action<string> add = q =>
            {
                q = Regex.Replace(q ?? "", @"\s+", " ").Trim();
                if (q.Length >= 2) queries.Add(q);
            };

            var clean = RemoveDiacritics(gameName ?? "").Replace("™", "").Replace("®", "").Replace("©", "").Trim();
            add(clean);

            var noParens = Regex.Replace(clean, @"\(.*?\)|\[.*?\]", " ");
            add(noParens);
            add(noParens.Split(':')[0]);
            add(Regex.Split(noParens, @"\s[-–—]\s")[0]);

            var key = CompareKey(gameName);
            add(key);

            var words = key.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 3) add(string.Join(" ", words.Take(3)));

            return queries.Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToList();
        }

        private class GameResolution
        {
            public GameCandidate Game;
            public string Error;
        }

        private GameResolution ResolveFromCandidate(GameCandidate best, string gameName, string raw, bool approximate)
        {
            if (string.IsNullOrEmpty(best.Id))
            {
                _logger.Warn($"[YGP] Resultado '{best.Title}' veio sem ID. Resposta bruta: {Trim(raw, 600)}");
                AppendDiagnostic($"Resultado sem ID para '{best.Title}'. Bruto: {Trim(raw, 800)}");
                return new GameResolution
                {
                    Error = "[ABORT] " + L.T("err.noGameId", best.Title)
                };
            }

            if (approximate)
            {
                _logger.Warn($"[YGP] Correspondência APROXIMADA: '{gameName}' -> '{best.Title}' ({best.Id})");
                AppendDiagnostic($"APROXIMADO: '{gameName}' -> '{best.Title}' ({best.Id})");
            }
            else
            {
                _logger.Info($"[YGP] Jogo resolvido: '{gameName}' -> '{best.Title}' ({best.Id})");
            }

            return new GameResolution { Game = best };
        }

        private async Task<GameResolution> ResolveGameAsync(string gameName)
        {
            var seen = new List<GameCandidate>();
            string lastError = null;
            string lastRaw = "";
            bool anyRawLogged = false;

            foreach (var query in BuildQueries(gameName))
            {
                var outcome = await SearchAsync(query);

                if (outcome.Error != null)
                {
                    if (outcome.Error.StartsWith("[AUTH]", StringComparison.Ordinal) || outcome.Error.StartsWith("[ABORT]", StringComparison.Ordinal))
                    {
                        return new GameResolution { Error = outcome.Error };
                    }
                    lastError = outcome.Error;
                    _logger.Warn($"[YGP] Busca '{query}' falhou: {outcome.Error}");
                    continue;
                }

                var candidates = outcome.Candidates;
                lastRaw = outcome.Raw;
                _logger.Info($"[YGP] Busca '{query}': {candidates.Count} resultado(s).");

                if (candidates.Count == 0 && !anyRawLogged && !string.IsNullOrEmpty(outcome.Raw))
                {
                    anyRawLogged = true;
                    _logger.Debug($"[YGP] Resposta bruta da busca (sem candidatos reconhecidos): {Trim(outcome.Raw, 600)}");
                }

                var best = PickBest(gameName, candidates, false);
                if (best != null) return ResolveFromCandidate(best, gameName, outcome.Raw, false);

                foreach (var c in candidates)
                {
                    if (!seen.Any(x => x.Id == c.Id && x.Title == c.Title)) seen.Add(c);
                }
                await Task.Delay(150);
            }

            // Nenhum nome idêntico: aceita uma correspondência aproximada (expansão -> jogo base, etc.).
            var relaxedBest = PickBest(gameName, seen, true);
            if (relaxedBest != null) return ResolveFromCandidate(relaxedBest, gameName, lastRaw, true);

            if (seen.Count == 0 && lastError != null)
            {
                return new GameResolution { Error = L.T("err.searchFailed", lastError) };
            }

            var names = string.Join(" | ", seen.Select(c => c.Title).Where(t => !string.IsNullOrEmpty(t)).Distinct().Take(5));
            AppendDiagnostic($"NÃO ENCONTRADO: '{gameName}'. Parecidos: {names}");
            return new GameResolution
            {
                Error = "[NOTFOUND] " + L.T("err.notFound", gameName) +
                        (names.Length > 0 ? L.T("err.notFoundSimilar", names) : ".")
            };
        }

        #endregion

        #region Sincronização

        private static bool IsConflict(TrpcResult r)
        {
            return r.Status == 409 || r.Code == "CONFLICT" ||
                   (r.Message ?? "").IndexOf("already have", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Rotas "adivinhadas" só valem se a resposta confirmar o jogo/tempo enviado.</summary>
        private static bool LooksLikeConfirmedWrite(TrpcResult r, string gameId, int minutes)
        {
            var raw = r.Raw ?? "";
            if (!string.IsNullOrEmpty(gameId) && raw.IndexOf(gameId, StringComparison.Ordinal) >= 0) return true;
            return Regex.IsMatch(raw, "playtime[A-Za-z]*\"\\s*:\\s*" + minutes + @"\b", RegexOptions.IgnoreCase);
        }

        private static string DateToIso(DateTime d)
        {
            // meio-dia UTC do dia de calendário: aparece como o mesmo dia em praticamente qualquer fuso horário
            return new DateTime(d.Year, d.Month, d.Day, 12, 0, 0, DateTimeKind.Utc)
                .ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        }

        private async Task<YgpSyncResult> WritePlaytimeAsync(string gameId, string platform, int minutes, YgpPlayDates dates)
        {
            var targetPlatform = MapPlatform(platform);

            // Datas: só envia as que o plugin realmente conhece; sem datas marca "Não lembro" (ignoreDates).
            JValue startedToken = (dates != null && dates.Start.HasValue) ? new JValue(DateToIso(dates.Start.Value)) : JValue.CreateNull();
            JValue completedToken = (dates != null && dates.Completed.HasValue) ? new JValue(DateToIso(dates.Completed.Value)) : JValue.CreateNull();
            bool anyDate = dates != null && (dates.Start.HasValue || dates.Completed.HasValue);

            var payload = new JObject
            {
                { "gameId", gameId },
                { "completedAt", completedToken },
                { "ignoreDates", !anyDate },
                { "ignorePlaytime", false },
                { "isAbandoned", false },
                { "isBacklog", false },
                { "isCompleted", false },
                { "isFinished", false },
                { "isPaused", false },
                { "isPlaying", true },
                { "platformPlayed", targetPlatform },
                { "playtimeMinutes", minutes },
                { "rating", JValue.CreateNull() },
                { "startedAt", startedToken }
            };

            var attempts = new List<string>();
            var tried = new HashSet<string>();
            var candidates = new List<string> { "unverified.addGame" };
            bool discoveryAdded = true; // não adivinhamos mais rotas de escrita: só as rotas reais conhecidas
            bool alreadyOnProfile = false;

            // Se o jogo já existe no perfil nesta plataforma, ATUALIZA em vez de tentar adicionar.
            var preUpdate = await TryUpdateExistingAsync(gameId, targetPlatform, minutes, dates, true, attempts);
            if (preUpdate != null) return preUpdate;

            for (int i = 0; ; i++)
            {
                if (i >= candidates.Count)
                {
                    if (discoveryAdded) break;

                    discoveryAdded = true;
                    await EnsureDiscoveryAsync();
                    candidates.AddRange(DiscoveredWriteProcedures().Where(p => !tried.Contains(p)));
                    if (i >= candidates.Count) break;
                }

                var proc = candidates[i];
                if (!tried.Add(proc)) continue;
                if (_skipProcs.Contains(proc)) continue;

                bool isAdd = proc == "unverified.addGame";
                var current = (JObject)payload.DeepClone();
                var r = await TrpcAsync(proc, current, true);

                // O site diz quais campos faltam/sobram (zod): ajusta o envio e tenta de novo (até 3 vezes).
                for (int round = 0; round < 3 && !r.Ok && r.Code == "BAD_REQUEST"; round++)
                {
                    var adapted = AdaptPayload(current, r.Message, gameId, targetPlatform, minutes);
                    if (adapted == null) break;

                    AppendDiagnostic($"ESCRITA {proc}: ajustando o envio -> {Trim(adapted.ToString(Formatting.None), 400)}");
                    current = adapted;
                    r = await TrpcAsync(proc, current, true);
                }

                if (r.Ok && (isAdd || LooksLikeConfirmedWrite(r, gameId, minutes)))
                {
                    _logger.Info($"[YGP] Jogo {gameId} gravado com {minutes}m em {targetPlatform} via {proc}.");
                    RememberWorkingProcedure(proc);

                    if (!isAdd)
                    {
                        _settings.LearnedUpdateProcedure = proc;
                        _settings.LearnedUpdateTemplate = current.ToString(Formatting.None);
                        NotifySettingsChanged();
                        AppendDiagnostic($"ROTA DE EDIÇÃO em uso: {proc} {current.ToString(Formatting.None)}");
                    }

                    return new YgpSyncResult { Success = true, YgpGameId = gameId, TotalPlaytimeMinutes = minutes };
                }

                // Só falha de login de verdade interrompe; "sem permissão" numa rota experimental apenas descarta a rota.
                bool hardAuth = r.Status == 401 || r.Code == "UNAUTHORIZED" || (isAdd && r.IsAuth);
                if (hardAuth) return AuthFailure();
                if (r.IsBlocked) return new YgpSyncResult { Success = false, ErrorMessage = r.Message };

                if (r.Ok)
                {
                    attempts.Add(proc + ": respondeu OK, mas sem confirmar o tempo (ignorado)");
                    AppendDiagnostic($"ESCRITA {proc} -> OK sem confirmação: {Trim(r.Raw, 500)}");
                    _skipProcs.Add(proc);
                }
                else if (r.ProcedureMissing)
                {
                    attempts.Add(proc + ": não existe no site");
                    _skipProcs.Add(proc);
                }
                else
                {
                    if (isAdd && IsConflict(r))
                    {
                        alreadyOnProfile = true;

                        var edited = await TryUpdateExistingAsync(gameId, targetPlatform, minutes, dates, false, attempts);
                        if (edited != null) return edited;

                        var learned = await TryLearnedUpdateAsync(gameId, targetPlatform, minutes, attempts);
                        if (learned != null) return learned;
                    }
                    else if (!isAdd)
                    {
                        _skipProcs.Add(proc);
                    }

                    attempts.Add(proc + ": " + Trim(r.Message ?? r.Raw, 220));
                    _logger.Warn($"[YGP] {proc} recusado (HTTP {r.Status}, {r.Code}): {Trim(r.Raw, 500)}");
                    AppendDiagnostic($"ESCRITA {proc} -> HTTP {r.Status} {r.Code}: {Trim(r.Raw, 700)}");
                }
            }

            if (alreadyOnProfile)
            {
                // O site já tem esse jogo nessa plataforma, mas a atualização das horas não foi aceita.
                // Isso é uma FALHA (volta para a fila e aparece no erro), nunca "sincronizado": antes o plugin
                // marcava como enviado e as horas do site ficavam paradas.
                return new YgpSyncResult
                {
                    Success = false,
                    AlreadyOnProfile = true,
                    UpdateFailed = true,
                    YgpGameId = gameId,
                    TotalPlaytimeMinutes = minutes,
                    ErrorMessage = L.T("err.existsRefused") + (attempts.Count > 0 ? " " + Trim(string.Join(" || ", attempts), 400) : "")
                };
            }

            return new YgpSyncResult
            {
                Success = false,
                ErrorMessage = L.T("api.siteRefused") + " " + string.Join(" || ", attempts)
            };
        }

        private static JToken GuessValue(string key, string expected, string gameId, string platform, int minutes)
        {
            var n = key.ToLowerInvariant();
            if (n == "gameid" || n == "game_id") return new JValue(gameId);
            if (n.Contains("platform")) return new JValue(platform);
            if (n.Contains("playtime") || n.Contains("minutes")) return new JValue(minutes);
            if (expected == "boolean") return new JValue(false);
            if (expected == "array") return new JArray();
            if (expected == "number") return new JValue(0);
            return null; // não inventa textos
        }

        /// <summary>Ajusta o envio conforme a validação do site (campos faltando / campos não aceitos).</summary>
        private static JObject AdaptPayload(JObject current, string message, string gameId, string platform, int minutes)
        {
            var issues = TryParse(message) as JArray;
            if (issues == null) return null;

            var clone = (JObject)current.DeepClone();
            bool changed = false;

            foreach (var issue in issues.OfType<JObject>())
            {
                var code = (string)issue["code"];

                if (code == "unrecognized_keys")
                {
                    var keys = issue["keys"] as JArray;
                    if (keys != null)
                    {
                        foreach (var k in keys)
                        {
                            if (clone.Remove((string)k)) changed = true;
                        }
                    }
                    continue;
                }

                var path = issue["path"] as JArray;
                if (path == null || path.Count != 1) continue;

                var key = (string)path[0];
                if (string.IsNullOrEmpty(key) || clone[key] != null) continue;

                var value = GuessValue(key, (string)issue["expected"], gameId, platform, minutes);
                if (value != null)
                {
                    clone[key] = value;
                    changed = true;
                }
            }

            return changed ? clone : null;
        }

        private static bool IsNullToken(JToken t)
        {
            return t == null || t.Type == JTokenType.Null || (t.Type == JTokenType.String && string.IsNullOrEmpty((string)t));
        }

        private static JToken IsoOrNull(DateTime? d)
        {
            return d.HasValue ? (JToken)new JValue(DateToIso(d.Value)) : JValue.CreateNull();
        }

        private static DateTime? ParseIso(JToken t)
        {
            if (IsNullToken(t)) return null;
            if (t.Type == JTokenType.Date) return t.Value<DateTime>().ToUniversalTime();
            DateTime d;
            if (DateTime.TryParse((string)t, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out d)) return d;
            return null;
        }

        /// <summary>O site exige startedAt &lt;= completedAt. Se estiver invertido, o início passa a ser a conclusão.</summary>
        private static void FixDateOrder(ref JToken started, ref JToken completed)
        {
            var s = ParseIso(started);
            var c = ParseIso(completed);
            if (s.HasValue && c.HasValue && s.Value > c.Value) started = completed.DeepClone();
        }

        private static bool Flag(JObject o, string key)
        {
            var t = o[key];
            return t != null && t.Type == JTokenType.Boolean && (bool)t;
        }

        /// <summary>Plataforma da entrada do perfil (STEAM, XBOX, UNVERIFIED...) combina com a plataforma jogada?</summary>
        private static bool PlatformMatches(string entryPlatform, string entrySubPlatform, string target)
        {
            if (!string.IsNullOrEmpty(entrySubPlatform))
            {
                return string.Equals(entrySubPlatform, target, StringComparison.OrdinalIgnoreCase);
            }

            var p = (entryPlatform ?? "").ToUpperInvariant();
            var t = (target ?? "").ToLowerInvariant();

            if (new[] { "STEAM", "EPICGAMES", "GOG", "BATTLENET", "UBISOFT", "EA" }.Contains(p)) return t.Contains("windows") || t.Contains("pc");
            if (p == "XBOX") return t.Contains("xbox");
            if (p == "PLAYSTATION") return t.Contains("playstation");
            if (p == "NINTENDO") return t.Contains("nintendo");
            return false;
        }

        /// <summary>
        /// Atualiza as horas de um jogo que JÁ está no perfil, usando as chamadas reais do site (capturadas):
        /// game.getGameUserStatus {gameId} -> entradas do perfil (platformGames) e sync.updateGameEdit
        /// {platform, userGameId, customPlaytimeMinutes, flags, rating, startedAt, completedAt, ...}.
        /// Preserva status, nota e datas que já estão no site. Retorna null se não houver entrada para atualizar.
        /// </summary>
        private async Task<YgpSyncResult> TryUpdateExistingAsync(string gameId, string targetPlatform, int minutes, YgpPlayDates dates, bool requireMatch, List<string> attempts)
        {
            const string statusProc = "game.getGameUserStatus";
            const string updateProc = "sync.updateGameEdit";
            if (_skipProcs.Contains(statusProc) || _skipProcs.Contains(updateProc)) return null;

            var status = await TrpcAsync(statusProc, new JObject { { "gameId", gameId } }, false);
            if (status.Status == 401 || status.Code == "UNAUTHORIZED") return AuthFailure();
            if (status.IsBlocked) return new YgpSyncResult { Success = false, ErrorMessage = status.Message };
            if (status.ProcedureMissing)
            {
                _skipProcs.Add(statusProc);
                return null;
            }
            if (!status.Ok)
            {
                AppendDiagnostic($"STATUS {statusProc} {gameId} -> HTTP {status.Status} {status.Code}: {Trim(status.Message ?? status.Raw, 400)}");
                return null;
            }

            var data = status.Data as JObject;
            var list = data != null ? data["platformGames"] as JArray : null;
            var entries = list != null ? list.OfType<JObject>().ToList() : new List<JObject>();
            if (entries.Count == 0) return null;

            var pick = entries.FirstOrDefault(e => PlatformMatches((string)e["platform"], (string)e["subPlatform"], targetPlatform));
            if (pick == null && !requireMatch)
            {
                pick = entries.FirstOrDefault(e => string.Equals((string)e["platform"], "UNVERIFIED", StringComparison.OrdinalIgnoreCase)) ?? entries[0];
            }
            if (pick == null) return null;

            var userGameId = (string)pick["id"];
            if (string.IsNullOrEmpty(userGameId)) return null;

            AppendDiagnostic("ENTRADA no perfil: " + Trim(pick.ToString(Formatting.None), 900));

            // Horas atuais no site (a edição manual, se houver, vale mais que o valor sincronizado).
            double? existing = null;
            var custom = pick["customPlaytimeMinutes"];
            var synced = pick["playtimeMinutes"];
            if (custom != null && (custom.Type == JTokenType.Integer || custom.Type == JTokenType.Float)) existing = (double)custom;
            else if (synced != null && (synced.Type == JTokenType.Integer || synced.Type == JTokenType.Float)) existing = (double)synced;

            bool force = dates != null && dates.Force;
            if (!force && existing.HasValue && existing.Value >= minutes)
            {
                return new YgpSyncResult
                {
                    Success = true,
                    AlreadyOnProfile = true,
                    YgpGameId = gameId,
                    TotalPlaytimeMinutes = minutes,
                    ErrorMessage = "[EXISTS] " + L.T("err.existsHours", existing.Value, minutes)
                };
            }

            // Datas: mantém as do site; só preenche o início se estiver vazio e a conclusão se o usuário pediu.
            JToken started = IsNullToken(pick["startedAt"]) ? IsoOrNull(dates != null ? dates.Start : null) : pick["startedAt"].DeepClone();
            JToken completed;
            if (dates != null && dates.Completed.HasValue) completed = IsoOrNull(dates.Completed);
            else completed = IsNullToken(pick["completedAt"]) ? (JToken)JValue.CreateNull() : pick["completedAt"].DeepClone();

            JToken rating = IsNullToken(pick["gameRating"]) ? (JToken)JValue.CreateNull() : pick["gameRating"].DeepClone();

            // O site exige início <= conclusão. Se a ordem estiver invertida (ex.: início = dia em que o jogo foi
            // adicionado e conclusão = histórico antigo), corrige antes de enviar em vez de levar um erro 400.
            FixDateOrder(ref started, ref completed);

            var payload = new JObject
            {
                { "platform", (string)pick["platform"] ?? "UNVERIFIED" },
                { "userGameId", userGameId },
                { "customPlaytimeMinutes", minutes },
                { "isCompleted", Flag(pick, "isCompleted") },
                { "isFinished", Flag(pick, "isFinished") },
                { "isPlaying", Flag(pick, "isPlaying") },
                { "isAbandoned", Flag(pick, "isAbandoned") },
                { "isPaused", Flag(pick, "isPaused") },
                { "isBacklog", Flag(pick, "isBacklog") },
                { "rating", rating },
                { "startedAt", started },
                { "completedAt", completed },
                { "ignoreDates", IsNullToken(started) && IsNullToken(completed) },
                { "ignorePlaytime", false },
                { "platformPlayed", (string)pick["subPlatform"] ?? targetPlatform }
            };

            var result = await TrpcAdaptiveAsync(updateProc, payload, true, gameId, targetPlatform, minutes, 3);
            AppendDiagnostic($"EDIÇÃO {updateProc} {Trim(result.Input.ToString(Formatting.None), 700)} -> HTTP {result.R.Status} {result.R.Code}: {Trim(result.R.Raw, 600)}");

            // Erro de datas: as horas são o que importa. Tenta de novo só com as datas que o próprio site já tinha
            // (ou sem datas), para nunca deixar de atualizar o tempo por causa de uma data.
            if (!result.R.Ok && result.R.Code == "BAD_REQUEST" &&
                ((result.R.Message ?? "").IndexOf("startedAt", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 (result.R.Message ?? "").IndexOf("completedAt", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                JToken siteStarted = IsNullToken(pick["startedAt"]) ? (JToken)JValue.CreateNull() : pick["startedAt"].DeepClone();
                JToken siteCompleted = IsNullToken(pick["completedAt"]) ? (JToken)JValue.CreateNull() : pick["completedAt"].DeepClone();
                FixDateOrder(ref siteStarted, ref siteCompleted);

                var retry = (JObject)payload.DeepClone();
                retry["startedAt"] = siteStarted;
                retry["completedAt"] = siteCompleted;
                retry["ignoreDates"] = IsNullToken(siteStarted) && IsNullToken(siteCompleted);

                result = await TrpcAdaptiveAsync(updateProc, retry, true, gameId, targetPlatform, minutes, 3);
                AppendDiagnostic($"EDIÇÃO (2ª tentativa, datas do site) {updateProc} {Trim(result.Input.ToString(Formatting.None), 700)} -> HTTP {result.R.Status} {result.R.Code}: {Trim(result.R.Raw, 600)}");
            }

            if (result.R.Status == 401 || result.R.Code == "UNAUTHORIZED") return AuthFailure();
            if (result.R.IsBlocked) return new YgpSyncResult { Success = false, ErrorMessage = result.R.Message };

            if (result.R.Ok)
            {
                _editFailures = 0;
                _logger.Info($"[YGP] Jogo {gameId} atualizado para {minutes}m (site tinha {(existing.HasValue ? existing.Value.ToString("0") : "?")}m) via {updateProc}.");
                await Task.Delay(250);
                return new YgpSyncResult
                {
                    Success = true,
                    YgpGameId = gameId,
                    TotalPlaytimeMinutes = minutes,
                    SiteMinutesBefore = existing.HasValue ? (int?)Math.Round(existing.Value) : null
                };
            }

            attempts.Add(updateProc + ": " + Trim(result.R.Message ?? result.R.Raw, 220));
            _logger.Warn($"[YGP] {updateProc} recusado (HTTP {result.R.Status}, {result.R.Code}): {Trim(result.R.Raw, 500)}");

            // Erro de validação (400) ou limite (429) é problema daquele jogo/momento, não da rota: nunca desativa a
            // rota por isso (foi o que impedia TODOS os jogos de atualizar até reiniciar o Playnite).
            // Só desativa se o site realmente não tem mais a rota.
            if (result.R.ProcedureMissing)
            {
                _logger.Warn("[YGP] O site não tem mais a rota de edição; desativada nesta sessão. Veja o ygp-diagnostico.txt.");
                _skipProcs.Add(updateProc);
            }

            return null;
        }

        private class AdaptiveResult
        {
            public TrpcResult R;
            public JObject Input;
        }

        /// <summary>Chama a rota e, se o site disser quais campos faltam/sobram (zod), ajusta e tenta de novo.</summary>
        private async Task<AdaptiveResult> TrpcAdaptiveAsync(string proc, JObject input, bool mutation, string gameId, string platform, int minutes, int maxRounds)
        {
            var current = (JObject)input.DeepClone();
            var r = await TrpcAsync(proc, current, mutation);

            for (int round = 0; round < maxRounds && !r.Ok && r.Code == "BAD_REQUEST"; round++)
            {
                var adapted = AdaptPayload(current, r.Message, gameId, platform, minutes);
                if (adapted == null) break;

                current = adapted;
                r = await TrpcAsync(proc, current, mutation);
            }

            return new AdaptiveResult { R = r, Input = current };
        }

        private static JObject PickOverlayObject(JToken data)
        {
            var arr = data as JArray;
            if (arr != null) data = arr.FirstOrDefault(x => x is JObject);

            var obj = data as JObject;
            if (obj == null) return null;

            foreach (var name in new[] { "overlay", "edit", "entry", "game", "data" })
            {
                var inner = obj[name] as JObject;
                if (inner != null && obj.Count <= 3) return inner;
            }
            return obj;
        }

        private static double? FindPlaytime(JObject obj)
        {
            foreach (var p in obj.Properties())
            {
                if (p.Name.IndexOf("playtime", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    (p.Value.Type == JTokenType.Integer || p.Value.Type == JTokenType.Float))
                {
                    return (double)p.Value;
                }
            }
            return null;
        }

        private int _editFailures;

        /// <summary>
        /// Jogo que o site já tem: lê o estado atual (sync.getGameEditOverlay), troca só horas/datas e grava
        /// (sync.updateGameEdit). Preserva status e nota que o usuário colocou no site. Retorna null se não conseguir.
        /// </summary>
        private async Task<YgpSyncResult> TryEditViaOverlayAsync(string gameId, string platform, int minutes, YgpPlayDates dates, List<string> attempts)
        {
            const string overlayProc = "sync.getGameEditOverlay";
            const string updateProc = "sync.updateGameEdit";
            if (_skipProcs.Contains(updateProc)) return null;

            // 1) estado atual no site
            JObject overlay = null;
            var inputs = new List<JObject>
            {
                new JObject { { "gameId", gameId } },
                new JObject { { "gameId", gameId }, { "platformPlayed", platform } },
                new JObject { { "id", gameId } }
            };

            foreach (var input in inputs)
            {
                var ar = await TrpcAdaptiveAsync(overlayProc, input, false, gameId, platform, minutes, 2);
                AppendDiagnostic($"OVERLAY {input.ToString(Formatting.None)} -> HTTP {ar.R.Status} {ar.R.Code}: {Trim(ar.R.Raw, 1500)}");

                if (ar.R.Status == 401 || ar.R.Code == "UNAUTHORIZED") return AuthFailure();
                if (ar.R.IsBlocked) return new YgpSyncResult { Success = false, ErrorMessage = ar.R.Message };
                if (ar.R.ProcedureMissing) break;

                if (ar.R.Ok)
                {
                    overlay = PickOverlayObject(ar.R.Data);
                    if (overlay != null) break;
                }
            }

            bool force = dates != null && dates.Force;

            // Nunca diminui as horas que o site já tem (ex.: horas vindas da Steam).
            if (overlay != null && !force)
            {
                var existing = FindPlaytime(overlay);
                if (existing.HasValue && existing.Value >= minutes)
                {
                    return new YgpSyncResult
                    {
                        Success = true,
                        AlreadyOnProfile = true,
                        YgpGameId = gameId,
                        TotalPlaytimeMinutes = minutes,
                        ErrorMessage = "[EXISTS] " + L.T("err.existsHours", existing.Value, minutes)
                    };
                }
            }

            // 2) monta a edição a partir do estado atual
            var payload = overlay != null ? (JObject)overlay.DeepClone() : new JObject();
            bool hasPlaytime = false;
            foreach (var p in payload.Properties().ToList())
            {
                var n = p.Name.ToLowerInvariant();
                bool numericOrEmpty = p.Value.Type == JTokenType.Integer || p.Value.Type == JTokenType.Float || p.Value.Type == JTokenType.Null;

                if (n.Contains("playtime") && numericOrEmpty)
                {
                    payload[p.Name] = minutes;
                    hasPlaytime = true;
                }
                else if (n == "ignoreplaytime")
                {
                    payload[p.Name] = false;
                }
            }
            if (!hasPlaytime) payload["playtimeMinutes"] = minutes;
            if (payload["gameId"] == null) payload["gameId"] = gameId;

            if (dates != null)
            {
                if (dates.Completed.HasValue)
                {
                    payload["completedAt"] = DateToIso(dates.Completed.Value);
                    payload["ignoreDates"] = false;
                }

                var currentStart = payload["startedAt"];
                if (dates.Start.HasValue && (currentStart == null || currentStart.Type == JTokenType.Null))
                {
                    payload["startedAt"] = DateToIso(dates.Start.Value);
                    payload["ignoreDates"] = false;
                }
            }

            // 3) grava (ajustando pelo que o site pedir)
            var result = await TrpcAdaptiveAsync(updateProc, payload, true, gameId, platform, minutes, 5);
            AppendDiagnostic($"EDIÇÃO {updateProc} {Trim(result.Input.ToString(Formatting.None), 700)} -> HTTP {result.R.Status} {result.R.Code}: {Trim(result.R.Raw, 800)}");

            if (result.R.Status == 401 || result.R.Code == "UNAUTHORIZED") return AuthFailure();
            if (result.R.IsBlocked) return new YgpSyncResult { Success = false, ErrorMessage = result.R.Message };

            if (result.R.Ok)
            {
                _editFailures = 0;
                _logger.Info($"[YGP] Jogo {gameId} atualizado para {minutes}m via {updateProc}.");
                return new YgpSyncResult { Success = true, YgpGameId = gameId, TotalPlaytimeMinutes = minutes };
            }

            attempts.Add(updateProc + ": " + Trim(result.R.Message ?? result.R.Raw, 220));
            _logger.Warn($"[YGP] {updateProc} recusado (HTTP {result.R.Status}, {result.R.Code}): {Trim(result.R.Raw, 500)}");

            if (result.R.ProcedureMissing)
            {
                _logger.Warn("[YGP] O site não tem mais a rota de edição; desativada nesta sessão. Veja o ygp-diagnostico.txt.");
                _skipProcs.Add(updateProc);
            }

            return null;
        }

        private JObject BuildLearnedPayload(string gameId, string platform, int minutes)
        {
            if (string.IsNullOrEmpty(_settings.LearnedUpdateTemplate)) return null;

            JObject template;
            try { template = JObject.Parse(_settings.LearnedUpdateTemplate); }
            catch { return null; }

            var clone = (JObject)template.DeepClone();
            foreach (var prop in clone.Properties().ToList())
            {
                var n = prop.Name.ToLowerInvariant();
                var isNumber = prop.Value.Type == JTokenType.Integer || prop.Value.Type == JTokenType.Float;

                if (n == "gameid" || n == "game_id") clone[prop.Name] = gameId;
                else if (n.Contains("playtime") && isNumber) clone[prop.Name] = minutes;
                else if ((n == "platformplayed" || n == "platform") && prop.Value.Type == JTokenType.String) clone[prop.Name] = platform;
            }
            return clone;
        }

        /// <summary>Usa a rota de edição aprendida no modo captura (se houver). Retorna null se não houver ou se falhar.</summary>
        private async Task<YgpSyncResult> TryLearnedUpdateAsync(string gameId, string platform, int minutes, List<string> attempts)
        {
            var proc = _settings.LearnedUpdateProcedure;
            if (string.IsNullOrEmpty(proc)) return null;

            var payload = BuildLearnedPayload(gameId, platform, minutes);
            if (payload == null) return null;

            var r = await TrpcAsync(proc, payload, true);
            if (r.IsAuth) return AuthFailure();
            if (r.Ok)
            {
                _logger.Info($"[YGP] Jogo {gameId} atualizado para {minutes}m via rota aprendida {proc}.");
                return new YgpSyncResult { Success = true, YgpGameId = gameId, TotalPlaytimeMinutes = minutes };
            }

            attempts.Add(proc + " (aprendida): " + Trim(r.Message ?? r.Raw, 200));
            AppendDiagnostic($"ESCRITA aprendida {proc} -> HTTP {r.Status} {r.Code}: {Trim(r.Raw, 600)}");
            _logger.Warn($"[YGP] Rota aprendida {proc} recusada (HTTP {r.Status}, {r.Code}): {Trim(r.Raw, 400)}");
            return null;
        }

        private void RememberWorkingProcedure(string proc)
        {
            if (proc == "unverified.addGame") return;
            var current = string.IsNullOrWhiteSpace(_settings.UpdateProcedures) ? PluginSettings.DefaultUpdateProcedures : _settings.UpdateProcedures;
            var list = current.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (list.Count > 0 && list[0] == proc) return;
            list.Remove(proc);
            list.Insert(0, proc);
            _settings.UpdateProcedures = string.Join(",", list);
            NotifySettingsChanged();
        }

        private IEnumerable<string> DiscoveredWriteProcedures()
        {
            var verbs = new[] { "add", "edit", "update", "upsert", "save", "set", "patch", "modify", "change", "log", "track", "sync", "import", "record" };
            var nouns = new[] { "game", "unverified", "library", "playtime", "entry", "platform", "played", "status" };
            var deny = new[]
            {
                "delete", "remove", "admin", "clear", "reset", "unlink", "disconnect", "logout", "revoke", "report", "block", "ban",
                "password", "email", "account", "setting", "avatar", "banner", "notification", "follow", "comment", "review",
                "screenshot", "guide", "tags", "friend"
            };

            return _discovered
                .Where(kv => kv.Value == "mutation")
                .Select(kv => kv.Key)
                .Where(k =>
                {
                    var l = k.ToLowerInvariant();
                    return nouns.Any(n => l.Contains(n)) && verbs.Any(v => l.Contains(v)) && !deny.Any(d => l.Contains(d));
                })
                .OrderBy(k => k.StartsWith("unverified.", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .Take(12)
                .ToList();
        }

        private IEnumerable<string> UpdateProcedures()
        {
            var raw = _settings.UpdateProcedures;
            if (string.IsNullOrWhiteSpace(raw)) raw = PluginSettings.DefaultUpdateProcedures;
            return raw.Split(new[] { ',', ';', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                      .Select(p => p.Trim())
                      .Where(p => p.Length > 0)
                      .Distinct();
        }

        private static YgpSyncResult AuthFailure()
        {
            return new YgpSyncResult
            {
                Success = false,
                ErrorMessage = "[AUTH] " + L.T("err.authExpired")
            };
        }

        /// <summary>
        /// Envia o tempo total de um jogo. Se knownGameId for informado, pula a busca no catálogo.
        /// </summary>
        public async Task<YgpSyncResult> SyncGameAsync(string gameName, string knownGameId, int totalSeconds, string platform, Func<string, bool> allowWrite = null, YgpPlayDates dates = null)
        {
            if (!IsAuthenticated())
            {
                return AuthFailure();
            }

            var minutes = totalSeconds / 60;
            if (minutes <= 0)
            {
                return new YgpSyncResult { Success = true, YgpGameId = knownGameId, TotalPlaytimeMinutes = 0 };
            }

            _logger.Info($"[YGP] Sincronizando '{gameName}': {minutes}m ({MapPlatform(platform)})");

            await _browserLock.WaitAsync();
            try
            {
                var error = await EnsureBrowserAsync(false);
                if (error != null) return new YgpSyncResult { Success = false, ErrorMessage = error };

                if (!_sessionVerified)
                {
                    var state = await CheckSessionAsync();
                    if (state == SessionState.LoggedOut)
                    {
                        _settings.IsConnected = false;
                        NotifySettingsChanged();
                        return AuthFailure();
                    }
                    _sessionVerified = true;
                }

                var gameId = knownGameId;
                var title = gameName;

                if (string.IsNullOrEmpty(gameId))
                {
                    var resolution = await ResolveGameAsync(gameName);
                    if (resolution.Game == null)
                    {
                        return new YgpSyncResult { Success = false, ErrorMessage = resolution.Error };
                    }
                    gameId = resolution.Game.Id;
                    title = resolution.Game.Title ?? gameName;
                }

                if (allowWrite != null && !allowWrite(gameId))
                {
                    _logger.Info($"[YGP] '{gameName}' ({gameId}) já foi tratado nesta plataforma com mais horas; duplicado ignorado.");
                    return new YgpSyncResult { Success = true, Skipped = true, YgpGameId = gameId, YgpGameTitle = title, TotalPlaytimeMinutes = minutes };
                }

                var result = await WritePlaytimeAsync(gameId, platform, minutes, dates);
                result.YgpGameTitle = title;

                if (result.Success && !result.AlreadyOnProfile)
                {
                    _settings.LastSyncUtc = DateTime.UtcNow;
                    NotifySettingsChanged();
                }

                await Task.Delay(400);
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[YGP] Exceção ao sincronizar '{gameName}': {ex.Message}");
                return new YgpSyncResult { Success = false, ErrorMessage = ex.Message };
            }
            finally
            {
                _browserLock.Release();
            }
        }

        #endregion

        #region Sessão / teste de conexão

        private enum SessionState { LoggedIn, LoggedOut, Unknown }

        /// <summary>Consulta a sessão do better-auth. Deve ser chamado com o navegador pronto e o lock adquirido.</summary>
        private async Task<SessionState> CheckSessionAsync()
        {
            var wrapper = await RunJsAsync(
                "var r=await fetch('/api/auth/get-session',{credentials:'include'});var t=await r.text();return {status:r.status,text:t.substring(0,6000)};",
                20000);

            if (wrapper == null || (bool?)wrapper["ok"] != true)
            {
                LastTestMessage = L.T("test.cantQuery");
                return SessionState.Unknown;
            }

            var status = (int?)wrapper["data"]["status"] ?? 0;
            var text = ((string)wrapper["data"]["text"] ?? "").Trim();

            if (status == 401) return SessionState.LoggedOut;
            if (status != 200) return SessionState.Unknown;
            if (text == "null") return SessionState.LoggedOut;
            if (text.Length == 0) return SessionState.Unknown;

            try
            {
                var obj = JToken.Parse(text) as JObject;
                var user = obj != null ? obj["user"] as JObject : null;
                if (user != null)
                {
                    FillProfileFromUser(user);
                    NotifySettingsChanged();
                    return SessionState.LoggedIn;
                }
                if (obj != null && obj["user"] != null && obj["user"].Type == JTokenType.Null) return SessionState.LoggedOut;
            }
            catch { }

            return SessionState.Unknown;
        }

        /// <summary>
        /// Teste manual de conexão (botão da tela de configurações). Não é mais chamado a cada sincronização.
        /// </summary>
        public async Task<bool> TestConnectionAsync()
        {
            LastTestMessage = "";
            if (!HasStoredCookies())
            {
                LastTestMessage = L.T("test.noAccount");
                return false;
            }

            await _browserLock.WaitAsync();
            try
            {
                var error = await EnsureBrowserAsync(true);
                if (error != null)
                {
                    LastTestMessage = error;
                    return false;
                }

                var state = await CheckSessionAsync();
                switch (state)
                {
                    case SessionState.LoggedIn:
                        _settings.IsConnected = true;
                        _settings.LastAuthTestUtc = DateTime.UtcNow;
                        _sessionVerified = true;
                        NotifySettingsChanged();
                        LastTestMessage = string.IsNullOrWhiteSpace(_settings.Username)
                            ? L.T("test.active")
                            : L.T("test.activeAs", _settings.Username);
                        _logger.Info("[YGP] " + LastTestMessage);
                        return true;

                    case SessionState.LoggedOut:
                        _settings.IsConnected = false;
                        NotifySettingsChanged();
                        LastTestMessage = L.T("test.expired");
                        _logger.Warn("[YGP] " + LastTestMessage);
                        return false;

                    default:
                        _logger.Warn("[YGP] Não foi possível confirmar a sessão (rota de sessão do site não respondeu como esperado). Assumindo sessão válida.");
                        LastTestMessage = L.T("test.unconfirmed");
                        return IsAuthenticated();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[YGP] Erro ao testar conexão: {ex.Message}");
                LastTestMessage = L.T("test.error", ex.Message);
                return false;
            }
            finally
            {
                DisposeViewInternal();
                _browserLock.Release();
            }
        }

        public void Disconnect()
        {
            _logger.Info("[YGP] Desconectando conta...");
            _settings.IsConnected = false;
            _settings.AuthCookiesJson = "";
            _settings.Username = "";
            _settings.UserId = "";
            _settings.AvatarUrl = "";
            _settings.LastSyncUtc = null;
            _sessionVerified = false;

            RunOnUIThread(() =>
            {
                try
                {
                    using (var webView = _playniteApi.WebViews.CreateOffscreenView())
                    {
                        webView.DeleteDomainCookies("yourgamerprofile.com");
                        webView.DeleteDomainCookies(".yourgamerprofile.com");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn($"[YGP] Erro ao limpar cookies: {ex.Message}");
                }
            });
        }

        #endregion
    }

    public class YgpSyncResult
    {
        public bool Success { get; set; }
        public string YgpGameId { get; set; }
        public string YgpGameTitle { get; set; }
        public bool AlreadyOnProfile { get; set; }
        public bool Skipped { get; set; }
        /// <summary>O jogo já existe no site mas a atualização das horas não foi aceita.</summary>
        public bool UpdateFailed { get; set; }
        /// <summary>Minutos que o site tinha antes da atualização (para mostrar quanto subiu).</summary>
        public int? SiteMinutesBefore { get; set; }
        public int TotalPlaytimeMinutes { get; set; }
        public string ErrorMessage { get; set; }
    }
}
