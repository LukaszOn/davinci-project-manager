using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ProjectLibraryDesk;

internal sealed class BackupHistory
{
    private static string Receipt(ServerProfile server, PgLibrary library)
    {
        string identity = $"{server.Host.ToLowerInvariant()}:{server.Port}/{library.Oid}/{library.Name}";
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ProjectLibraryDesk", "backups", key);
    }

    public DateTimeOffset? Last(ServerProfile server, PgLibrary library)
    {
        try
        {
            string path = Receipt(server, library);
            return File.Exists(path) ? new DateTimeOffset(File.GetLastWriteTimeUtc(path)) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    public void Record(ServerProfile server, PgLibrary library)
    {
        string path = Receipt(server, library);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var receipt = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
        File.SetLastWriteTimeUtc(receipt.SafeFileHandle, DateTime.UtcNow);
    }
}
