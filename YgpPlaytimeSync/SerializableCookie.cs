using System;
using Playnite.SDK;

namespace YgpPlaytimeSync
{
    /// <summary>
    /// Serializable representation of an HttpCookie used to persist session
    /// cookies (including __Secure-better-auth.session_data) across Playnite
    /// restarts in the plugin settings JSON blob.
    /// </summary>
    public class SerializableCookie
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public string Domain { get; set; }
        public string Path { get; set; }
        public DateTime? Expires { get; set; }
        public bool HttpOnly { get; set; }
        public bool Secure { get; set; }

        /// <summary>
        /// Creates a serializable cookie from a live HttpCookie captured by the WebView.
        /// </summary>
        public static SerializableCookie FromHttpCookie(HttpCookie cookie)
        {
            if (cookie == null)
                throw new ArgumentNullException(nameof(cookie));

            return new SerializableCookie
            {
                Name = cookie.Name,
                Value = cookie.Value,
                Domain = cookie.Domain,
                Path = cookie.Path,
                Expires = cookie.Expires == default || cookie.Expires == DateTime.MinValue
                    ? (DateTime?)null
                    : cookie.Expires,
                HttpOnly = cookie.HttpOnly,
                Secure = cookie.Secure
            };
        }

        /// <summary>
        /// Rehydrates an HttpCookie from the persisted representation so it can be
        /// re-applied to an offscreen WebView for authenticated tRPC calls.
        /// </summary>
        public HttpCookie ToHttpCookie()
        {
            var cookie = new HttpCookie
            {
                Name = Name,
                Value = Value,
                Domain = Domain,
                Path = Path,
                HttpOnly = HttpOnly,
                Secure = Secure
            };

            if (Expires.HasValue)
                cookie.Expires = Expires.Value;

            return cookie;
        }
    }
}