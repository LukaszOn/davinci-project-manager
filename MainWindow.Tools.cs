using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace ProjectLibraryDesk;

public partial class MainWindow
{
    private string? _clientToolsFolder;
    private CancellationTokenSource? _toolsDownload;

    private async Task RefreshClientToolsAsync()
    {
        if (_server is not { } server || _identity is not { } identity) return;
        _clientToolsFolder = await PgClientTools.FindAsync(server.Profile.ToolsDirectory, identity.Major, PgClientTools.All);
    }

    private void UpdateClientToolsUi()
    {
        bool connected = _server is not null && _identity is not null;
        ClientToolsCard.IsVisible = connected;
        BackupsToolsNotice.IsVisible = connected && _clientToolsFolder is null;
        if (!connected || _identity is not { } identity) return;

        int major = identity.Major;
        ClientToolsTitle.Text = $"Narzędzia PostgreSQL {major}";
        bool missing = _clientToolsFolder is null;
        ClientToolsDownload.IsVisible = missing;
        CheckClientTools.IsVisible = missing;
        if (!missing)
        {
            ClientToolsMessage.Text = "Gotowe do tworzenia kopii i obsługi modułu kont.";
            ToolTip.SetTip(ClientToolsMessage, _clientToolsFolder);
            return;
        }

        ToolTip.SetTip(ClientToolsMessage, null);
        BackupsToolsMessage.Text = $"Kopie dla tego serwera wymagają narzędzi PostgreSQL {major}.";
        var package = PgToolDownloads.ForCurrentPlatform(major);
        ClientToolsDownload.Content = package is null ? "Instrukcja" : "Pobierz narzędzia";
        ClientToolsMessage.Text = package is null ? ClientToolsGuide(major).Message
            : $"Brakuje zestawu. Pobierz paczkę od {package.Provider} albo wskaż folder w opcjach połączenia.";
    }

    private async void RecheckClientTools(object? sender, RoutedEventArgs e) =>
        await RunAsync("Sprawdzanie narzędzi PostgreSQL…", async () =>
        {
            await RefreshClientToolsAsync();
            return _clientToolsFolder is null ? "Wciąż brakuje odpowiednich narzędzi." : "Wykryto narzędzia PostgreSQL.";
        });

    private void OpenClientTools(object? sender, RoutedEventArgs e) => ShowClientTools();

    private void ShowClientTools()
    {
        OpenView(ConnectionsView, ConnectionsNav);
        if (_identity is null)
            ConnectionOptions.IsChecked = true;
        else
            Dispatcher.UIThread.Post(() => ClientToolsCard.BringIntoView(), DispatcherPriority.Loaded);
    }

    private async void PrepareClientTools(object? sender, RoutedEventArgs e)
    {
        if (_busy || _identity is not { } identity) return;
        int major = identity.Major;
        var package = PgToolDownloads.ForCurrentPlatform(major);
        if (package is null)
        {
            try
            {
                if (!await Launcher.LaunchUriAsync(new Uri(ClientToolsGuide(major).Url)))
                    AddJournal("Nie udało się otworzyć instrukcji. Zajrzyj do sekcji kopii zapasowych w README.");
            }
            catch (Exception error) { Report(error); }
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _toolsDownload = cancellation;
        CancelToolsDownload.IsVisible = true;
        try
        {
            await RunAsync($"Pobieranie narzędzi PostgreSQL {major}…", async () =>
            {
                var progress = new Progress<string>(message =>
                {
                    if (ReferenceEquals(_toolsDownload, cancellation)) Status.Text = message;
                });
                try
                {
                    _clientToolsFolder = await new PgToolDownloads().DownloadAsync(package, progress, cancellation.Token);
                    return $"Narzędzia PostgreSQL {major} są gotowe.";
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    return "Anulowano pobieranie narzędzi.";
                }
            });
        }
        finally
        {
            _toolsDownload = null;
            CancelToolsDownload.IsVisible = false;
        }
    }

    private void CancelClientTools(object? sender, RoutedEventArgs e) => _toolsDownload?.Cancel();

    private static (string Message, string Url) ClientToolsGuide(int major)
    {
        if (OperatingSystem.IsWindows()) return ("Wskaż folder bin z narzędziami tej wersji.", "https://www.postgresql.org/download/windows/");
        if (OperatingSystem.IsMacOS()) return ("Wskaż folder bin z narzędziami tej wersji.", "https://www.postgresql.org/download/macosx/");
        if (!OperatingSystem.IsLinux()) return ("Wskaż folder z narzędziami w opcjach połączenia.", "https://www.postgresql.org/download/");

        if (LinuxDistribution.Value.Contains("ubuntu", StringComparer.OrdinalIgnoreCase))
            return ($"Zainstaluj pakiet postgresql-client-{major} z repozytorium swojej dystrybucji lub wskaż folder bin.", "https://www.postgresql.org/download/linux/ubuntu/");
        if (LinuxDistribution.Value.Contains("debian", StringComparer.OrdinalIgnoreCase))
            return ($"Zainstaluj pakiet postgresql-client-{major} z repozytorium swojej dystrybucji lub wskaż folder bin.", "https://www.postgresql.org/download/linux/debian/");
        if (LinuxDistribution.Value.Any(id => id is "rhel" or "fedora" or "almalinux" or "rocky"))
            return ("Użyj pakietu klienckiego z repozytorium swojej dystrybucji lub wskaż folder bin.", "https://www.postgresql.org/download/linux/redhat/");
        return ("Użyj pakietu klienckiego z repozytorium swojej dystrybucji lub wskaż folder bin.", "https://www.postgresql.org/download/linux/");
    }

    private static readonly Lazy<string[]> LinuxDistribution = new(() =>
    {
        try
        {
            return File.ReadLines("/etc/os-release")
                .Where(line => line.StartsWith("ID=", StringComparison.Ordinal) || line.StartsWith("ID_LIKE=", StringComparison.Ordinal))
                .SelectMany(line => line[(line.IndexOf('=') + 1)..].Trim().Trim('"', '\'').Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .ToArray();
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    });
}
