using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace ProjectLibraryDesk;

internal static class PgClientTools
{
    internal static readonly string[] All = ["pg_dump", "pg_restore", "psql"];
    internal static string FileName(string tool) => OperatingSystem.IsWindows() ? tool + ".exe" : tool;

    internal static string Platform => (OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), OperatingSystem.IsLinux(), RuntimeInformation.ProcessArchitecture) switch
    {
        (true, _, _, Architecture.X64) => "win-x64",
        (_, true, _, Architecture.X64) => "osx-x64",
        (_, true, _, Architecture.Arm64) => "osx-arm64",
        (_, _, true, Architecture.X64) => "linux-x64",
        _ => "unsupported",
    };

    internal static async Task<string?> FindAsync(string? configured, int? major, params string[] tools)
    {
        foreach (string folder in Candidates(configured, major))
            if (await SupportsAsync(folder, major, tools)) return folder;
        return null;
    }

    internal static async Task<bool> SupportsAsync(string folder, int? major, string[] tools, CancellationToken token = default)
    {
        foreach (string tool in tools)
        {
            token.ThrowIfCancellationRequested();
            string path = Path.Combine(folder, FileName(tool));
            if (!File.Exists(path)) return false;
            int? actual = await ReadMajorAsync(path, token);
            if (actual is null || (major is not null && major != actual)) return false;
            major ??= actual;
        }
        return true;
    }

    private static IEnumerable<string> Candidates(string? configured, int? major)
    {
        var paths = new List<string>();
        if (!string.IsNullOrWhiteSpace(configured) && Path.IsPathFullyQualified(configured)) paths.Add(configured);
        paths.AddRange(PgToolDownloads.InstalledFolders(major));
        foreach (int version in major.HasValue ? [major.Value] : PgToolDownloads.KnownMajors)
        {
            if (OperatingSystem.IsWindows())
            {
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (Path.IsPathFullyQualified(programFiles)) paths.Add(Path.Combine(programFiles, "PostgreSQL", version.ToString(CultureInfo.InvariantCulture), "bin"));
            }
            else if (OperatingSystem.IsMacOS())
            {
                paths.Add($"/Applications/Postgres.app/Contents/Versions/{version}/bin");
                foreach (string prefix in new[] { "/opt/homebrew", "/usr/local" })
                {
                    paths.Add($"{prefix}/opt/libpq@{version}/bin");
                    paths.Add($"{prefix}/opt/postgresql@{version}/bin");
                }
            }
            else if (OperatingSystem.IsLinux())
            {
                paths.Add($"/usr/lib/postgresql/{version}/bin");
                paths.Add($"/usr/pgsql-{version}/bin");
            }
        }
        if (OperatingSystem.IsMacOS()) paths.AddRange(["/opt/homebrew/opt/libpq/bin", "/usr/local/opt/libpq/bin"]);
        paths.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(part => part.Trim().Trim('"')).Where(Path.IsPathFullyQualified));
        return paths.Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    }

    private static async Task<int?> ReadMajorAsync(string path, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var child = new Process { StartInfo = new ProcessStartInfo(path, "--version")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(path)!,
        } };
        child.StartInfo.Environment["LC_ALL"] = "C";
        try
        {
            if (!child.Start()) return null;
            var output = child.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = child.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await child.WaitForExitAsync(timeout.Token);
                await Task.WhenAll(output, error);
            }
            catch (OperationCanceledException)
            {
                try { if (!child.HasExited) child.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                catch (Win32Exception) { }
                token.ThrowIfCancellationRequested();
                return null;
            }
            Match match = Regex.Match(await output, @"\(PostgreSQL\)\s+(\d+)");
            return child.ExitCode == 0 && int.TryParse(match.Groups[1].Value, out int version) ? version : null;
        }
        catch (Win32Exception) { return null; }
        catch (IOException) { return null; }
    }
}
