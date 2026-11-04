using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace YgpPlaytimeSync
{
    /// <summary>Resultado da consulta de nova versão no GitHub.</summary>
    public class UpdateInfo
    {
        public string Version { get; set; }
        public string Url { get; set; }
    }

    /// <summary>
    /// Consulta o GitHub (releases públicos do repositório) e diz se existe uma versão mais nova.
    /// Não envia nenhum dado do usuário; é só um GET anônimo.
    /// </summary>
    public static class UpdateChecker
    {
        private const string ReleasesApi = "https://api.github.com/repos/uWaazy/ygp-playtime-sync/releases?per_page=10";

        /// <summary>Retorna a versão mais nova que a atual, ou null (sem atualização, sem internet ou erro).</summary>
        public static async Task<UpdateInfo> CheckAsync(Version current)
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("YgpPlaytimeSync-UpdateCheck");
                    http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                    var json = await http.GetStringAsync(ReleasesApi).ConfigureAwait(false);
                    var releases = JArray.Parse(json);

                    UpdateInfo best = null;
                    Version bestVersion = current;
                    foreach (var r in releases)
                    {
                        if ((bool?)r["draft"] == true) continue;
                        var tag = (string)r["tag_name"];
                        Version v;
                        if (!TryParseTag(tag, out v)) continue;
                        if (v > bestVersion)
                        {
                            bestVersion = v;
                            best = new UpdateInfo { Version = v.ToString(3), Url = (string)r["html_url"] };
                        }
                    }
                    return best;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>"v0.5.3" ou "0.5.3-beta" -> 0.5.3.</summary>
        public static bool TryParseTag(string tag, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(tag)) return false;
            var t = tag.Trim().TrimStart('v', 'V');
            var dash = t.IndexOf('-');
            if (dash >= 0) t = t.Substring(0, dash);
            return Version.TryParse(t, out version);
        }
    }
}
