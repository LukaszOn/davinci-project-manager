using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Npgsql;

namespace ProjectLibraryDesk;

public sealed class PgTools(string? toolsDirectory)
{
    public async Task CreateBackupAsync(PgServer server, string database, string destination)
    {
        int major = (await server.IdentifyAsync(database)).Major;
        string folder = await RequireToolsAsync(major, "pg_dump", "pg_restore");
        string pgDump = Path.Combine(folder, PgClientTools.FileName("pg_dump"));
        string pgRestore = Path.Combine(folder, PgClientTools.FileName("pg_restore"));
        if (File.Exists(destination))
            throw new InvalidOperationException("Plik o tej nazwie już istnieje. Nadaj kopii inną nazwę.");
        string partial = destination + $".{Guid.NewGuid():N}.partial";
        try
        {
            await RunAsync(pgDump, ["--no-password", "--format=custom", "--dbname", $"dbname={QuoteLibpq(database)}", "--file", partial], server, database);
            await RunAsync(pgRestore, ["--list", partial]);
            File.Move(partial, destination);
        }
        finally
        {
            File.Delete(partial);
        }
    }

    public async Task<int> InspectArchiveAsync(string archive)
    {
        string folder = await RequireToolsAsync(null, "pg_restore");
        string listing = await RunAsync(Path.Combine(folder, PgClientTools.FileName("pg_restore")), ["--list", archive]);
        return listing.Split('\n').Count(line => Regex.IsMatch(line, @"^\d+;"));
    }

    public async Task RestoreAsync(PgServer server, string archive, string target)
    {
        int major = (await server.IdentifyAsync(server.Profile.MaintenanceDb)).Major;
        string folder = await RequireToolsAsync(major, "pg_restore");
        string pgRestore = Path.Combine(folder, PgClientTools.FileName("pg_restore"));
        string list = await RunAsync(pgRestore, ["--list", archive]);
        string? listPath = null;
        try
        {
            // Library-specific access is registered for the new database name after a restore.
            // A copied event trigger would refer to access roles and membership on the source server.
            var lines = list.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            string[] filtered = lines.Where(line => !Regex.IsMatch(line,
                @"^\d+;\s+\d+\s+\d+\s+(?:EVENT TRIGGER -|COMMENT - EVENT TRIGGER) resolve_share_new_objects(?: |$)")).ToArray();
            if (lines.Length != filtered.Length)
            {
                listPath = Path.GetTempFileName();
                await File.WriteAllLinesAsync(listPath, filtered);
            }
            await server.CreateRestoreDatabaseAsync(target);
            try
            {
                string[] contents = listPath is null ? [] : ["--use-list", listPath];
                await RunAsync(pgRestore, ["--no-password", "--exit-on-error", "--single-transaction", "--no-owner",
                    "--no-privileges", "--no-tablespaces", "--dbname", $"dbname={QuoteLibpq(target)}", .. contents, archive], server, target);
            }
            catch (InvalidOperationException error)
            {
                throw new InvalidOperationException($"Utworzono bazę {target}, lecz odtwarzanie zakończyło się błędem.\n{error.Message}", error);
            }
        }
        finally
        {
            if (listPath is not null) File.Delete(listPath);
        }
    }

    public async Task InstallAccessModuleAsync(PgServer server, string library)
    {
        int major = (await server.IdentifyAsync(server.Profile.MaintenanceDb)).Major;
        string folder = await RequireToolsAsync(major, "psql");
        string psql = Path.Combine(folder, PgClientTools.FileName("psql"));
        string scripts = Path.Combine(AppContext.BaseDirectory, "sql");
        if (!new[] { "control.sql", "library.sql", "register.sql" }.All(name => File.Exists(Path.Combine(scripts, name))))
            throw new InvalidOperationException("Brakuje plików SQL. Rozpakuj całą paczkę programu.");
        await server.RequireLibraryHbaAsync(library);
        await RunSqlAsync("postgres", "control.sql");
        await RunSqlAsync(library, "library.sql");
        await RunSqlAsync("postgres", "register.sql", "--set", "library=" + library);

        async Task RunSqlAsync(string database, string script, params string[] extra)
        {
            await RunAsync(psql, ["-X", "--no-password", "--quiet", "--set", "ON_ERROR_STOP=1",
                "--dbname", $"dbname={QuoteLibpq(database)}", .. extra, "--file", Path.Combine(scripts, script)], server, database);
        }
    }

    private async Task<string> RequireToolsAsync(int? major, params string[] tools) =>
        await PgClientTools.FindAsync(toolsDirectory, major, tools) ?? throw new PgToolsUnavailableException(major);

    private static string QuoteLibpq(string value) => "'" + value.Replace("\\", "\\\\").Replace("'", "\\'") + "'";

    private static async Task<string> RunAsync(string exe, string[] arguments, PgServer? server = null, string? database = null)
    {
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true,
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        // Discard inherited libpq routing and credential settings. Each job uses its chosen profile.
        foreach (string name in start.Environment.Keys.Where(name => name.StartsWith("PG", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(name);
        if (server is not null)
        {
            var profile = server.Profile;
            start.Environment["PGHOST"] = profile.Host;
            start.Environment["PGPORT"] = profile.Port.ToString(CultureInfo.InvariantCulture);
            start.Environment["PGUSER"] = profile.User;
            start.Environment["PGPASSWORD"] = server.Password;
            start.Environment["PGDATABASE"] = database ?? profile.MaintenanceDb;
            start.Environment["PGSSLMODE"] = profile.SslMode switch
            {
                SslMode.VerifyFull => "verify-full", SslMode.Require => "require", _ => "prefer",
            };
            start.Environment["PGCONNECT_TIMEOUT"] = "8";
            start.Environment["PGCLIENTENCODING"] = "UTF8";
            start.Environment["PGAPPNAME"] = "DaVinci Project Manager";
            if (profile.RootCertificate is not null) start.Environment["PGSSLROOTCERT"] = profile.RootCertificate;
        }
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException("Nie udało się uruchomić narzędzia PostgreSQL.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string diagnostic = (await errors).Trim();
        if (server is not null && !string.IsNullOrEmpty(server.Password))
            diagnostic = diagnostic.Replace(server.Password, "[hasło]", StringComparison.Ordinal);
        if (process.ExitCode != 0)
        {
            if (diagnostic.Length > 1600) diagnostic = diagnostic[..1600];
            throw new InvalidOperationException($"{Path.GetFileName(exe)} zakończył pracę kodem {process.ExitCode}.\n{diagnostic}");
        }
        return await output;
    }
}

internal sealed class PgToolsUnavailableException(int? major) : InvalidOperationException(major is int version
    ? $"Brakuje narzędzi klienta PostgreSQL {version}. Pobierz je lub wskaż folder w widoku Połączenia."
    : "Brakuje pg_restore. Wskaż folder narzędzi lub połącz się z serwerem, aby dobrać zestaw.");
