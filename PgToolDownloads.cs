using static ProjectLibraryDesk.UiText;
using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProjectLibraryDesk;

internal sealed record PgToolPackage(int Major, string Platform, string Version, string Provider, string Url, string Sha256)
{
    internal string CacheName => $"{Major}-{Version}-{Sha256[..12]}";
}

internal sealed class PgToolDownloads(string? cacheBase = null)
{
    private const long MaxDownload = 600L * 1024 * 1024;
    private const long MaxExtracted = 650L * 1024 * 1024;
    private const int RangeSize = 16 * 1024 * 1024;
    private static readonly PgToolPackage[] Packages = ReadCatalog();
    private static readonly HttpClient Http = new(new HttpClientHandler { UseCookies = false }) { Timeout = Timeout.InfiniteTimeSpan };
    internal static readonly int[] KnownMajors = Packages.Select(package => package.Major).Distinct().OrderDescending().ToArray();

    internal static PgToolPackage? ForCurrentPlatform(int major) =>
        Packages.FirstOrDefault(package => package.Major == major && package.Platform == PgClientTools.Platform);

    private static PgToolPackage[] ReadCatalog()
    {
        using Stream stream = typeof(PgToolDownloads).Assembly.GetManifestResourceStream("PgToolPackages.json")!;
        return JsonSerializer.Deserialize<PgToolPackage[]>(stream) ?? [];
    }

    private static string UserCache => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProjectLibraryDesk", "pg-tools");
    private string PlatformCache => Path.Combine(cacheBase ?? UserCache, PgClientTools.Platform);

    internal static IEnumerable<string> InstalledFolders(int? major) => Packages
        .Where(package => package.Platform == PgClientTools.Platform && (major is null || package.Major == major))
        .OrderByDescending(package => package.Major)
        .Select(package => Path.Combine(UserCache, package.Platform, package.CacheName, "bin"));

    internal async Task<string> DownloadAsync(PgToolPackage package, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (package != ForCurrentPlatform(package.Major)) throw new InvalidOperationException(T("NoVerifiedPackageIsAvailableFor"));
        string target = Path.Combine(PlatformCache, package.CacheName);
        string bin = Path.Combine(target, "bin");
        if (await PgClientTools.SupportsAsync(bin, package.Major, PgClientTools.All, cancellationToken)) return bin;

        Directory.CreateDirectory(PlatformCache);
        using var installLock = new FileStream(Path.Combine(PlatformCache, package.CacheName + ".lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        if (await PgClientTools.SupportsAsync(bin, package.Major, PgClientTools.All, cancellationToken)) return bin;

        string staging = Path.Combine(PlatformCache, ".download-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var uri = new Uri(package.Url);
            string archive = Path.Combine(staging, "package");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromMinutes(15));
            try { await FetchAsync(uri, archive, progress, deadline.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(T("TheDownloadTimedOutTryAgain"));
            }
            progress?.Report(T("VerifyingTheSHAChecksumAndExtracting"));
            string payload = Path.Combine(staging, "verified");
            await StageArchiveAsync(package, archive, payload, cancellationToken);
            if (!await PgClientTools.SupportsAsync(Path.Combine(payload, "bin"), package.Major, PgClientTools.All, cancellationToken))
                throw new InvalidOperationException(T("TheDownloadedToolsCannotRunOn"));

            cancellationToken.ThrowIfCancellationRequested();
            string previous = Path.Combine(staging, "previous");
            if (Directory.Exists(target)) Directory.Move(target, previous);
            try { Directory.Move(payload, target); }
            catch
            {
                if (Directory.Exists(previous)) Directory.Move(previous, target);
                throw;
            }
            return bin;
        }
        finally
        {
            try { Directory.Delete(staging, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task FetchAsync(Uri uri, string output, IProgress<string>? progress, CancellationToken token)
    {
        await using var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        byte[] buffer = new byte[81920];
        long? length = null;
        int reported = -5;
        int retries = 0;
        while (length is null || file.Position < length)
        {
            long offset = file.Position;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Range = new RangeHeaderValue(offset, offset + RangeSize - 1);
                request.Headers.ConnectionClose = true;
                request.Headers.UserAgent.ParseAdd("DaVinciProjectManager/0.4");
                using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();
                if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
                    throw new InvalidDataException(T("ToolDownloadsRequireHTTPS"));

                long? end;
                if (response.StatusCode == HttpStatusCode.PartialContent)
                {
                    var range = response.Content.Headers.ContentRange;
                    if (range is null || range.From != offset || range.To is not long last || range.Length is not long size ||
                        last < offset || last >= size || (length is not null && length != size))
                        throw new InvalidDataException(T("TheServerReturnedAnInvalidPackage"));
                    length = size;
                    end = last + 1;
                }
                else if (response.StatusCode == HttpStatusCode.OK)
                {
                    if (offset > 0)
                    {
                        file.SetLength(0);
                        file.Position = 0;
                        reported = -5;
                    }
                    end = length = response.Content.Headers.ContentLength;
                }
                else throw new InvalidDataException(T("TheServerDidNotReturnA"));

                if (length > MaxDownload) throw new InvalidDataException(T("ThePackageExceedsTheAllowedSize"));
                await using Stream input = await response.Content.ReadAsStreamAsync(token);
                int count;
                while ((count = await input.ReadAsync(buffer, token)) > 0)
                {
                    long next = file.Position + count;
                    if (next > MaxDownload || next > end) throw new InvalidDataException(T("ThePackageExceedsItsDeclaredSize"));
                    await file.WriteAsync(buffer.AsMemory(0, count), token);
                    if (length is > 0)
                    {
                        int percent = (int)(100 * file.Position / length.Value);
                        if (percent >= reported + 5)
                        {
                            reported = percent;
                            progress?.Report(T("DownloadingPostgreSQLTools2", reported));
                        }
                    }
                }
                if (end is not null && file.Position != end) throw new IOException(T("TheDownloadWasInterrupted"));
                if (length is null) return;
                retries = 0;
            }
            catch (Exception error) when (retries < 4 && IsRetryable(error))
            {
                await Task.Delay(TimeSpan.FromSeconds(++retries), token);
            }
        }
    }

    private static bool IsRetryable(Exception error) => error is IOException && error is not InvalidDataException ||
        error is HttpRequestException http && (http.StatusCode is null or HttpStatusCode.TooManyRequests || (int)http.StatusCode >= 500);

    internal static async Task StageArchiveAsync(PgToolPackage package, string archive, string output, CancellationToken token)
    {
        await using (var input = File.OpenRead(archive))
        {
            byte[] digest = await SHA256.HashDataAsync(input, token);
            if (!CryptographicOperations.FixedTimeEquals(digest, Convert.FromHexString(package.Sha256)))
                throw new InvalidDataException(T("ThePackageSHAChecksumDiffersFrom"));
        }
        await Task.Run(() => Extract(package, archive, output, token), token);
    }

    private static void Extract(PgToolPackage package, string source, string output, CancellationToken token)
    {
        Directory.CreateDirectory(Path.Combine(output, "bin"));
        long written = 0;
        bool windows = package.Platform == "win-x64";
        string root = Path.GetFileName(new Uri(package.Url).AbsolutePath)[..^7] + "/";
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        using var file = File.OpenRead(source);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        while (tar.GetNextEntry() is { } entry)
        {
            token.ThrowIfCancellationRequested();
            if (!entry.Name.StartsWith(root, StringComparison.Ordinal)) continue;
            string relative = entry.Name[root.Length..];
            if (!IsClientFile(relative, windows)) continue;
            if (entry.EntryType == TarEntryType.SymbolicLink && relative.StartsWith("lib/", StringComparison.Ordinal) && SafeName(entry.LinkName))
                aliases.Add(relative, "lib/" + entry.LinkName);
            else if (entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile && entry.DataStream is { } data)
                WriteFile(relative, data, entry.Length);
        }
        foreach (var (alias, destination) in aliases)
        {
            string library = destination;
            for (int hops = 0; aliases.TryGetValue(library, out string? next) && hops <= aliases.Count; hops++) library = next;
            string path = Path.Combine(output, library);
            if (!IsClientFile(library, windows) || !File.Exists(path))
                throw new InvalidDataException(T("ThePackageContainsABrokenLibrary"));
            using var data = File.OpenRead(path);
            WriteFile(alias, data, data.Length);
        }

        void WriteFile(string relative, Stream data, long size)
        {
            token.ThrowIfCancellationRequested();
            if (size < 0 || size > MaxExtracted - written) throw new InvalidDataException(T("TheExtractedPackageIsTooLarge"));
            written += size;
            string path = Path.Combine(output, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var item = new FileStream(path, FileMode.CreateNew)) data.CopyTo(item);
            if (!OperatingSystem.IsWindows() && relative.StartsWith("bin/", StringComparison.Ordinal))
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static bool IsClientFile(string relative, bool windows)
    {
        string[] parts = relative.Split('/');
        if (parts.Length == 1) return SafeName(relative) && (relative is "COPYRIGHT" or "LICENSE" or "NOTICE" || relative.EndsWith("licenses.txt", StringComparison.OrdinalIgnoreCase));
        if (parts.Length != 2 || !SafeName(parts[1])) return false;
        return parts[0] == "bin" && (PgClientTools.All.Any(tool => parts[1] == (windows ? tool + ".exe" : tool))
            || windows && parts[1].EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            || !windows && parts[0] == "lib" && parts[1].EndsWith(".dylib", StringComparison.Ordinal);
    }

    private static bool SafeName(string value) => Regex.IsMatch(value, @"\A[A-Za-z0-9][A-Za-z0-9_.+-]*\z", RegexOptions.CultureInvariant);
}
