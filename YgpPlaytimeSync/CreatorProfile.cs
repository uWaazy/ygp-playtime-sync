using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using Playnite.SDK.Events;

namespace YgpPlaytimeSync
{
    public class CreatorProfileInfo
    {
        public string AvatarUrl { get; set; }
        public string LastGameName { get; set; }
        public string LastGameUrl { get; set; }
        public DateTime? FetchedUtc { get; set; }
    }

    public class CreatorProfileService
    {
        public const string Username = "uWaazy";
        public const string ProfileUrl = "https://yourgamerprofile.com/uWaazy";

        private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(2);

        private const string ExtractScript =
            "(function(){" +
            "var out={avatar:'',game:'',gameUrl:'',headings:[]};" +
            "var user='uwaazy';" +
            "function abs(u){try{return new URL(u,location.href).href;}catch(e){return '';}}" +
            "var imgs=document.images;" +
            "for(var i=0;i<imgs.length;i++){" +
            "var im=imgs[i];var src=im.currentSrc||im.src||'';var alt=(im.alt||'').toLowerCase();" +
            "if(!src||src.indexOf('data:')===0)continue;" +
            "if(alt.indexOf(user)>=0||/avatar/i.test(src)||/avatar/i.test(String(im.className||''))){out.avatar=abs(src);break;}" +
            "}" +
            "var recentRe=/recent|played|jogad|reciente|jugad|activity|atividade|actividad/i;" +
            "var nodes=document.querySelectorAll('h1,h2,h3,h4,a[href*=\"/games/\"]');" +
            "var inRecent=false;" +
            "for(var j=0;j<nodes.length;j++){" +
            "var n=nodes[j];" +
            "if(n.tagName==='A'){" +
            "if(!inRecent||out.game)continue;" +
            "var m=(n.getAttribute('href')||'').match(/\\/games\\/([^\\/?#]+)\\/?$/);" +
            "if(!m)continue;" +
            "var name=(n.getAttribute('aria-label')||n.title||'').trim();" +
            "if(!name){var im2=n.querySelector('img');if(im2)name=(im2.alt||'').trim();}" +
            "if(!name){var lines=(n.innerText||'').split('\\n');for(var k=0;k<lines.length;k++){if(lines[k].trim()){name=lines[k].trim();break;}}}" +
            "if(!name)continue;" +
            "out.game=name;out.gameUrl=abs(n.getAttribute('href'));" +
            "}else{" +
            "var text=(n.innerText||'').trim();" +
            "if(out.headings.length<15)out.headings.push(text.substring(0,60));" +
            "inRecent=recentRe.test(text);" +
            "}" +
            "}" +
            "return JSON.stringify(out);" +
            "})()";

        private readonly IPlayniteAPI _api;
        private readonly ILogger _logger;
        private readonly string _file;
        private readonly object _lock = new object();
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private CreatorProfileInfo _info = new CreatorProfileInfo();
        private DateTime _lastAttemptUtc = DateTime.MinValue;

        public CreatorProfileService(IPlayniteAPI api, ILogger logger, string dataPath)
        {
            _api = api;
            _logger = logger;
            _file = string.IsNullOrEmpty(dataPath) ? null : Path.Combine(dataPath, "ygp-creator.json");

            try
            {
                if (_file != null && File.Exists(_file))
                {
                    var loaded = JsonConvert.DeserializeObject<CreatorProfileInfo>(File.ReadAllText(_file));
                    if (loaded != null) _info = loaded;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"[YGP] Cache do perfil do criador ilegível: {ex.Message}");
            }
        }

        public CreatorProfileInfo Current
        {
            get
            {
                lock (_lock)
                {
                    return new CreatorProfileInfo
                    {
                        AvatarUrl = _info.AvatarUrl,
                        LastGameName = _info.LastGameName,
                        LastGameUrl = _info.LastGameUrl,
                        FetchedUtc = _info.FetchedUtc
                    };
                }
            }
        }

        public async Task RefreshAsync(bool force)
        {
            if (!await _gate.WaitAsync(0)) return;

            try
            {
                var now = DateTime.UtcNow;
                var cached = Current;
                if (!force)
                {
                    if (cached.FetchedUtc.HasValue && now - cached.FetchedUtc.Value < RefreshInterval) return;
                    if (now - _lastAttemptUtc < RetryInterval) return;
                }

                _lastAttemptUtc = now;
                var fetched = await FetchAsync();
                if (fetched == null) return;

                lock (_lock)
                {
                    if (!string.IsNullOrWhiteSpace(fetched.AvatarUrl)) _info.AvatarUrl = fetched.AvatarUrl;
                    if (!string.IsNullOrWhiteSpace(fetched.LastGameName))
                    {
                        _info.LastGameName = fetched.LastGameName;
                        _info.LastGameUrl = fetched.LastGameUrl;
                    }
                    _info.FetchedUtc = DateTime.UtcNow;
                }

                Save();
            }
            catch (Exception ex)
            {
                _logger.Debug($"[YGP] Falha ao atualizar o perfil do criador: {ex.Message}");
            }
            finally
            {
                _gate.Release();
            }
        }

        private void Save()
        {
            if (_file == null) return;

            try
            {
                string json;
                lock (_lock) { json = JsonConvert.SerializeObject(_info); }
                Directory.CreateDirectory(Path.GetDirectoryName(_file));
                File.WriteAllText(_file, json);
            }
            catch (Exception ex)
            {
                _logger.Debug($"[YGP] Não foi possível salvar {_file}: {ex.Message}");
            }
        }

        private void RunOnUi(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess()) dispatcher.Invoke(action);
            else action();
        }

        private static async Task<bool> NavigateAsync(IWebView view, string url, int timeoutMs)
        {
            var tcs = new TaskCompletionSource<bool>();
            EventHandler<WebViewLoadingChangedEventArgs> handler = (s, e) =>
            {
                if (!e.IsLoading) tcs.TrySetResult(true);
            };

            view.LoadingChanged += handler;
            try
            {
                view.Navigate(url);
                var done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
                return done == tcs.Task;
            }
            finally
            {
                try { view.LoadingChanged -= handler; } catch { }
            }
        }

        private static async Task<string> EvaluateStringAsync(IWebView view, string script)
        {
            dynamic res = await view.EvaluateScriptAsync(script);
            return res?.Result as string;
        }

        private async Task<CreatorProfileInfo> FetchAsync()
        {
            IWebView view = null;
            try
            {
                RunOnUi(() => { view = _api.WebViews.CreateOffscreenView(); });
                if (view == null) return null;

                if (!await NavigateAsync(view, ProfileUrl, 25000)) return null;

                CreatorProfileInfo result = null;
                string lastHeadings = "";

                for (int attempt = 0; attempt < 10; attempt++)
                {
                    await Task.Delay(1500);

                    var title = (await EvaluateStringAsync(view, "document.title") ?? "").ToLowerInvariant();
                    if (title.Contains("just a moment") || title.Contains("checking your browser") || title.Contains("attention required")) continue;

                    var json = await EvaluateStringAsync(view, ExtractScript);
                    if (string.IsNullOrEmpty(json)) continue;

                    var data = JObject.Parse(json);
                    result = new CreatorProfileInfo
                    {
                        AvatarUrl = (string)data["avatar"],
                        LastGameName = (string)data["game"],
                        LastGameUrl = (string)data["gameUrl"]
                    };
                    lastHeadings = string.Join(" | ", data["headings"] ?? new JArray());

                    if (!string.IsNullOrWhiteSpace(result.AvatarUrl) && !string.IsNullOrWhiteSpace(result.LastGameName)) break;
                }

                if (result != null && string.IsNullOrWhiteSpace(result.LastGameName))
                {
                    _logger.Info($"[YGP] Perfil do criador: último jogo não identificado na página. Títulos de seção vistos: {lastHeadings}");
                }

                return result;
            }
            finally
            {
                if (view != null)
                {
                    var v = view;
                    try { RunOnUi(() => v.Dispose()); } catch { }
                }
            }
        }
    }
}
