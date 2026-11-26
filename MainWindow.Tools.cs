using static ProjectLibraryDesk.UiText;
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
        ClientToolsTitle.Text = T("PostgreSQLTools", major);
        bool missing = _clientToolsFolder is null;
        ClientToolsDownload.IsVisible = missing;
        CheckClientTools.IsVisible = missing;
        if (!missing)
        {
            ClientToolsMessage.Text = T("ReadyForBackupsAndAccountManagement");
            ToolTip.SetTip(ClientToolsMessage, _clientToolsFolder);
            return;
        }

        ToolTip.SetTip(ClientToolsMessage, null);
        BackupsToolsMessage.Text = T("BackupsOnThisServerRequirePostgreSQL", major);
        var package = PgToolDownloads.ForCurrentPlatform(major);
        ClientToolsDownload.Content = package is null ? T("Instructions") : T("DownloadTools");
        ClientToolsMessage.Text = package is null ? ClientToolsGuide(major).Message
            : T("TheToolsAreMissingDownloadThem", package.Provider);
    }

    private async void RecheckClientTools(object? sender, RoutedEventArgs e) =>
        await RunAsync(T("CheckingPostgreSQLTools"), async () =>
        {
            await RefreshClientToolsAsync();
            return _clientToolsFolder is null ? T("TheRequiredToolsAreStillMissing") : T("PostgreSQLToolsDetected");
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
                    AddJournal(T("CouldNotOpenTheInstructionsSee"));
            }
            catch (Exception error) { Report(error); }
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _toolsDownload = cancellation;
        CancelToolsDownload.IsVisible = true;
        try
        {
            await RunAsync(T("DownloadingPostgreSQLTools", major), async () =>
            {
                var progress = new Progress<string>(message =>
                {
                    if (ReferenceEquals(_toolsDownload, cancellation)) Status.Text = message;
                });
                try
                {
                    _clientToolsFolder = await new PgToolDownloads().DownloadAsync(package, progress, cancellation.Token);
                    return T("PostgreSQLToolsAreReady", major);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    return T("ToolDownloadCanceled");
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
        if (OperatingSystem.IsWindows()) return (T("SelectTheBinFolderForTools"), "https://www.postgresql.org/download/windows/");
        if (OperatingSystem.IsMacOS()) return (T("SelectTheBinFolderForTools"), "https://www.postgresql.org/download/macosx/");
        if (!OperatingSystem.IsLinux()) return (T("SelectTheToolsFolderInConnection"), "https://www.postgresql.org/download/");

        if (LinuxDistribution.Value.Contains("ubuntu", StringComparer.OrdinalIgnoreCase))
            return (T("InstallPostgresqlClientFromYourDistribution", major), "https://www.postgresql.org/download/linux/ubuntu/");
        if (LinuxDistribution.Value.Contains("debian", StringComparer.OrdinalIgnoreCase))
            return (T("InstallPostgresqlClientFromYourDistribution", major), "https://www.postgresql.org/download/linux/debian/");
        if (LinuxDistribution.Value.Any(id => id is "rhel" or "fedora" or "almalinux" or "rocky"))
            return (T("InstallTheClientPackageFromYour"), "https://www.postgresql.org/download/linux/redhat/");
        return (T("InstallTheClientPackageFromYour"), "https://www.postgresql.org/download/linux/");
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
