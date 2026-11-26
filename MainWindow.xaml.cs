using System.Globalization;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Npgsql;

namespace ProjectLibraryDesk;

public partial class MainWindow : Window
{
    private readonly ProfileStore _store = ProfileStore.ForCurrentUser();
    private readonly DispatcherTimer _connectionMonitor = new() { Interval = TimeSpan.FromSeconds(20) };
    private List<ServerProfile> _profiles = [];
    private Guid _editingId;
    private string? _certificate;
    private string? _tools;
    private PgServer? _server;
    private ServerProfile? _connectingProfile;
    private PgIdentity? _identity;
    private bool _checkingConnection;
    private bool _memberControls;
    private bool _canPrepareAccess;
    private bool _busy;
    private bool _membersLoaded;
    private IReadOnlyList<Member> _directoryMembers = [];

    public MainWindow()
    {
        InitializeComponent();
        _connectionMonitor.Tick += CheckConnection;
        Closed += (_, _) =>
        {
            _connectionMonitor.Stop();
            _server = null;
        };
        try { _profiles = _store.Load(); }
        catch (Exception error) { AddJournal("Odczyt profili: " + ErrorMessage(error)); }
        BindProfiles(_profiles.FirstOrDefault()?.Id);
        if (ProfileList.SelectedItem is null) Populate(null);
        OpenView(ConnectionsView, ConnectionsNav);
        RefreshMemberRows();
        UpdateControls();
    }

    private void BindProfiles(Guid? selected)
    {
        ProfileList.ItemsSource = _profiles.OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        ProfileList.SelectedItem = _profiles.FirstOrDefault(profile => profile.Id == selected);
    }

    private void Populate(ServerProfile? profile)
    {
        _editingId = profile?.Id ?? Guid.NewGuid();
        ProfileName.Text = profile?.Name ?? "";
        Host.Text = profile?.Host ?? "";
        Port.Text = (profile?.Port ?? 5432).ToString(CultureInfo.InvariantCulture);
        Login.Text = profile?.User ?? "postgres";
        LoginPassword.Text = null;
        MaintenanceDb.Text = profile?.MaintenanceDb ?? "postgres";
        SslModeList.SelectedIndex = (profile?.SslMode ?? SslMode.VerifyFull) switch
        {
            SslMode.Require => 1,
            SslMode.Prefer => 2,
            _ => 0,
        };
        _certificate = profile?.RootCertificate;
        _tools = profile?.ToolsDirectory;
        UpdateProfilePaths();
    }

    private void UpdateProfilePaths()
    {
        CertificatePath.Text = _certificate ?? "Domyślne certyfikaty systemu";
        ToolsPath.Text = _tools ?? "Wykrywanie automatyczne";
        ResetCertificateButton.IsVisible = _certificate is not null;
        ResetToolsButton.IsVisible = _tools is not null;
    }

    private ServerProfile CurrentProfile()
    {
        string host = Host.Text?.Trim().TrimStart('[').TrimEnd(']') ?? "";
        string name = ProfileName.Text?.Trim() ?? "";
        string user = Login.Text?.Trim() ?? "";
        string db = MaintenanceDb.Text?.Trim() ?? "";
        if (Uri.CheckHostName(host) == UriHostNameType.Unknown || host.Any(char.IsWhiteSpace))
            throw new InvalidOperationException("Wpisz nazwę hosta lub adres IP bez portu i protokołu.");
        if (!int.TryParse(Port.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int port) || port is < 1 or > 65535)
            throw new InvalidOperationException("Wpisz port od 1 do 65535.");
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(db))
            throw new InvalidOperationException("Wpisz login i bazę do nawiązania połączenia.");
        var ssl = Enum.Parse<SslMode>((string?)(SslModeList.SelectedItem as ComboBoxItem)?.Tag ?? nameof(SslMode.VerifyFull));
        return new(_editingId, string.IsNullOrEmpty(name) ? host : name, host, port, user, db, ssl, _certificate, _tools);
    }

    private void ProfileSelected(object sender, SelectionChangedEventArgs e)
    {
        DeleteProfileButton.IsEnabled = ProfileList.SelectedItem is ServerProfile;
        if (ProfileList.SelectedItem is not ServerProfile profile) return;
        Populate(profile);
        if (_server is not null && _server.Profile.Id != profile.Id)
            Status.Text = "Kliknij Połącz, aby przełączyć aktywny serwer.";
    }

    private void NewProfile(object sender, RoutedEventArgs e)
    {
        ProfileList.SelectedIndex = -1;
        Populate(null);
        ProfileName.Focus();
    }

    private void SaveProfile(object sender, RoutedEventArgs e)
    {
        try
        {
            var profile = CurrentProfile();
            var updated = _profiles.Where(other => other.Id != profile.Id).Append(profile).ToList();
            string? password = LoginPassword.Text;
            _store.Save(updated);
            _profiles = updated;
            BindProfiles(profile.Id);
            LoginPassword.Text = password;
            Status.Text = $"Zapisano profil {profile.Name}.";
        }
        catch (Exception error) { Report(error); }
    }

    private async void DeleteProfile(object sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is not ServerProfile profile ||
            !await ConfirmAsync($"Usunąć zapisany profil {profile.Name} z tego komputera?")) return;
        try
        {
            var updated = _profiles.Where(other => other.Id != profile.Id).ToList();
            _store.Save(updated);
            _profiles = updated;
            BindProfiles(null);
            Populate(null);
            Status.Text = "Usunięto lokalny profil.";
        }
        catch (Exception error) { Report(error); }
    }

    private async void SelectCertificate(object sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Certyfikat CA serwera", FileTypeFilter = [new("Certyfikaty CA") { Patterns = ["*.crt", "*.pem", "*.cer"] }],
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        _certificate = path;
        UpdateProfilePaths();
    }

    private void ResetCertificate(object sender, RoutedEventArgs e)
    {
        _certificate = null;
        UpdateProfilePaths();
    }

    private async void SelectTools(object sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Katalog bin narzędzi PostgreSQL" });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        _tools = path;
        UpdateProfilePaths();
        Status.Text = _server is null ? "Zapisz profil, aby zachować katalog." : "Połącz ponownie, aby użyć narzędzi z nowego katalogu.";
    }

    private void ResetTools(object sender, RoutedEventArgs e)
    {
        _tools = null;
        UpdateProfilePaths();
        Status.Text = _server is null ? "Zapisz profil, aby zachować ustawienie." : "Połącz ponownie, aby użyć narzędzi z systemowej ścieżki PATH.";
    }

    private async void Connect(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        ServerProfile profile;
        try { profile = CurrentProfile(); }
        catch (Exception error) { Report(error); return; }
        var next = new PgServer(profile, LoginPassword.Text ?? "");
        ClearConnection();
        _connectingProfile = profile;
        UpdateConnectionSummary();
        try
        {
            await RunAsync("", async () =>
            {
                var state = await next.ConnectAsync();
                _server = next;
                _identity = state.Identity;
                _connectionMonitor.Start();
                LoginPassword.Text = null;
                await RefreshClientToolsAsync();
                await ShowLibrariesAsync(state.Libraries);
                OpenView(UsersView, UsersNav);
                return $"Połączono z {Endpoint(profile)} jako {state.Identity.Username}.";
            }, showResult: false);
        }
        finally
        {
            _connectingProfile = null;
            UpdateConnectionSummary();
        }
    }

    private void Disconnect(object sender, RoutedEventArgs e)
    {
        ClearConnection();
        LoginPassword.Text = null;
        OpenView(ConnectionsView, ConnectionsNav);
        UpdateControls();
        Status.Text = null;
        AddJournal("Rozłączono.", showStatus: false);
    }

    private void ClearConnection()
    {
        _connectionMonitor.Stop();
        _server = null;
        _identity = null;
        _clientToolsFolder = null;
        _memberControls = false;
        _canPrepareAccess = false;
        _membersLoaded = false;
        _directoryMembers = [];
        LibraryList.ItemsSource = null;
        RefreshMemberRows();
        UpdateConnectionSummary();
        SetupNotice.IsVisible = false;
    }

    private void UpdateConnectionSummary()
    {
        ServerProfile? profile = _server?.Profile ?? _connectingProfile;
        bool connected = _server is not null;
        ConnectionBadge.Classes.Set("online", connected);
        ConnectionBadge.Classes.Set("connecting", !connected && profile is not null);
        ConnectionBadge.Classes.Set("offline", profile is null);
        ConnectionState.Text = connected ? "Połączono" : profile is not null ? "Łączenie…" : "Rozłączono";
        ActiveLibrary.Text = connected ? (LibraryList.SelectedItem as PgLibrary)?.Name ?? "Wybierz bibliotekę" : "—";
        ActiveEndpoint.IsVisible = profile is not null;
        if (profile is not null)
        {
            string endpoint = Endpoint(profile);
            ActiveEndpoint.Text = profile.Name == profile.Host || profile.Name == endpoint ? endpoint : $"{endpoint}  ·  {profile.Name}";
        }
    }

    private async void CheckConnection(object? sender, EventArgs e)
    {
        if (_busy || _checkingConnection || _server is not { } server) return;
        _checkingConnection = true;
        try { await server.PingAsync(); }
        catch (NpgsqlException)
        {
            if (!_busy && ReferenceEquals(_server, server))
            {
                ClearConnection();
                UpdateControls();
                AddJournal($"Utracono połączenie z {Endpoint(server.Profile)}. Połącz się ponownie w widoku Połączenia.");
            }
        }
        finally { _checkingConnection = false; }
    }

    private async void Refresh(object sender, RoutedEventArgs e)
    {
        if (_server is not { } server) return;
        string? selected = (LibraryList.SelectedItem as PgLibrary)?.Name;
        await RunAsync("Odświeżanie bibliotek", async () =>
        {
            var state = await server.ConnectAsync();
            _identity = state.Identity;
            await RefreshClientToolsAsync();
            await ShowLibrariesAsync(state.Libraries, selected);
            return "Odświeżono biblioteki.";
        });
    }

    private async Task ShowLibrariesAsync(IReadOnlyList<PgLibrary> libraries, string? selection = null)
    {
        LibraryList.ItemsSource = libraries;
        LibraryList.SelectedItem = libraries.FirstOrDefault(library => library.Name == selection)
            ?? libraries.FirstOrDefault(library => !string.Equals(library.Name, _server?.Profile.MaintenanceDb, StringComparison.Ordinal))
            ?? libraries.FirstOrDefault();
        await ShowDirectoryAsync();
    }

    private async void LibrarySelected(object sender, SelectionChangedEventArgs e)
    {
        if (_busy || _server is null) return;
        UpdateConnectionSummary();
        await RunAsync("", async () =>
        {
            await ShowDirectoryAsync();
            return $"Wybrano bibliotekę {ActiveLibrary.Text}.";
        }, showResult: false);
    }

    private async Task ShowDirectoryAsync(string? memberLogin = null)
    {
        if (_server is not { } server) return;
        var library = LibraryList.SelectedItem as PgLibrary;
        UpdateConnectionSummary();
        _memberControls = false;
        _canPrepareAccess = false;
        SetupButton.IsVisible = false;
        _membersLoaded = false;
        _directoryMembers = [];
        RefreshMemberRows();
        SetupNotice.IsVisible = library is not null;
        if (library is null) return;
        DirectoryMessage.Text = "Sprawdzam obsługę kont tej biblioteki.";
        DirectoryState result;
        try { result = await server.ReadDirectoryAsync(library.Name); }
        catch
        {
            DirectoryMessage.Text = "Nie udało się odczytać listy kont. Odśwież listę bibliotek, aby spróbować ponownie.";
            throw;
        }
        _memberControls = result.Registered;
        _canPrepareAccess = !result.Registered && _identity?.IsSuperuser == true && library.Name != server.Profile.MaintenanceDb;
        _membersLoaded = result.Registered;
        _directoryMembers = result.Members;
        DirectoryMessage.Text = result.Message;
        SetupNotice.IsVisible = !_memberControls;
        RefreshMemberRows(memberLogin);
    }

    private void OpenUsers(object sender, RoutedEventArgs e) => OpenView(UsersView, UsersNav);
    private void OpenConnections(object sender, RoutedEventArgs e) => OpenView(ConnectionsView, ConnectionsNav);
    private void OpenBackups(object sender, RoutedEventArgs e) => OpenView(BackupsView, BackupsNav);
    private void OpenJournal(object sender, RoutedEventArgs e) => OpenView(JournalView, JournalNav);

    private void OpenView(Control view, Button navigation)
    {
        foreach (Control page in new Control[] { UsersView, ConnectionsView, BackupsView, JournalView })
            page.IsVisible = page == view;
        foreach (Button button in new[] { UsersNav, ConnectionsNav, BackupsNav, JournalNav })
            button.Classes.Set("selected", button == navigation);
        Status.IsVisible = view != JournalView;
    }

    private void LayoutChanged(object? sender, SizeChangedEventArgs e)
    {
        if (BackupCards is null) return;
        double width = e.NewSize.Width;
        bool compact = width < 900;
        int sidebar = compact ? 172 : 224;
        int padding = compact ? 15 : 26;
        double workspace = width - sidebar - 2 * padding;
        ShellGrid.ColumnDefinitions[0].Width = new GridLength(sidebar);
        MainWorkspace.Margin = new Avalonia.Thickness(padding, compact ? 14 : 20, padding, 12);
        BrandLockup.ColumnDefinitions[0].Width = new GridLength(compact ? 31 : 38);
        BrandName.FontSize = compact ? 13 : 15;

        bool stackedUsers = workspace < 880;
        Grid.SetColumn(DetailsCard, stackedUsers ? 0 : 1);
        Grid.SetRow(DetailsCard, stackedUsers ? 1 : 0);
        UserCards.ColumnDefinitions[1].Width = stackedUsers ? new GridLength(0) : new GridLength(0.94, GridUnitType.Star);
        UserCards.ColumnSpacing = stackedUsers ? 0 : 16;

        bool stackedFields = workspace < 650;
        SetFieldLayout(ProfileFields, AddressFields, stackedFields);
        SetFieldLayout(CredentialsFields, PasswordField, stackedFields);
        SetFieldLayout(OptionFields, TlsField, stackedFields);
        Grid.SetRow(CertificateField, stackedFields ? 2 : 1);
        Grid.SetColumn(ToolsField, stackedFields ? 0 : 1);
        Grid.SetRow(ToolsField, stackedFields ? 3 : 1);
        int optionRows = stackedFields ? 4 : 2;
        if (OptionFields.RowDefinitions.Count != optionRows)
            OptionFields.RowDefinitions = new RowDefinitions(stackedFields ? "Auto,Auto,Auto,Auto" : "Auto,Auto");
        OptionFields.RowSpacing = 13;

        bool stackedBackups = workspace < 760;
        Grid.SetColumn(ArchiveCard, stackedBackups ? 0 : 1);
        Grid.SetRow(ArchiveCard, stackedBackups ? 1 : 0);
        Grid.SetColumn(RestoreCard, stackedBackups ? 0 : 2);
        Grid.SetRow(RestoreCard, stackedBackups ? 2 : 0);
        BackupCards.ColumnDefinitions[1].Width = BackupCards.ColumnDefinitions[2].Width = stackedBackups ? new GridLength(0) : GridLength.Star;
        BackupCards.ColumnSpacing = stackedBackups ? 0 : 13;
        BackupCards.RowSpacing = stackedBackups ? 12 : 0;
    }

    private static void SetFieldLayout(Grid grid, Control second, bool stacked)
    {
        Grid.SetColumn(second, stacked ? 0 : 1);
        Grid.SetRow(second, stacked ? 1 : 0);
        grid.ColumnDefinitions[1].Width = stacked ? new GridLength(0) : GridLength.Star;
        grid.ColumnSpacing = stacked ? 0 : 14;
        grid.RowSpacing = stacked ? 13 : 0;
    }

    private void ShowHelp(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button) ToolTip.SetIsOpen(button, true);
    }

    private void SearchMembers(object? sender, TextChangedEventArgs e)
    {
        if (MemberList is not null) RefreshMemberRows((MemberList.SelectedItem as Member)?.Login);
    }

    private void RefreshMemberRows(string? preferred = null)
    {
        string query = MemberSearch.Text?.Trim() ?? "";
        var items = _directoryMembers.Where(member => member.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || member.Login.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        MemberList.ItemsSource = items;
        MemberList.SelectedItem = items.FirstOrDefault(member => member.Login == preferred) ?? items.FirstOrDefault();
        MemberStats.Text = _membersLoaded ? $"Aktywne konta: {_directoryMembers.Count(member => member.Enabled)} z {_directoryMembers.Count}" : "Brak danych";
        EmptyDirectory.IsVisible = items.Length == 0;
        EmptyDirectory.Text = !_membersLoaded ? (_server is null ? "Połącz się z serwerem w widoku Połączenia."
            : LibraryList.SelectedItem is null ? "Wybierz bibliotekę w pasku bocznym." : "Lista kont jest niedostępna.")
            : query.Length > 0 ? "Brak osób pasujących do wyszukiwania." : "Dodaj pierwszą osobę do biblioteki.";
        MemberList.IsVisible = items.Length > 0;
        UpdateMemberDetail();
    }

    private void MemberSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (MemberDetail is not null) UpdateMemberDetail();
    }

    private void UpdateMemberDetail()
    {
        var member = MemberList.SelectedItem as Member;
        MemberDetail.IsVisible = member is not null;
        NoMemberDetail.IsVisible = member is null;
        if (member is null) return;
        string? library = (LibraryList.SelectedItem as PgLibrary)?.Name;
        bool assigned = library is not null && member.Libraries.Contains(library, StringComparer.Ordinal);
        DetailInitials.Text = member.Initials;
        DetailName.Text = member.Name;
        DetailRole.Text = $"{member.Login} · {member.RoleLabel}";
        DetailCurrentLibrary.Text = library ?? "Wybierz bibliotekę";
        DetailAssignment.Text = assigned ? "Przypisano" : "Bez dostępu";
        DetailAccessButton.Content = assigned ? "Odbierz dostęp" : "Nadaj dostęp";
        DetailAccessButton.IsEnabled = _memberControls;
        LockButton.Content = member.Enabled ? "Zablokuj konto" : "Odblokuj konto";
        LockButton.Classes.Set("danger", member.Enabled);
        string[] others = member.Libraries.Where(name => name != library).ToArray();
        OtherLibraries.IsVisible = others.Length > 0;
        OtherLibraries.Text = $"Pozostałe biblioteki z dostępem · {string.Join(", ", others)}";
    }

    private async void AddMember(object sender, RoutedEventArgs e) => await EditMemberAsync("create");
    private async void EditMember(object sender, RoutedEventArgs e) => await EditMemberAsync("edit");
    private async void ResetPassword(object sender, RoutedEventArgs e) => await EditMemberAsync("password");

    private async Task EditMemberAsync(string action)
    {
        if (!_memberControls || _server is not { } server || LibraryList.SelectedItem is not PgLibrary library) return;
        Member? person = action == "create" ? null : SelectedMember();
        if (action != "create" && person is null) return;
        var form = new MemberDialog(action, person, library.Name);
        if (await form.ShowDialog<MemberInput?>(this) is not { } result) return;
        string login = person?.Login ?? result.Login;
        await RunAsync("Zapisywanie konta", async () =>
        {
            await server.SaveMemberAsync(library.Name, action, login, result.Login, result.Name, result.Role, result.Password, result.Grant);
            if (action == "create") MemberSearch.Text = "";
            await ShowDirectoryAsync(result.Login);
            return $"Zapisano konto {result.Login}.";
        });
    }

    private async void ChangeSelectedAccess(object sender, RoutedEventArgs e)
    {
        if (MemberList.SelectedItem is Member member && LibraryList.SelectedItem is PgLibrary library)
            await AccessAsync(member.Libraries.Contains(library.Name, StringComparer.Ordinal) ? "revoke" : "grant");
    }
    private async void ToggleMember(object sender, RoutedEventArgs e) => await AccessAsync("toggle");

    private async Task AccessAsync(string change)
    {
        if (!_memberControls || _server is not { } server || LibraryList.SelectedItem is not PgLibrary library || SelectedMember() is not { } member) return;
        string command = change == "toggle" ? (member.Enabled ? "disable" : "enable") : change;
        if (command == "disable" && !await ConfirmAsync($"Zablokować konto {member.Login} na serwerze {Endpoint(server.Profile)}? Jego sesje zostaną zamknięte.")) return;
        if (command == "revoke" && !await ConfirmAsync($"Odebrać {member.Login} dostęp do {library.Name}? Sesje w tej bibliotece zostaną zamknięte.")) return;
        await RunAsync("Zmiana dostępu", async () =>
        {
            await server.ChangeAccessAsync(command, member.Login, library.Name);
            await ShowDirectoryAsync(member.Login);
            return $"Zapisano dostęp dla {member.Login}.";
        });
    }

    private Member? SelectedMember()
    {
        if (MemberList.SelectedItem is Member member) return member;
        Status.Text = "Wybierz użytkownika na liście.";
        return null;
    }

    private async void DeleteMember(object sender, RoutedEventArgs e)
    {
        if (!_memberControls || _server is not { } server || SelectedMember() is not { } member) return;
        if (!await ConfirmAsync($"Usunąć konto {member.Login} na serwerze {Endpoint(server.Profile)}? Projekty zostaną zachowane.")) return;
        await RunAsync("Usuwanie konta", async () =>
        {
            await server.DeleteMemberAsync(member.Login);
            await ShowDirectoryAsync();
            return $"Usunięto konto {member.Login}.";
        });
    }

    private async void SetupAccess(object sender, RoutedEventArgs e)
    {
        if (!_canPrepareAccess || _server is not { } server || LibraryList.SelectedItem is not PgLibrary library) return;
        string group;
        try { group = PgServer.LibraryAccessRole(library.Name); }
        catch (InvalidOperationException error) { Report(error); return; }
        string hostRule = server.Profile.SslMode == SslMode.Prefer ? "host" : "hostssl";
        string accessRules = $"local \"{library.Name}\" +{group} scram-sha-256\n{hostRule} \"{library.Name}\" +{group} all scram-sha-256";
        if (!await SetupDialog.ShowAsync(this, library.Name, Endpoint(server.Profile), accessRules)) return;
        await RunAsync($"Przygotowywanie modułu dla {library.Name}", async () =>
        {
            await new PgTools(server.Profile.ToolsDirectory).InstallAccessModuleAsync(server, library.Name);
            await ShowDirectoryAsync();
            return $"Możesz teraz dodawać użytkowników do {library.Name}.";
        });
    }

    private async void Backup(object sender, RoutedEventArgs e)
    {
        if (_server is not { } server || LibraryList.SelectedItem is not PgLibrary library) return;
        var invalid = Path.GetInvalidFileNameChars();
        string filename = new(library.Name.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Zapisz kopię PostgreSQL", DefaultExtension = "backup",
            SuggestedFileName = $"{filename}_{DateTime.Now:yyyyMMdd_HHmmss}.backup", ShowOverwritePrompt = true,
            FileTypeChoices = [new("Archiwum PostgreSQL") { Patterns = ["*.backup"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        await RunAsync($"Kopia {library.Name}", async () =>
        {
            await new PgTools(server.Profile.ToolsDirectory).CreateBackupAsync(server, library.Name, path);
            return $"Zapisano kopię i odczytano spis archiwum.\n{path}";
        });
    }

    private async void InspectArchive(object sender, RoutedEventArgs e)
    {
        string? archive = await SelectArchiveAsync();
        if (archive is null) return;
        await RunAsync("Sprawdzanie archiwum", async () =>
        {
            int entries = await new PgTools(_server?.Profile.ToolsDirectory ?? _tools).InspectArchiveAsync(archive);
            return $"Odczytano spis archiwum. Liczba wpisów: {entries}.";
        });
    }

    private async void Restore(object sender, RoutedEventArgs e)
    {
        if (_server is not { } server || _identity?.CanCreateDatabase is not true) return;
        string? archive = await SelectArchiveAsync();
        if (archive is null) return;
        string? requested = await TextDialog.PromptAsync(this, "Odtwórz do nowej bazy", $"Nowa baza na {Endpoint(server.Profile)}",
            $"resolve_restore_{DateTime.Now:yyyyMMdd_HHmmss}");
        if (string.IsNullOrWhiteSpace(requested)) return;
        string target = requested.Trim();
        if (!await ConfirmAsync($"Odtworzyć kopię {Path.GetFileName(archive)} do nowej bazy {target} na {Endpoint(server.Profile)}?")) return;
        await RunAsync($"Odtwarzanie do {target}", async () =>
        {
            await new PgTools(server.Profile.ToolsDirectory).RestoreAsync(server, archive, target);
            var state = await server.ConnectAsync();
            _identity = state.Identity;
            await ShowLibrariesAsync(state.Libraries, target);
            return $"Odtworzono {target}. Otwórz projekt w Resolve, aby sprawdzić bibliotekę.";
        });
    }

    private async Task<string?> SelectArchiveAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Wybierz archiwum PostgreSQL", FileTypeFilter = [new("Kopie PostgreSQL") { Patterns = ["*.backup", "*.dump", "*.tar"] }],
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    private async Task RunAsync(string title, Func<Task<string>> operation, bool showResult = true)
    {
        if (_busy) return;
        _busy = true;
        Status.Text = title;
        UpdateControls();
        try { AddJournal(await operation(), showResult); }
        catch (PgToolsUnavailableException error)
        {
            Report(error);
            ShowClientTools();
        }
        catch (Exception error) { Report(error); }
        finally
        {
            _busy = false;
            UpdateControls();
        }
    }

    private void UpdateControls()
    {
        Progress.IsVisible = _busy;
        bool connected = _server is not null;
        LibraryList.IsEnabled = !_busy && connected;
        RefreshButton.IsVisible = connected;
        RefreshButton.IsEnabled = !_busy && connected;
        WorkArea.IsEnabled = !_busy;
        MemberButtons.IsEnabled = _memberControls;
        AddMemberButton.IsEnabled = _memberControls;
        DisconnectButton.IsVisible = connected;
        DisconnectButton.IsEnabled = !_busy && connected;
        SetupButton.IsVisible = _canPrepareAccess;
        BackupButton.IsEnabled = connected && LibraryList.SelectedItem is PgLibrary;
        RestoreButton.IsEnabled = _identity?.CanCreateDatabase is true;
        ToolTip.SetTip(BackupButton, BackupButton.IsEnabled ? null : connected ? "Wybierz bibliotekę w pasku bocznym." : "Połącz się z serwerem, aby zapisać kopię.");
        ToolTip.SetTip(RestoreButton, RestoreButton.IsEnabled ? null : connected ? "Połącz się jako użytkownik z uprawnieniem do tworzenia baz." : "Połącz się z serwerem docelowym.");
        UpdateClientToolsUi();
    }

    private void WindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_busy) return;
        e.Cancel = true;
        Status.Text = "Operacja trwa. Zamknij aplikację po zakończeniu.";
    }

    private Task<bool> ConfirmAsync(string text) => TextDialog.ConfirmAsync(this, text);

    private void Report(Exception error)
    {
        string message = ErrorMessage(error);
        AddJournal(message);
    }

    private void AddJournal(string message, bool showStatus = true)
    {
        if (showStatus) Status.Text = message.Split('\n')[0];
        Journal.Text += $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
        Journal.CaretIndex = Journal.Text?.Length ?? 0;
    }

    private static string ErrorMessage(Exception error) => error switch
    {
        PostgresException postgres => $"PostgreSQL {postgres.SqlState} · {postgres.MessageText}",
        NpgsqlException => "Połączenie z PostgreSQL nie powiodło się. Sprawdź adres, dane logowania i ustawienia TLS.",
        _ => error.Message,
    };

    private static string Endpoint(ServerProfile profile) => profile.Host.Contains(':', StringComparison.Ordinal)
        ? $"[{profile.Host}]:{profile.Port}" : $"{profile.Host}:{profile.Port}";
}
