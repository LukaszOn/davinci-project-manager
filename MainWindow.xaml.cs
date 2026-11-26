using static ProjectLibraryDesk.UiText;
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
    private bool _groupsAvailable;
    private bool _canPrepareAccess;
    private bool _busy;
    private bool _membersLoaded;
    private IReadOnlyList<Member> _directoryMembers = [];

    public MainWindow()
    {
        InitializeComponent();
        LanguagePicker.SelectedIndex = UiText.Language == "pl" ? 1 : 0;
        LanguagePicker.SelectionChanged += ChangeLanguage;
        _connectionMonitor.Tick += CheckConnection;
        Closed += (_, _) =>
        {
            _connectionMonitor.Stop();
            _server = null;
        };
        try { _profiles = _store.Load(); }
        catch (Exception error) { AddJournal(T("CouldNotLoadProfiles") + ErrorMessage(error)); }
        BindProfiles(_profiles.FirstOrDefault()?.Id);
        if (ProfileList.SelectedItem is null) Populate(null);
        OpenView(ConnectionsView, ConnectionsNav);
        RefreshMemberRows();
        UpdateControls();
    }

    private void ChangeLanguage(object? sender, SelectionChangedEventArgs e)
    {
        string language = LanguagePicker.SelectedIndex == 1 ? "pl" : "en";
        try
        {
            UiText.SavePreference(language);
            LanguageRestartHint.IsVisible = language != UiText.Language;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            LanguagePicker.SelectionChanged -= ChangeLanguage;
            LanguagePicker.SelectedIndex = UiText.Language == "pl" ? 1 : 0;
            LanguagePicker.SelectionChanged += ChangeLanguage;
            LanguageRestartHint.IsVisible = false;
            AddJournal(T("CouldNotSaveTheLanguagePreference"));
        }
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
        CertificatePath.Text = _certificate ?? T("DefaultSystemCertificates");
        ToolsPath.Text = _tools ?? T("AutomaticDetection");
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
            throw new InvalidOperationException(T("EnterAHostnameOrIPAddress"));
        if (!int.TryParse(Port.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int port) || port is < 1 or > 65535)
            throw new InvalidOperationException(T("EnterAPortFromTo"));
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(db))
            throw new InvalidOperationException(T("EnterTheUsernameAndTheDatabase"));
        var ssl = Enum.Parse<SslMode>((string?)(SslModeList.SelectedItem as ComboBoxItem)?.Tag ?? nameof(SslMode.VerifyFull));
        return new(_editingId, string.IsNullOrEmpty(name) ? host : name, host, port, user, db, ssl, _certificate, _tools);
    }

    private void ProfileSelected(object sender, SelectionChangedEventArgs e)
    {
        DeleteProfileButton.IsEnabled = ProfileList.SelectedItem is ServerProfile;
        if (ProfileList.SelectedItem is not ServerProfile profile) return;
        Populate(profile);
        if (_server is not null && _server.Profile.Id != profile.Id)
            Status.Text = T("SelectConnectToSwitchTheActive");
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
            Status.Text = T("SavedProfile", profile.Name);
        }
        catch (Exception error) { Report(error); }
    }

    private async void DeleteProfile(object sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is not ServerProfile profile ||
            !await ConfirmAsync(T("RemoveSavedProfileFromThisComputer", profile.Name))) return;
        try
        {
            var updated = _profiles.Where(other => other.Id != profile.Id).ToList();
            _store.Save(updated);
            _profiles = updated;
            BindProfiles(null);
            Populate(null);
            Status.Text = T("LocalProfileRemoved");
        }
        catch (Exception error) { Report(error); }
    }

    private async void SelectCertificate(object sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = T("ServerCACertificate"), FileTypeFilter = [new(T("CACertificates")) { Patterns = ["*.crt", "*.pem", "*.cer"] }],
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
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = T("PostgreSQLToolsBinFolder") });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        _tools = path;
        UpdateProfilePaths();
        Status.Text = _server is null ? T("SaveTheProfileToKeepThis") : T("ReconnectToUseToolsFromThe");
    }

    private void ResetTools(object sender, RoutedEventArgs e)
    {
        _tools = null;
        UpdateProfilePaths();
        Status.Text = _server is null ? T("SaveTheProfileToKeepThis2") : T("ReconnectToUseToolsFromThe2");
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
                return T("ConnectedToAs", Endpoint(profile), state.Identity.Username);
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
        AddJournal(T("Disconnected"), showStatus: false);
    }

    private void ClearConnection()
    {
        _connectionMonitor.Stop();
        _server = null;
        _identity = null;
        _clientToolsFolder = null;
        _memberControls = false;
        _groupsAvailable = false;
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
        ConnectionState.Text = connected ? T("Connected") : profile is not null ? T("Connecting") : T("Disconnected2");
        ActiveLibrary.Text = connected ? (LibraryList.SelectedItem as PgLibrary)?.Name ?? T("SelectALibrary") : "—";
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
                AddJournal(T("LostTheConnectionToReconnectFrom", Endpoint(server.Profile)));
            }
        }
        finally { _checkingConnection = false; }
    }

    private async void Refresh(object sender, RoutedEventArgs e)
    {
        if (_server is not { } server) return;
        string? selected = (LibraryList.SelectedItem as PgLibrary)?.Name;
        await RunAsync(T("RefreshingLibraries"), async () =>
        {
            var state = await server.ConnectAsync();
            _identity = state.Identity;
            await RefreshClientToolsAsync();
            await ShowLibrariesAsync(state.Libraries, selected);
            return T("LibrariesRefreshed");
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
            return T("SelectedLibrary", ActiveLibrary.Text);
        }, showResult: false);
    }

    private async Task ShowDirectoryAsync(string? memberLogin = null)
    {
        if (_server is not { } server) return;
        var library = LibraryList.SelectedItem as PgLibrary;
        UpdateConnectionSummary();
        _memberControls = false;
        _groupsAvailable = false;
        _canPrepareAccess = false;
        SetupButton.IsVisible = false;
        _membersLoaded = false;
        _directoryMembers = [];
        RefreshMemberRows();
        SetupNotice.IsVisible = library is not null;
        if (library is null) return;
        DirectoryMessage.Text = T("CheckingAccountManagementForThisLibrary");
        DirectoryState result;
        try { result = await server.ReadDirectoryAsync(library.Name); }
        catch
        {
            DirectoryMessage.Text = T("CouldNotReadAccountsRefreshThe");
            throw;
        }
        _memberControls = result.Registered;
        _groupsAvailable = result.GroupsAvailable;
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
        MemberStats.Text = _membersLoaded ? T("ActiveAccountsOf", _directoryMembers.Count(member => member.Enabled), _directoryMembers.Count) : T("NoData");
        EmptyDirectory.IsVisible = items.Length == 0;
        EmptyDirectory.Text = !_membersLoaded ? (_server is null ? T("ConnectToAServerFromConnections")
            : LibraryList.SelectedItem is null ? T("SelectALibraryInTheSidebar") : T("TheAccountListIsUnavailable"))
            : query.Length > 0 ? T("NoPeopleMatchYourSearch") : T("AddTheFirstPersonToThe");
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
        bool direct = library is not null && member.DirectLibraries.Contains(library, StringComparer.Ordinal);
        DetailInitials.Text = member.Initials;
        DetailName.Text = member.Name;
        DetailRole.Text = $"{member.Login} · {member.RoleLabel}";
        DetailCurrentLibrary.Text = library ?? T("SelectALibrary");
        DetailAssignment.Text = _groupsAvailable && assigned ? T(direct ? "DirectAccess" : "AccessThroughAGroup") : T(assigned ? "Assigned" : "NoAccess");
        DetailAccessButton.Content = direct ? T(_groupsAvailable ? "RevokeDirectAccess" : "RevokeAccess")
            : T(assigned ? "GrantDirectAccess" : "GrantAccess");
        DetailAccessButton.IsEnabled = _memberControls;
        LockButton.Content = member.Enabled ? T("LockAccount") : T("UnlockAccount");
        LockButton.Classes.Set("danger", member.Enabled);
        string[] others = member.Libraries.Where(name => name != library).ToArray();
        OtherLibraries.IsVisible = others.Length > 0;
        OtherLibraries.Text = T("OtherLibrariesWithAccess", string.Join(", ", others));
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
        await RunAsync(T("SavingAccount"), async () =>
        {
            await server.SaveMemberAsync(library.Name, action, login, result.Login, result.Name, result.Role, result.Password, result.Grant);
            if (action == "create") MemberSearch.Text = "";
            await ShowDirectoryAsync(result.Login);
            return T("SavedAccount", result.Login);
        });
    }

    private async void ChangeSelectedAccess(object sender, RoutedEventArgs e)
    {
        if (MemberList.SelectedItem is Member member && LibraryList.SelectedItem is PgLibrary library)
            await AccessAsync(member.DirectLibraries.Contains(library.Name, StringComparer.Ordinal) ? "revoke" : "grant");
    }
    private async void ToggleMember(object sender, RoutedEventArgs e) => await AccessAsync("toggle");

    private async Task AccessAsync(string change)
    {
        if (!_memberControls || _server is not { } server || LibraryList.SelectedItem is not PgLibrary library || SelectedMember() is not { } member) return;
        string command = change == "toggle" ? (member.Enabled ? "disable" : "enable") : change;
        if (command == "disable" && !await ConfirmAsync(T("LockAccountOnItsSessionsWill", member.Login, Endpoint(server.Profile)))) return;
        if (command == "revoke" && !await ConfirmAsync(T(_groupsAvailable ? "RevokeDirectAccessFor" : "RevokeAccessForToSessionsIn", member.Login, library.Name))) return;
        await RunAsync(T("UpdatingAccess"), async () =>
        {
            await server.ChangeAccessAsync(command, member.Login, library.Name);
            await ShowDirectoryAsync(member.Login);
            return T("SavedAccessFor", member.Login);
        });
    }

    private Member? SelectedMember()
    {
        if (MemberList.SelectedItem is Member member) return member;
        Status.Text = T("SelectAUserFromTheList");
        return null;
    }

    private async void OpenGroups(object sender, RoutedEventArgs e)
    {
        if (_busy || !_memberControls || _server is not { } server) return;
        if (!_groupsAvailable)
        {
            if (_identity?.IsSuperuser != true)
            {
                Status.Text = T("ConnectAsSuperuserToEnableGroups");
                return;
            }
            if (!await ConfirmAsync(T("EnableGroupsOnServer", Endpoint(server.Profile)))) return;
            if (!ReferenceEquals(_server, server)) return;
            await RunAsync(T("EnablingUserGroups"), async () =>
            {
                await server.InstallGroupsAsync();
                await ShowDirectoryAsync();
                return T("UserGroupsEnabled");
            });
            if (!_groupsAvailable) return;
        }

        GroupDirectory? directory = null;
        await RunAsync("", async () =>
        {
            directory = await server.ReadGroupsAsync();
            return T("UserGroupsLoaded");
        }, showResult: false);
        if (directory is null || !ReferenceEquals(_server, server)) return;

        var dialog = new GroupsDialog(directory, _directoryMembers);
        if (await dialog.ShowDialog<GroupInput?>(this) is not { } input) return;
        if (input.Label is null && !await ConfirmAsync(T("DeleteGroupConfirm", input.Group!.Name))) return;
        if (!ReferenceEquals(_server, server)) return;
        await RunAsync(T("SavingGroup"), async () =>
        {
            await server.ChangeGroupAsync(input.Group, input.Label, input.Logins, input.Libraries);
            await ShowDirectoryAsync((MemberList.SelectedItem as Member)?.Login);
            return input.Label is null ? T("GroupDeleted", input.Group!.Name) : T("GroupSaved", input.Label);
        });
    }

    private async void DeleteMember(object sender, RoutedEventArgs e)
    {
        if (!_memberControls || _server is not { } server || SelectedMember() is not { } member) return;
        if (!await ConfirmAsync(T("DeleteAccountOnProjectsWillBe", member.Login, Endpoint(server.Profile)))) return;
        await RunAsync(T("DeletingAccount"), async () =>
        {
            await server.DeleteMemberAsync(member.Login);
            await ShowDirectoryAsync();
            return T("DeletedAccount", member.Login);
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
        await RunAsync(T("SettingUpTheModuleFor", library.Name), async () =>
        {
            await new PgTools(server.Profile.ToolsDirectory).InstallAccessModuleAsync(server, library.Name);
            await ShowDirectoryAsync();
            return T("YouCanNowAddUsersTo", library.Name);
        });
    }

    private async void Backup(object sender, RoutedEventArgs e)
    {
        if (_server is not { } server || LibraryList.SelectedItem is not PgLibrary library) return;
        var invalid = Path.GetInvalidFileNameChars();
        string filename = new(library.Name.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = T("SavePostgreSQLBackup"), DefaultExtension = "backup",
            SuggestedFileName = $"{filename}_{DateTime.Now:yyyyMMdd_HHmmss}.backup", ShowOverwritePrompt = true,
            FileTypeChoices = [new(T("PostgreSQLArchive")) { Patterns = ["*.backup"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        await RunAsync(T("BackupOf", library.Name), async () =>
        {
            await new PgTools(server.Profile.ToolsDirectory).CreateBackupAsync(server, library.Name, path);
            return T("BackupSavedAndArchiveContentsVerified", path);
        });
    }

    private async void InspectArchive(object sender, RoutedEventArgs e)
    {
        string? archive = await SelectArchiveAsync();
        if (archive is null) return;
        await RunAsync(T("InspectingArchive"), async () =>
        {
            int entries = await new PgTools(_server?.Profile.ToolsDirectory ?? _tools).InspectArchiveAsync(archive);
            return T("ArchiveInspectedEntries", entries);
        });
    }

    private async void Restore(object sender, RoutedEventArgs e)
    {
        if (_server is not { } server || _identity?.CanCreateDatabase is not true) return;
        string? archive = await SelectArchiveAsync();
        if (archive is null) return;
        string? requested = await TextDialog.PromptAsync(this, T("RestoreToANewDatabase"), T("NewDatabaseOn", Endpoint(server.Profile)),
            $"resolve_restore_{DateTime.Now:yyyyMMdd_HHmmss}");
        if (string.IsNullOrWhiteSpace(requested)) return;
        string target = requested.Trim();
        if (!await ConfirmAsync(T("RestoreBackupToTheNewDatabase", Path.GetFileName(archive), target, Endpoint(server.Profile)))) return;
        await RunAsync(T("RestoringTo", target), async () =>
        {
            await new PgTools(server.Profile.ToolsDirectory).RestoreAsync(server, archive, target);
            var state = await server.ConnectAsync();
            _identity = state.Identity;
            await ShowLibrariesAsync(state.Libraries, target);
            return T("RestoredOpenAProjectInResolve", target);
        });
    }

    private async Task<string?> SelectArchiveAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = T("SelectAPostgreSQLArchive"), FileTypeFilter = [new(T("PostgreSQLBackups")) { Patterns = ["*.backup", "*.dump", "*.tar"] }],
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
        GroupsButton.IsEnabled = _memberControls;
        DisconnectButton.IsVisible = connected;
        DisconnectButton.IsEnabled = !_busy && connected;
        SetupButton.IsVisible = _canPrepareAccess;
        BackupButton.IsEnabled = connected && LibraryList.SelectedItem is PgLibrary;
        RestoreButton.IsEnabled = _identity?.CanCreateDatabase is true;
        ToolTip.SetTip(BackupButton, BackupButton.IsEnabled ? null : connected ? T("SelectALibraryInTheSidebar") : T("ConnectToAServerToSave"));
        ToolTip.SetTip(RestoreButton, RestoreButton.IsEnabled ? null : connected ? T("ConnectAsAnAccountWithPermission") : T("ConnectToTheDestinationServer"));
        UpdateClientToolsUi();
    }

    private void WindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_busy) return;
        e.Cancel = true;
        Status.Text = T("AnOperationIsInProgressClose");
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
        PostgresException postgres => $"PostgreSQL {postgres.SqlState} · {UiText.SqlError(postgres.MessageText)}",
        NpgsqlException => T("CouldNotConnectToPostgreSQLCheck"),
        _ => error.Message,
    };

    private static string Endpoint(ServerProfile profile) => profile.Host.Contains(':', StringComparison.Ordinal)
        ? $"[{profile.Host}]:{profile.Port}" : $"{profile.Host}:{profile.Port}";
}
