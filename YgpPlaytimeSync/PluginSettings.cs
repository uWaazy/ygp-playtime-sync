using System;
using System.Collections.Generic;
using System.ComponentModel;
using Playnite.SDK;

namespace YgpPlaytimeSync
{
    /// <summary>
    /// Configurações do plugin Your Gamer Profile Sync.
    /// </summary>
    public class PluginSettings : ISettings, IEditableObject
    {
        private YourGamerProfileSyncPlugin _owner;

        public void AttachOwner(YourGamerProfileSyncPlugin owner) { _owner = owner; }

        // --- Configurações Gerais de Sincronização ---
        public bool AutoSyncEnabled { get; set; } = true;
        public bool SyncOnGameStop { get; set; } = true;
        public bool SyncManualPlaytimeEdits { get; set; } = true;
        public bool SyncTotalPlaytime { get; set; } = true;
        public int SyncIntervalMinutes { get; set; } = 5;

        /// <summary>Idioma da interface: "auto" (segue o Playnite), "pt", "en" ou "es".</summary>
        public string Language { get; set; } = "auto";

        // --- Conclusão automática ---
        public const int AutoCompleteMinDays = 7;
        public const int AutoCompleteMaxDays = 30;

        /// <summary>Quando ligado, jogos sem serem abertos há AutoCompleteDays dias recebem a data de conclusão (= último dia jogado).</summary>
        public bool AutoCompleteEnabled { get; set; } = false;

        /// <summary>Dias sem jogar (de 7 a 30) para considerar o jogo concluído.</summary>
        public int AutoCompleteDays { get; set; } = 14;
        public bool RetryFailedSessions { get; set; } = true;

        // --- Dados da Conta Real (Your Gamer Profile) ---
        public bool IsConnected { get; set; } = false;
        public string Username { get; set; } = "";
        public string UserId { get; set; } = "";
        public string AvatarUrl { get; set; } = "";
        public string AuthCookiesJson { get; set; } = "";
        public DateTime? LastAuthTestUtc { get; set; }
        public DateTime? LastSyncUtc { get; set; }

        // --- Fila e Lote ---
        public int BatchSize { get; set; } = 10;
        public int MaxRetryCount { get; set; } = 10;

        // --- Avançado: procedimentos tRPC usados para ATUALIZAR um jogo que já existe no perfil ---
        public const string DefaultUpdateProcedures = "";
        public string UpdateProcedures { get; set; } = DefaultUpdateProcedures;

        // --- Datas e avisos ---
        /// <summary>Envia o primeiro dia jogado como data de "Início" (cada jogo pode mudar isso no menu YGP: Settings).</summary>
        public bool SendStartDateByDefault { get; set; } = true;

        /// <summary>Mostra uma notificação do Playnite quando um jogo é sincronizado em segundo plano.</summary>
        public bool ShowSyncNotifications { get; set; } = true;

        // --- Filtros de envio ---
        /// <summary>Ignora aplicativos/softwares (gêneros/tags como Utilities, Software, Design &amp; Illustration...).</summary>
        public bool IgnoreSoftware { get; set; } = true;

        /// <summary>Nomes de jogos/aplicativos que nunca devem ser enviados (separados por vírgula).</summary>
        public string IgnoredGameNames { get; set; } = "Wallpaper Engine";

        // --- Rota de edição aprendida no "modo captura" (ver botão Aprender rotas do site) ---
        public string LearnedUpdateProcedure { get; set; } = "";
        public string LearnedUpdateTemplate { get; set; } = "";

        // --- Rota de busca descoberta automaticamente no site (preenchida pelo plugin) ---
        public string SearchProcedure { get; set; } = "sync.searchGames";
        public string SearchInputTemplate { get; set; } = "{\"query\":\"$Q\"}";

        /// <summary>Versão do registro de tempos enviados (permite zerar o registro uma vez após uma correção).</summary>
        public int StoreSchemaVersion { get; set; }
        public string UserSearchProcedure { get; set; } = "";
        public string UserSearchInputTemplate { get; set; } = "";

        // --- Diagnóstico e Logs ---
        public bool DiagnosticLogging { get; set; } = false;
        public bool DebugLogging { get; set; } = false;

        // Implementação da interface ISettings do Playnite
        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            if (SyncIntervalMinutes < 1 || SyncIntervalMinutes > 1440)
            {
                errors.Add(L.T("opt.interval") + " (1-1440)");
            }
            if (AutoCompleteDays < AutoCompleteMinDays || AutoCompleteDays > AutoCompleteMaxDays)
            {
                errors.Add(L.T("opt.autoCompleteDays") + " (" + AutoCompleteMinDays + "-" + AutoCompleteMaxDays + ")");
            }
            if (BatchSize < 1 || BatchSize > 100)
            {
                errors.Add("BatchSize (1-100)");
            }
            if (MaxRetryCount < 0 || MaxRetryCount > 100)
            {
                errors.Add("MaxRetryCount (0-100)");
            }
            return errors.Count == 0;
        }

        public void BeginEdit() { }
        public void EndEdit()
        {
            if (_owner != null) _owner.SaveSettingsAsync(this).GetAwaiter().GetResult();
        }
        public void CancelEdit() { }
    }
}
