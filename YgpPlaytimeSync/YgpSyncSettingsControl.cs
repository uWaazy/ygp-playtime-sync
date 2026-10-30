using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Playnite.SDK;
using Playnite.SDK.Controls;

namespace YgpPlaytimeSync
{
    /// <summary>
    /// Página de configurações do Your Gamer Profile Sync no Playnite (pt-BR, en, es).
    /// </summary>
    public class YgpSyncSettingsControl : PluginUserControl
    {
        private const string SiteUrl = "https://yourgamerprofile.com";

        private readonly PluginSettings _settings;
        private readonly YourGamerProfileSyncPlugin _plugin;
        private readonly StackPanel _mainPanel;

        private TextBlock _connectionStatusValue;
        private TextBlock _accountInfoText;
        private TextBlock _pendingText;
        private TextBlock _syncedText;
        private TextBlock _failedText;
        private TextBlock _summaryText;

        private System.Windows.Shapes.Ellipse _avatarCircle;
        private TextBlock _avatarInitial;
        private System.Windows.Documents.Hyperlink _lastGameLink;
        private System.Windows.Documents.Run _lastGameText;
        private string _lastGameUrl;
        private string _loadedAvatarUrl;

        private bool _buildingLanguageBox;

        public YgpSyncSettingsControl(PluginSettings settings, YourGamerProfileSyncPlugin plugin)
        {
            _settings = settings;
            _plugin = plugin;

            _mainPanel = new StackPanel { Margin = new Thickness(15) };
            Content = new ScrollViewer { Content = _mainPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

            BuildUI();
        }

        private static Thickness ButtonPadding { get { return new Thickness(15, 6, 15, 6); } }

        private void Rebuild()
        {
            _mainPanel.Children.Clear();
            BuildUI();
        }

        private void BuildUI()
        {
            BuildHeader();

            // --- Conta ---
            AddSectionHeader(L.T("sec.account"));

            var statusPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 10) };
            statusPanel.Children.Add(new TextBlock
            {
                Text = L.T("status.label"),
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            _connectionStatusValue = new TextBlock
            {
                Text = GetConnectionStatusText(),
                FontWeight = FontWeights.Bold,
                Foreground = _settings.IsConnected ? Brushes.LimeGreen : Brushes.OrangeRed,
                VerticalAlignment = VerticalAlignment.Center
            };
            statusPanel.Children.Add(_connectionStatusValue);
            _mainPanel.Children.Add(statusPanel);

            _accountInfoText = new TextBlock
            {
                Text = GetAccountDetailsText(),
                FontSize = 11,
                Foreground = Brushes.LightGray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            _mainPanel.Children.Add(_accountInfoText);

            var buttonPanel = new WrapPanel { Margin = new Thickness(0, 5, 0, 15) };

            var loginButton = new Button
            {
                Content = L.T("btn.link"),
                Padding = ButtonPadding,
                Margin = new Thickness(0, 0, 10, 5),
                FontWeight = FontWeights.SemiBold
            };
            loginButton.Click += async (s, e) =>
            {
                loginButton.IsEnabled = false;
                try
                {
                    var success = await _plugin.ConnectCommandAsync();
                    UpdateAccountUI();
                    if (success)
                    {
                        MessageBox.Show(L.T("msg.linked", _settings.Username), L.T("app.name"), MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show(L.T("msg.loginIncomplete"), L.T("app.name"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                finally
                {
                    loginButton.IsEnabled = true;
                }
            };
            buttonPanel.Children.Add(loginButton);

            var cookieButton = new Button
            {
                Content = L.T("btn.linkCookie"),
                Padding = ButtonPadding,
                Margin = new Thickness(0, 0, 10, 5)
            };
            cookieButton.Click += async (s, e) =>
            {
                cookieButton.IsEnabled = false;
                try
                {
                    var linked = await _plugin.ImportSessionCookieCommandAsync();
                    UpdateAccountUI();
                    if (linked)
                    {
                        MessageBox.Show(L.T("msg.linked", _settings.Username), L.T("app.name"), MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                finally
                {
                    cookieButton.IsEnabled = true;
                }
            };
            buttonPanel.Children.Add(cookieButton);

            var testButton = new Button
            {
                Content = L.T("btn.test"),
                Padding = ButtonPadding,
                Margin = new Thickness(0, 0, 10, 5)
            };
            testButton.Click += async (s, e) =>
            {
                testButton.IsEnabled = false;
                try
                {
                    var ok = await _plugin.TestConnectionCommandAsync();
                    UpdateAccountUI();

                    var detail = _plugin.LastConnectionMessage;
                    string msg = ok
                        ? L.T("msg.connOk", detail, _plugin.LastTrackedGameName)
                        : L.T("msg.connFail", string.IsNullOrEmpty(detail) ? L.T("msg.tryLogin") : detail);

                    MessageBox.Show(msg, L.T("title.connDiag"), MessageBoxButton.OK,
                        ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
                }
                finally
                {
                    testButton.IsEnabled = true;
                }
            };
            buttonPanel.Children.Add(testButton);

            var disconnectButton = new Button
            {
                Content = L.T("btn.disconnect"),
                Padding = ButtonPadding,
                Margin = new Thickness(0, 0, 10, 5)
            };
            disconnectButton.Click += async (s, e) =>
            {
                await _plugin.DisconnectCommandAsync();
                UpdateAccountUI();
                MessageBox.Show(L.T("msg.disconnected"), L.T("app.name"), MessageBoxButton.OK, MessageBoxImage.Information);
            };
            buttonPanel.Children.Add(disconnectButton);

            _mainPanel.Children.Add(buttonPanel);
            _mainPanel.Children.Add(new Separator { Margin = new Thickness(0, 5, 0, 15) });

            // --- Sincronização ---
            AddSectionHeader(L.T("sec.sync"));

            AddCheckBox(L.T("opt.autoSync"), L.T("opt.autoSync.desc"),
                _settings.AutoSyncEnabled, v => _settings.AutoSyncEnabled = v);

            AddCheckBox(L.T("opt.syncOnStop"), L.T("opt.syncOnStop.desc"),
                _settings.SyncOnGameStop, v => _settings.SyncOnGameStop = v);

            AddCheckBox(L.T("opt.manualEdits"), L.T("opt.manualEdits.desc"),
                _settings.SyncManualPlaytimeEdits, v => _settings.SyncManualPlaytimeEdits = v);

            AddCheckBox(L.T("opt.sendStart"), L.T("opt.sendStart.desc"),
                _settings.SendStartDateByDefault, v => _settings.SendStartDateByDefault = v);

            AddCheckBox(L.T("opt.notify"), L.T("opt.notify.desc"),
                _settings.ShowSyncNotifications, v => _settings.ShowSyncNotifications = v);

            AddCheckBox(L.T("opt.total"), L.T("opt.total.desc"),
                _settings.SyncTotalPlaytime, v => _settings.SyncTotalPlaytime = v);

            AddIntSlider(L.T("opt.interval"), L.T("opt.interval.desc"),
                _settings.SyncIntervalMinutes, 1, 60, L.T("unit.min"),
                v => _settings.SyncIntervalMinutes = v);

            _mainPanel.Children.Add(new Separator { Margin = new Thickness(0, 10, 0, 15) });

            // --- Conclusão automática ---
            AddSectionHeader(L.T("sec.autoComplete"));

            StackPanel daysPanel = null;
            AddCheckBox(L.T("opt.autoComplete"), L.T("opt.autoComplete.desc"),
                _settings.AutoCompleteEnabled,
                v =>
                {
                    _settings.AutoCompleteEnabled = v;
                    if (daysPanel != null) daysPanel.IsEnabled = v;
                });

            var startDays = Math.Max(PluginSettings.AutoCompleteMinDays, Math.Min(PluginSettings.AutoCompleteMaxDays, _settings.AutoCompleteDays));
            daysPanel = AddIntSlider(L.T("opt.autoCompleteDays"), L.T("opt.autoCompleteDays.desc"),
                startDays, PluginSettings.AutoCompleteMinDays, PluginSettings.AutoCompleteMaxDays, L.T("unit.days"),
                v => _settings.AutoCompleteDays = v);
            daysPanel.IsEnabled = _settings.AutoCompleteEnabled;

            _mainPanel.Children.Add(new Separator { Margin = new Thickness(0, 10, 0, 15) });

            // --- Fila ---
            AddSectionHeader(L.T("sec.queue"));

            _pendingText = new TextBlock { Margin = new Thickness(0, 0, 0, 4) };
            _failedText = new TextBlock { Margin = new Thickness(0, 0, 0, 4) };
            _syncedText = new TextBlock { Margin = new Thickness(0, 0, 0, 10) };
            _summaryText = new TextBlock
            {
                Margin = new Thickness(0, 0, 0, 10),
                FontSize = 11,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap
            };
            _mainPanel.Children.Add(_pendingText);
            _mainPanel.Children.Add(_failedText);
            _mainPanel.Children.Add(_syncedText);
            _mainPanel.Children.Add(_summaryText);
            UpdateQueueCounts();

            var queueButtonPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 15) };

            var syncNowButton = new Button
            {
                Content = L.T("btn.syncNow"),
                Padding = ButtonPadding,
                Margin = new Thickness(0, 0, 10, 5)
            };
            syncNowButton.Click += async (s, e) =>
            {
                syncNowButton.IsEnabled = false;
                try
                {
                    var result = await Task.Run(async () => await _plugin.SyncNowAsync());
                    UpdateQueueCounts();

                    string msg;
                    if (result.AlreadyRunning)
                    {
                        msg = L.T("msg.alreadyRunning");
                    }
                    else if (result.SuccessfulCount == 0 && result.FailedCount == 0 && result.AlreadyOnProfileCount == 0)
                    {
                        msg = !string.IsNullOrEmpty(result.Error) ? result.Error : L.T("msg.queueEmpty");
                    }
                    else
                    {
                        msg = L.T("msg.syncSummary", result.SuccessfulCount, result.AlreadyOnProfileCount, result.FailedCount);
                        foreach (var err in result.Errors.Take(5)) msg += "\n- " + err;
                        if (result.SkippedDueToUnavailable && !string.IsNullOrEmpty(result.Error)) msg += "\n" + result.Error;
                    }

                    MessageBox.Show(
                        msg,
                        L.T("title.sync"),
                        MessageBoxButton.OK,
                        result.SuccessfulCount > 0 ? MessageBoxImage.Information : (result.FailedCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information));
                }
                finally
                {
                    syncNowButton.IsEnabled = true;
                }
            };
            queueButtonPanel.Children.Add(syncNowButton);

            var syncLibraryButton = new Button
            {
                Content = L.T("btn.syncLibrary"),
                Padding = ButtonPadding,
                Margin = new Thickness(0, 0, 10, 5),
                FontWeight = FontWeights.SemiBold
            };
            syncLibraryButton.Click += (s, e) =>
            {
                _plugin.SyncLibraryWithProgress(null);
                UpdateQueueCounts();
            };
            queueButtonPanel.Children.Add(syncLibraryButton);

            var clearQueueButton = new Button
            {
                Content = L.T("btn.clearQueue"),
                Padding = ButtonPadding,
                Margin = new Thickness(0, 0, 10, 5)
            };
            clearQueueButton.Click += (s, e) =>
            {
                if (MessageBox.Show(L.T("msg.clearQueueAsk"), L.T("title.clearQueue"),
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    foreach (var session in _plugin.SessionStorage.GetPendingSessions())
                    {
                        session.SyncStatus = SyncStatus.Ignored;
                        _plugin.SessionStorage.UpdateSessionStatusAsync(session).Wait();
                    }
                    UpdateQueueCounts();
                }
            };
            queueButtonPanel.Children.Add(clearQueueButton);

            var clearDataButton = new Button
            {
                Content = L.T("btn.clearData"),
                Padding = ButtonPadding,
                Margin = new Thickness(0, 0, 10, 5)
            };
            clearDataButton.Click += async (s, e) =>
            {
                if (MessageBox.Show(L.T("msg.clearDataAsk"), L.T("title.clearData"),
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    await _plugin.ClearAllDataAsync();
                    UpdateQueueCounts();
                    MessageBox.Show(L.T("msg.dataCleared"), L.T("app.name"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
            };
            queueButtonPanel.Children.Add(clearDataButton);

            _mainPanel.Children.Add(queueButtonPanel);

            // --- Avançado ---
            _mainPanel.Children.Add(new Separator { Margin = new Thickness(0, 5, 0, 15) });
            AddSectionHeader(L.T("sec.advanced"));

            _mainPanel.Children.Add(new TextBlock
            {
                Text = L.T("adv.procedures"),
                FontSize = 11,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var proceduresBox = new TextBox
            {
                Text = string.IsNullOrWhiteSpace(_settings.UpdateProcedures) ? PluginSettings.DefaultUpdateProcedures : _settings.UpdateProcedures,
                Margin = new Thickness(0, 0, 0, 10)
            };
            proceduresBox.TextChanged += (s, e) => _settings.UpdateProcedures = proceduresBox.Text;
            _mainPanel.Children.Add(proceduresBox);

            var softwareCheck = new CheckBox
            {
                Content = L.T("adv.ignoreSoftware"),
                IsChecked = _settings.IgnoreSoftware,
                Margin = new Thickness(0, 5, 0, 8)
            };
            softwareCheck.Checked += (s, e) => _settings.IgnoreSoftware = true;
            softwareCheck.Unchecked += (s, e) => _settings.IgnoreSoftware = false;
            _mainPanel.Children.Add(softwareCheck);

            _mainPanel.Children.Add(new TextBlock
            {
                Text = L.T("adv.ignoredNames"),
                FontSize = 11,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 0, 0, 4)
            });
            var ignoredBox = new TextBox
            {
                Text = _settings.IgnoredGameNames ?? "",
                Margin = new Thickness(0, 0, 0, 10)
            };
            ignoredBox.TextChanged += (s, e) => _settings.IgnoredGameNames = ignoredBox.Text;
            _mainPanel.Children.Add(ignoredBox);

            var diagPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 15) };

            var learnButton = new Button
            {
                Content = L.T("btn.learn"),
                Padding = ButtonPadding,
                Margin = new Thickness(0, 0, 10, 5)
            };
            learnButton.Click += async (s, e) =>
            {
                var go = MessageBox.Show(L.T("learn.confirm"), L.T("title.learn"), MessageBoxButton.OKCancel, MessageBoxImage.Information);
                if (go != MessageBoxResult.OK) return;

                learnButton.IsEnabled = false;
                try
                {
                    var text = await _plugin.LearnFromSiteAsync();
                    MessageBox.Show(text, L.T("title.learn"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
                finally
                {
                    learnButton.IsEnabled = true;
                }
            };
            diagPanel.Children.Add(learnButton);

            var diagButton = new Button
            {
                Content = L.T("btn.diag"),
                Padding = ButtonPadding,
                Margin = new Thickness(0, 0, 10, 5)
            };
            diagButton.Click += async (s, e) =>
            {
                diagButton.IsEnabled = false;
                try
                {
                    var text = await _plugin.RunDiagnosticsAsync();
                    MessageBox.Show(text, L.T("title.diag"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
                finally
                {
                    diagButton.IsEnabled = true;
                }
            };
            diagPanel.Children.Add(diagButton);

            var folderButton = new Button
            {
                Content = L.T("btn.openFolder"),
                Padding = ButtonPadding,
                Margin = new Thickness(0, 0, 10, 5)
            };
            folderButton.Click += (s, e) =>
            {
                try
                {
                    var dir = _plugin.DataDirectory;
                    if (!string.IsNullOrEmpty(dir))
                    {
                        System.IO.Directory.CreateDirectory(dir);
                        System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\"");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(L.T("msg.openFolderFail", ex.Message), L.T("app.name"));
                }
            };
            diagPanel.Children.Add(folderButton);

            _mainPanel.Children.Add(diagPanel);
        }

        // ------------------------------------------------------------------ cabeçalho e cartão de perfil

        private void BuildHeader()
        {
            var header = new Grid { Margin = new Thickness(0, 0, 0, 15) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Esquerda: logo + título + selo BETA + subtítulo
            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            var logo = LoadLogo();
            if (logo != null)
            {
                left.Children.Add(new Image { Source = logo, Width = 60, Height = 60, Margin = new Thickness(0, 0, 14, 0) });
            }

            var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
            titleRow.Children.Add(new TextBlock
            {
                Text = L.T("app.name"),
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            });
            titleRow.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = L.T("beta"),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.Black
                }
            });

            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(titleRow);
            texts.Children.Add(new TextBlock
            {
                Text = L.T("subtitle"),
                FontSize = 12,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 380,
                Margin = new Thickness(0, 2, 0, 0)
            });
            left.Children.Add(texts);
            Grid.SetColumn(left, 0);
            header.Children.Add(left);

            // Direita: cartão de perfil + idioma
            var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(16, 0, 0, 0) };
            right.Children.Add(BuildCreatorCard());
            right.Children.Add(BuildLanguageRow());
            Grid.SetColumn(right, 1);
            header.Children.Add(right);

            _mainPanel.Children.Add(header);
            RefreshCreatorCard();
            StartCreatorRefresh();
        }

        private Border BuildCreatorCard()
        {
            var accent = Color.FromRgb(0x38, 0xBD, 0xF8);

            var avatarHost = new Grid { Width = 48, Height = 48, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            _avatarCircle = new System.Windows.Shapes.Ellipse
            {
                Width = 48,
                Height = 48,
                Stroke = new SolidColorBrush(accent),
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55))
            };
            _loadedAvatarUrl = null;
            _avatarInitial = new TextBlock
            {
                Text = CreatorProfileService.Username.Substring(0, 1).ToUpperInvariant(),
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            avatarHost.Children.Add(_avatarCircle);
            avatarHost.Children.Add(_avatarInitial);

            var profileLink = new System.Windows.Documents.Hyperlink { Foreground = new SolidColorBrush(accent) };
            profileLink.Inlines.Add(new System.Windows.Documents.Run(L.T("profile.view") + " ↗"));
            profileLink.Click += (s, e) => OpenUrl(CreatorProfileService.ProfileUrl);
            var profileLinkBlock = new TextBlock { FontSize = 11 };
            profileLinkBlock.Inlines.Add(profileLink);

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock
            {
                Text = L.T("profile.creator").ToUpperInvariant(),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(accent)
            });
            info.Children.Add(new TextBlock
            {
                Text = CreatorProfileService.Username,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 220
            });
            info.Children.Add(profileLinkBlock);

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(avatarHost);
            row.Children.Add(info);

            _lastGameLink = new System.Windows.Documents.Hyperlink { Foreground = new SolidColorBrush(accent) };
            _lastGameLink.Click += (s, e) => OpenUrl(_lastGameUrl);
            _lastGameText = new System.Windows.Documents.Run();
            _lastGameLink.Inlines.Add(_lastGameText);

            var lastGameBlock = new TextBlock
            {
                FontSize = 11,
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 280
            };
            lastGameBlock.Inlines.Add(new System.Windows.Documents.Run(L.T("profile.lastGame") + " ") { Foreground = Brushes.Gray });
            lastGameBlock.Inlines.Add(_lastGameLink);

            var content = new StackPanel();
            content.Children.Add(row);
            content.Children.Add(lastGameBlock);

            return new Border
            {
                Child = content,
                Padding = new Thickness(14, 10, 18, 10),
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x70, 0x38, 0xBD, 0xF8)),
                Background = new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x90, 0xA8)),
                HorizontalAlignment = HorizontalAlignment.Right
            };
        }

        private UIElement BuildLanguageRow()
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            panel.Children.Add(new TextBlock
            {
                Text = L.T("lang.label"),
                FontSize = 11,
                Foreground = Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });

            var box = new ComboBox { MinWidth = 190, FontSize = 11 };
            var items = new[]
            {
                new { Tag = L.Auto, Text = L.T("lang.auto") },
                new { Tag = "pt", Text = "Português (Brasil)" },
                new { Tag = "en", Text = "English" },
                new { Tag = "es", Text = "Español" }
            };

            _buildingLanguageBox = true;
            var current = string.IsNullOrWhiteSpace(_settings.Language) ? L.Auto : _settings.Language;
            foreach (var item in items)
            {
                var entry = new ComboBoxItem { Content = item.Text, Tag = item.Tag };
                box.Items.Add(entry);
                if (string.Equals(item.Tag, current, StringComparison.OrdinalIgnoreCase)) box.SelectedItem = entry;
            }
            if (box.SelectedItem == null) box.SelectedIndex = 0;
            _buildingLanguageBox = false;

            box.SelectionChanged += (s, e) =>
            {
                if (_buildingLanguageBox) return;
                var chosen = box.SelectedItem as ComboBoxItem;
                if (chosen == null) return;

                _plugin.ChangeLanguage(chosen.Tag as string);
                Dispatcher.BeginInvoke(new Action(Rebuild));
            };

            panel.Children.Add(box);
            return panel;
        }

        private void OpenUrl(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps) return;

            try { System.Diagnostics.Process.Start(uri.AbsoluteUri); }
            catch (Exception ex) { MessageBox.Show(ex.Message, L.T("app.name")); }
        }

        private void StartCreatorRefresh()
        {
            var service = _plugin.CreatorProfile;
            if (service == null) return;

            service.RefreshAsync(false).ContinueWith(t => Dispatcher.BeginInvoke(new Action(RefreshCreatorCard)));
        }

        private void RefreshCreatorCard()
        {
            if (_avatarCircle == null) return;

            var service = _plugin.CreatorProfile;
            var info = service != null ? service.Current : new CreatorProfileInfo();

            if (!string.IsNullOrWhiteSpace(info.AvatarUrl) && info.AvatarUrl != _loadedAvatarUrl)
            {
                _loadedAvatarUrl = info.AvatarUrl;
                LoadAvatar(info.AvatarUrl);
            }

            if (string.IsNullOrWhiteSpace(info.LastGameName))
            {
                _lastGameUrl = null;
                _lastGameText.Text = info.FetchedUtc.HasValue ? L.T("profile.lastGameNone") : L.T("profile.lastGameLoading");
                _lastGameLink.IsEnabled = false;
                return;
            }

            _lastGameUrl = info.LastGameUrl;
            _lastGameText.Text = info.LastGameName;
            _lastGameLink.IsEnabled = !string.IsNullOrWhiteSpace(info.LastGameUrl);
        }

        private void LoadAvatar(string url)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url)) return;

                url = url.Trim();
                if (url.StartsWith("//", StringComparison.Ordinal)) url = "https:" + url;
                else if (url.StartsWith("/", StringComparison.Ordinal)) url = SiteUrl + url;

                Uri uri;
                if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)) return;

                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 96;
                bitmap.UriSource = uri;
                bitmap.EndInit();

                Action apply = () =>
                {
                    _avatarCircle.Fill = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
                    _avatarInitial.Text = "";
                };

                if (bitmap.IsDownloading)
                {
                    bitmap.DownloadCompleted += (s, e) => apply();
                }
                else
                {
                    apply();
                }
            }
            catch
            {
                // sem avatar: continua mostrando a inicial do nome
            }
        }

        // ------------------------------------------------------------------ atualizações de tela

        private void UpdateAccountUI()
        {
            _connectionStatusValue.Text = GetConnectionStatusText();
            _connectionStatusValue.Foreground = _settings.IsConnected ? Brushes.LimeGreen : Brushes.OrangeRed;
            _accountInfoText.Text = GetAccountDetailsText();
        }

        private void UpdateQueueCounts()
        {
            _pendingText.Text = L.T("q.pending", _plugin.PendingSessionCount);
            if (_failedText != null) _failedText.Text = L.T("q.failed", _plugin.FailedSessionCount);
            _syncedText.Text = L.T("q.synced", _plugin.SyncedSessionCount);
            if (_summaryText != null)
            {
                _summaryText.Text = string.IsNullOrEmpty(_plugin.LastSyncSummary)
                    ? L.T("q.noRun")
                    : L.T("q.lastRun", _plugin.LastSyncSummary);
            }
        }

        private string GetConnectionStatusText()
        {
            if (!_settings.IsConnected) return L.T("status.notConnected");
            return string.IsNullOrWhiteSpace(_settings.Username) ? L.T("status.connected") : L.T("status.connectedAs", _settings.Username);
        }

        private string GetAccountDetailsText()
        {
            if (!_settings.IsConnected) return L.T("acct.hint");

            var lastSync = _settings.LastSyncUtc.HasValue
                ? _settings.LastSyncUtc.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm")
                : L.T("acct.neverSynced");

            return L.T("acct.lastSync", lastSync);
        }

        private static System.Windows.Media.Imaging.BitmapImage LoadLogo()
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(typeof(YgpSyncSettingsControl).Assembly.Location);
                var path = System.IO.Path.Combine(dir, "Resources", "pluginicon.png");
                if (!System.IO.File.Exists(path)) return null;

                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        private void AddSectionHeader(string title)
        {
            _mainPanel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.DeepSkyBlue,
                Margin = new Thickness(0, 5, 0, 8)
            });
        }

        private CheckBox AddCheckBox(string title, string description, bool defaultValue, Action<bool> onChanged)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 6) };

            var checkBox = new CheckBox
            {
                Content = title,
                IsChecked = defaultValue,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 2)
            };
            checkBox.Checked += (s, e) => onChanged(true);
            checkBox.Unchecked += (s, e) => onChanged(false);
            panel.Children.Add(checkBox);

            if (!string.IsNullOrEmpty(description))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = description,
                    FontSize = 11,
                    Foreground = Brushes.Gray,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(20, 0, 0, 0)
                });
            }

            _mainPanel.Children.Add(panel);
            return checkBox;
        }

        private StackPanel AddIntSlider(string title, string description, int defaultValue, int min, int max, string unit, Action<int> onChanged)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 6) };

            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 2)
            });

            if (!string.IsNullOrEmpty(description))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = description,
                    FontSize = 11,
                    Foreground = Brushes.Gray,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 4)
                });
            }

            var sliderPanel = new StackPanel { Orientation = Orientation.Horizontal };

            var slider = new Slider
            {
                Minimum = min,
                Maximum = max,
                Value = defaultValue,
                Width = 220,
                TickFrequency = 1,
                IsSnapToTickEnabled = true
            };

            var valueText = new TextBlock
            {
                Text = defaultValue + " " + unit,
                MinWidth = 70,
                TextAlignment = TextAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };
            slider.ValueChanged += (s, e) =>
            {
                var value = (int)e.NewValue;
                valueText.Text = value + " " + unit;
                onChanged(value);
            };

            sliderPanel.Children.Add(slider);
            sliderPanel.Children.Add(valueText);
            panel.Children.Add(sliderPanel);

            _mainPanel.Children.Add(panel);
            return panel;
        }
    }
}
