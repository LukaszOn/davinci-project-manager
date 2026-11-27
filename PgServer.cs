using static ProjectLibraryDesk.UiText;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using NpgsqlTypes;
using Org.BouncyCastle.Crypto.Generators;

namespace ProjectLibraryDesk;

public sealed record ServerProfile(Guid Id, string Name, string Host, int Port, string User,
    string MaintenanceDb, SslMode SslMode, string? RootCertificate, string? ToolsDirectory);

public sealed record PgLibrary(string Name, string Owner)
{
    public bool Registered { get; init; }
    public uint Oid { get; init; }
    public DateTimeOffset? ServerBackup { get; init; }
}

public sealed record PgIdentity(string Username, int Version, bool CanCreateDatabase, bool IsSuperuser)
{
    public int Major => Version / 10_000;
    public string DisplayVersion => $"{Major}.{Version % 10_000}";
}

public sealed record Member(string Login, string Name, string Role, bool Enabled, string[] Libraries)
{
    public string[] DirectLibraries { get; init; } = Libraries;
    public string[] GrantingGroups { get; init; } = [];
    public string RoleLabel => Role == "administrator" ? T("PanelAdministrator") : T("Collaborator");
    public string State => Enabled ? T("Active") : T("Locked");
    public string LibraryList => string.Join(", ", Libraries);
    public int LibraryCount => Libraries.Length;
    public string Initials => string.Concat(Name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Take(2).Select(word => StringInfo.GetNextTextElement(word))).ToUpperInvariant();
}

public sealed record DirectoryState(bool Available, bool Registered, string Message, IReadOnlyList<Member> Members)
{
    public bool GroupsAvailable { get; init; }
}

public sealed record MemberGroup(string Key, string Name, long Revision, string[] Logins, string[] Libraries);
public sealed record GroupDirectory(IReadOnlyList<MemberGroup> Groups, IReadOnlyList<string> Libraries);

public sealed class ProfileStore(string file)
{
    public static ProfileStore ForCurrentUser() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ProjectLibraryDesk", "profiles.json"));

    public List<ServerProfile> Load() => File.Exists(file)
        ? JsonSerializer.Deserialize<List<ServerProfile>>(File.ReadAllText(file)) ?? [] : [];

    public void Save(List<ServerProfile> profiles)
    {
        string directory = Path.GetDirectoryName(file)!;
        System.IO.Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $"profiles-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, file, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}

public sealed class PgServer(ServerProfile profile, string password)
{
    public ServerProfile Profile { get; } = profile;
    internal string Password { get; } = password;
    private string _accessPrefix = "resolve";

    // Only trusted module SQL and HBA templates pass through this method. User input uses query parameters.
    internal string AccessSql(string template) =>
        template.Replace("resolve_", _accessPrefix + "_", StringComparison.Ordinal);

    private NpgsqlCommand AccessCommand(string template, NpgsqlConnection connection, NpgsqlTransaction? transaction = null) =>
        new(AccessSql(template), connection, transaction);

    private async Task<NpgsqlConnection> OpenRegistryAsync()
    {
        var connection = Connection();
        try
        {
            await connection.OpenAsync();
            using var reader = new StreamReader(typeof(PgServer).Assembly.GetManifestResourceStream("RegistryLayout.sql")!);
            await using var probe = new NpgsqlCommand(await reader.ReadToEndAsync(), connection);
            var prefix = await probe.ExecuteScalarAsync() as string;
            if (prefix is null || !Regex.IsMatch(prefix, @"\A[a-z][a-z0-9_]{0,25}\z", RegexOptions.CultureInvariant))
                throw new InvalidOperationException(T("UnknownRegistryLayout"));
            _accessPrefix = prefix;
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private NpgsqlConnection Connection(string? database = null) => new(new NpgsqlConnectionStringBuilder
    {
        Host = Profile.Host, Port = Profile.Port, Username = Profile.User, Password = Password,
        Database = database ?? Profile.MaintenanceDb, SslMode = Profile.SslMode,
        RootCertificate = Profile.RootCertificate, ApplicationName = "DaVinci Project Manager",
        Timeout = 8, CommandTimeout = 12, Pooling = false,
    }.ConnectionString);

    public async Task PingAsync()
    {
        await using var connection = Connection();
        await connection.OpenAsync();
    }

    public async Task<(PgIdentity Identity, IReadOnlyList<PgLibrary> Libraries)> ConnectAsync()
    {
        await using var connection = await OpenRegistryAsync();
        var identity = await IdentifyAsync(connection);
        var registered = new HashSet<string>(StringComparer.Ordinal);
        await using var capabilities = AccessCommand("""
            SELECT EXISTS (
                SELECT FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'resolve_access' AND c.relname = 'available_libraries'
                  AND has_schema_privilege(n.oid, 'USAGE') AND has_table_privilege(c.oid, 'SELECT')
            ), has_table_privilege('pg_catalog.pg_db_role_setting', 'SELECT')
            """, connection);
        bool canReadRegistry, canReadBackupDates;
        await using (var row = await capabilities.ExecuteReaderAsync())
        {
            await row.ReadAsync();
            canReadRegistry = row.GetBoolean(0);
            canReadBackupDates = row.GetBoolean(1);
        }
        if (canReadRegistry)
        {
            await using var registry = AccessCommand("SELECT name::text FROM resolve_access.available_libraries", connection);
            await using var names = await registry.ExecuteReaderAsync();
            while (await names.ReadAsync()) registered.Add(names.GetString(0));
        }

        var libraries = new List<PgLibrary>();
        string backupDate = canReadBackupDates ? """
            (SELECT split_part(setting, '=', 2)
             FROM pg_catalog.pg_db_role_setting s, unnest(s.setconfig) setting
             WHERE s.setdatabase = d.oid AND s.setrole = 0
               AND split_part(setting, '=', 1) = 'davinci_manager.last_backup_utc')
            """ : "NULL::text";
        await using var command = new NpgsqlCommand($"""
            SELECT datname, pg_get_userbyid(datdba), oid, {backupDate}
            FROM pg_catalog.pg_database d
            WHERE datallowconn AND NOT datistemplate AND has_database_privilege(oid, 'CONNECT')
            ORDER BY datname
            """, connection);
        await using var rows = await command.ExecuteReaderAsync();
        while (await rows.ReadAsync())
        {
            string name = rows.GetString(0);
            string? stamp = rows.IsDBNull(3) ? null : rows.GetString(3);
            libraries.Add(new(name, rows.GetString(1))
            {
                Oid = rows.GetFieldValue<uint>(2), Registered = registered.Contains(name),
                ServerBackup = DateTimeOffset.TryParseExact(stamp, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date) ? date : null,
            });
        }
        return (identity, libraries);
    }

    public async Task<PgIdentity> IdentifyAsync(string database)
    {
        await using var connection = Connection(database);
        await connection.OpenAsync();
        return await IdentifyAsync(connection);
    }

    private static async Task<PgIdentity> IdentifyAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("""
            SELECT current_user, current_setting('server_version_num')::int,
                rolsuper OR rolcreatedb, rolsuper FROM pg_roles WHERE rolname = current_user
            """, connection);
        await using var row = await command.ExecuteReaderAsync();
        if (!await row.ReadAsync())
            throw new InvalidOperationException(T("CouldNotReadThePostgreSQLAccount"));
        return new(row.GetString(0), row.GetInt32(1), row.GetBoolean(2), row.GetBoolean(3));
    }

    public async Task<DirectoryState> ReadDirectoryAsync(string database)
    {
        await using var connection = await OpenRegistryAsync();
        await using var inspection = AccessCommand("""
            SELECT CASE WHEN to_regclass('resolve_access.directory') IS NOT NULL
               AND to_regprocedure('resolve_access.change_member(text,text,text,text,text,text,text,bytea,boolean)') IS NOT NULL
               THEN CASE WHEN to_regclass('resolve_access.member_direct_access') IS NULL THEN 1
                    WHEN has_table_privilege(to_regclass('resolve_access.group_directory'), 'SELECT') THEN 3 ELSE 2 END
               ELSE 0 END
            """, connection);
        try
        {
            int version = (int)(await inspection.ExecuteScalarAsync())!;
            if (version == 0)
                return new(false, false, T("InstallTheSQLModuleOnThis"), []);

            await using var registered = AccessCommand("SELECT EXISTS (SELECT FROM resolve_access.available_libraries WHERE name = @library)", connection);
            registered.Parameters.AddWithValue("library", database);
            if (await registered.ExecuteScalarAsync() is not true)
                return new(true, false, T("RegisterThisLibraryWithTheAccess"), []);

            bool groupsAvailable = version >= 2;
            string directLibraries = groupsAvailable ? "a.libraries" : "d.libraries::text[]";
            string directJoin = groupsAvailable ? "JOIN resolve_access.member_direct_access a USING (username)" : "";
            string grantingGroups = version == 3
                ? "ARRAY(SELECT g.label FROM resolve_access.group_directory g WHERE d.username::text = ANY(g.logins) AND @library = ANY(g.libraries) ORDER BY lower(g.label), g.group_key)"
                : "ARRAY[]::text[]";
            await using var command = AccessCommand($"""
                SELECT d.username::text, d.label, d.account_role, d.enabled, d.libraries::text[], {directLibraries}, {grantingGroups}
                FROM resolve_access.directory d {directJoin}
                ORDER BY lower(d.label), d.username
                """, connection);
            if (version == 3) command.Parameters.AddWithValue("library", database);
            var members = new List<Member>();
            await using var rows = await command.ExecuteReaderAsync();
            while (await rows.ReadAsync())
                members.Add(new(rows.GetString(0), rows.GetString(1), rows.GetString(2), rows.GetBoolean(3), rows.GetFieldValue<string[]>(4))
                {
                    DirectLibraries = rows.GetFieldValue<string[]>(5), GrantingGroups = rows.GetFieldValue<string[]>(6),
                });
            return new(true, true, T("TheseAccountsAreManagedOnThis"), members)
            {
                GroupsAvailable = groupsAvailable,
            };
        }
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            return new(false, false, T("TheSelectedPostgreSQLAccountCannotAccess"), []);
        }
    }

    public async Task InstallGroupsAsync()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "sql", "groups.sql");
        if (!File.Exists(path))
            throw new InvalidOperationException(T("GroupsSqlMissing"));
        await using var connection = await OpenRegistryAsync();
        await using var command = AccessCommand(await File.ReadAllTextAsync(path), connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<GroupDirectory> ReadGroupsAsync()
    {
        await using var connection = await OpenRegistryAsync();
        await using var command = AccessCommand("""
            SELECT group_key, label, revision, logins, libraries
            FROM resolve_access.group_directory ORDER BY lower(label), group_key;
            SELECT name::text FROM resolve_access.available_libraries ORDER BY name;
            """, connection);
        await using var rows = await command.ExecuteReaderAsync();
        var groups = new List<MemberGroup>();
        while (await rows.ReadAsync())
            groups.Add(new(rows.GetString(0), rows.GetString(1), rows.GetInt64(2),
                rows.GetFieldValue<string[]>(3), rows.GetFieldValue<string[]>(4)));
        await rows.NextResultAsync();
        var libraries = new List<string>();
        while (await rows.ReadAsync())
            libraries.Add(rows.GetString(0));
        return new(groups, libraries);
    }

    public async Task ChangeGroupAsync(MemberGroup? group, string? label, string[] logins, string[] libraries)
    {
        await using var connection = await OpenRegistryAsync();
        await using var command = AccessCommand("""
            SELECT resolve_access.change_group(@group, @revision, @label, @logins, @libraries)
            """, connection);
        command.Parameters.AddWithValue("group", NpgsqlDbType.Text, (object?)group?.Key ?? DBNull.Value);
        command.Parameters.AddWithValue("revision", NpgsqlDbType.Bigint, (object?)group?.Revision ?? DBNull.Value);
        command.Parameters.AddWithValue("label", NpgsqlDbType.Text, (object?)label ?? DBNull.Value);
        command.Parameters.AddWithValue("logins", NpgsqlDbType.Array | NpgsqlDbType.Text, logins);
        command.Parameters.AddWithValue("libraries", NpgsqlDbType.Array | NpgsqlDbType.Text, libraries);
        var affected = (string[])(await command.ExecuteScalarAsync())!;
        await CloseRevokedSessionsAsync(connection, affected);
    }

    public async Task SaveMemberAsync(string library, string operation, string login, string nextLogin,
        string label, string role, string? newPassword, bool grantAccess = false)
    {
        if (operation is not ("create" or "edit" or "password"))
            throw new ArgumentOutOfRangeException(nameof(operation));
        if (operation is "create" or "edit")
        {
            ValidateLogin(nextLogin);
            if (label.Trim().Length is < 1 or > 80 || role is not ("collaborator" or "administrator"))
                throw new InvalidOperationException(T("CheckTheDisplayNameAndAccount"));
        }
        CredentialHashes? hashes = null;
        if (operation is "create" or "password")
        {
            if (newPassword is null || newPassword.Length is < 8 or > 128 || newPassword.Contains('\0'))
                throw new InvalidOperationException(T("ThePasswordMustBeCharactersLong"));
            hashes = await Task.Run(() => CredentialHashes.Create(newPassword));
        }

        await using var connection = await OpenRegistryAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = AccessCommand("""
            SELECT resolve_access.change_member(@operation, @login, @next, @label, @role, @scram, @web, NULL, false)
            """, connection, transaction);
        command.Parameters.AddWithValue("operation", operation);
        command.Parameters.AddWithValue("login", login);
        command.Parameters.AddWithValue("next", nextLogin);
        command.Parameters.AddWithValue("label", label.Trim());
        command.Parameters.AddWithValue("role", role);
        command.Parameters.AddWithValue("scram", NpgsqlDbType.Text, (object?)hashes?.Scram ?? DBNull.Value);
        command.Parameters.AddWithValue("web", NpgsqlDbType.Text, (object?)hashes?.Web ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
        if (operation == "create" && grantAccess)
            await ApplyAsync(connection, transaction, "grant", nextLogin, library);
        await transaction.CommitAsync();
    }

    public async Task ChangeAccessAsync(string operation, string login, string library)
    {
        if (operation is not ("grant" or "revoke" or "enable" or "disable"))
            throw new ArgumentOutOfRangeException(nameof(operation));
        await using var connection = await OpenRegistryAsync();
        await ApplyAsync(connection, null, operation, login, library);
        if (operation is "revoke" or "disable")
            await CloseRevokedSessionsAsync(connection, [login]);
    }

    private async Task CloseRevokedSessionsAsync(NpgsqlConnection connection, string[] logins)
    {
        if (logins.Length == 0) return;
        await using var command = AccessCommand("""
            SELECT resolve_access.close_revoked_sessions(login) FROM unnest(@logins) AS affected(login)
            """, connection);
        command.Parameters.AddWithValue("logins", NpgsqlDbType.Array | NpgsqlDbType.Text, logins);
        try { await command.ExecuteNonQueryAsync(); }
        catch (NpgsqlException error)
        {
            throw new InvalidOperationException(T("AccessChangedClosingOpenSessionsCould"), error);
        }
    }

    public async Task DeleteMemberAsync(string login)
    {
        ValidateLogin(login);
        await using var connection = await OpenRegistryAsync();
        await using var command = AccessCommand("""
            SELECT resolve_access.change_member('delete', @login, NULL, NULL, NULL, NULL, NULL, NULL, false)
            """, connection);
        command.Parameters.AddWithValue("login", login);
        await command.ExecuteNonQueryAsync();
    }

    private async Task ApplyAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        string operation, string login, string library)
    {
        await using var command = AccessCommand("SELECT resolve_access.apply_change(@operation, @login, NULL, NULL, @library)", connection, transaction);
        command.Parameters.AddWithValue("operation", operation);
        command.Parameters.AddWithValue("login", login);
        command.Parameters.AddWithValue("library", library);
        await command.ExecuteNonQueryAsync();
    }

    public static bool ValidLibraryName(string name) =>
        Regex.IsMatch(name, @"\A[A-Za-z0-9_]{1,63}\z", RegexOptions.CultureInvariant);

    public async Task CreateRestoreDatabaseAsync(string target)
    {
        if (!ValidLibraryName(target))
            throw new InvalidOperationException(T("LibraryNameFormat"));
        await using var connection = Connection();
        await connection.OpenAsync();
        if (!(await IdentifyAsync(connection)).CanCreateDatabase)
            throw new InvalidOperationException(T("CreatingADatabaseRequiresAPostgreSQL"));
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{target}\" TEMPLATE template0", connection);
        try { await command.ExecuteNonQueryAsync(); }
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.DuplicateDatabase)
        {
            throw new InvalidOperationException(T("ADatabaseWithThatNameAlready"), error);
        }
    }

    public static string LibraryAccessRole(string database)
    {
        if (!ValidLibraryName(database))
            throw new InvalidOperationException(T("TheSetupWizardSupportsDatabaseNames"));
        // Matches resolve_access.register and the PostgreSQL role installed in the library.
        return "resolve_lib_" + Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(database)));
    }

    public async Task RequireLibraryHbaAsync(string database)
    {
        await using var connection = await OpenRegistryAsync();
        string group = "+" + AccessSql(LibraryAccessRole(database));
        PgIdentity identity = await IdentifyAsync(connection);
        if (!identity.IsSuperuser)
            throw new InvalidOperationException(T("APostgreSQLSuperuserMustPerformThe"));
        if (identity.Major < 13)
            throw new InvalidOperationException(T("TheSetupWizardSupportsPostgreSQLOr"));
        if (!string.Equals(Profile.MaintenanceDb, "postgres", StringComparison.Ordinal))
            throw new InvalidOperationException(T("SetTheConnectionDatabaseToPostgres"));
        if (database == Profile.MaintenanceDb)
            throw new InvalidOperationException(T("SelectAProjectLibraryBeforeInstalling"));

        await using var loaded = new NpgsqlCommand("SELECT (pg_stat_file(current_setting('hba_file'))).modification <= pg_conf_load_time()", connection);
        if (await loaded.ExecuteScalarAsync() is not true)
            throw new InvalidOperationException(T("ReloadThePostgreSQLConfigurationAfterEditing"));

        string order = identity.Major >= 16 ? "rule_number" : "line_number";
        await using var command = new NpgsqlCommand($"SELECT type, database, user_name, auth_method, address, error FROM pg_hba_file_rules ORDER BY {order}", connection);
        await using var rows = await command.ExecuteReaderAsync();
        var local = new List<HbaRule>();
        var tcp = new List<HbaRule>();
        while (await rows.ReadAsync())
        {
            if (!rows.IsDBNull(5)) throw new InvalidOperationException(T("PgHbaConfContainsASyntax"));
            var rule = new HbaRule(rows.GetString(0), rows.GetFieldValue<string[]>(1), rows.GetFieldValue<string[]>(2), rows.GetString(3),
                rows.IsDBNull(4) ? null : rows.GetString(4));
            (rule.Type == "local" ? local : tcp).Add(rule);
        }
        if (!ValidHba(local, database, group, "local") || !ValidHba(tcp, database, group, "host"))
            throw new InvalidOperationException(T("AddTheLibraryAccessRulesAnd"));
    }

    private bool ValidHba(List<HbaRule> rules, string database, string group, string generalType)
    {
        int allowed = rules.FindIndex(rule => rule.Databases.SequenceEqual([database]) && rule.Users.SequenceEqual([group])
            && rule.Method == "scram-sha-256" && (rule.Type == generalType || rule.Type == "hostssl"));
        int restricted = rules.FindIndex(rule => rule.Type == generalType && rule.Method == "reject" && (generalType == "local" || rule.Address == "all")
            && rule.Databases.SequenceEqual(["all"]) && rule.Users.Contains(AccessSql("+resolve_panel_members")));
        int open = rules.FindIndex(rule => rule.Users.Contains("all"));
        return allowed >= 0 && restricted > allowed && (open < 0 || restricted < open);
    }

    private sealed record HbaRule(string Type, string[] Databases, string[] Users, string Method, string? Address);

    private void ValidateLogin(string login)
    {
        if (!Regex.IsMatch(login, @"\A[a-z][a-z0-9_]{1,30}\z", RegexOptions.CultureInvariant) ||
            login == "postgres" || login.StartsWith("pg_", StringComparison.Ordinal) ||
            login.StartsWith(AccessSql("resolve_panel_"), StringComparison.Ordinal) || login.StartsWith(AccessSql("resolve_lib_"), StringComparison.Ordinal))
            throw new InvalidOperationException(T("UseAUsernameOfCharactersStarting"));
    }
}

internal sealed record CredentialHashes(string Scram, string Web)
{
    public static CredentialHashes Create(string password)
    {
        byte[] raw = Encoding.UTF8.GetBytes(password);
        byte[] normalized = Encoding.UTF8.GetBytes(password.Normalize(NormalizationForm.FormKC));
        byte[] scramSalt = RandomNumberGenerator.GetBytes(16);
        byte[] salted = Rfc2898DeriveBytes.Pbkdf2(normalized, scramSalt, 4096, HashAlgorithmName.SHA256, 32);
        try
        {
            byte[] stored = SHA256.HashData(HMACSHA256.HashData(salted, "Client Key"u8));
            byte[] server = HMACSHA256.HashData(salted, "Server Key"u8);
            string scram = $"SCRAM-SHA-256$4096:{Convert.ToBase64String(scramSalt)}${Convert.ToBase64String(stored)}:{Convert.ToBase64String(server)}";
            const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            string webSalt = RandomNumberGenerator.GetString(alphabet, 16);
            byte[] webHash = SCrypt.Generate(raw, Encoding.UTF8.GetBytes(webSalt), 32768, 8, 1, 64);
            return new(scram, $"scrypt:32768:8:1${webSalt}${Convert.ToHexStringLower(webHash)}");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(raw);
            CryptographicOperations.ZeroMemory(normalized);
            CryptographicOperations.ZeroMemory(salted);
        }
    }
}
