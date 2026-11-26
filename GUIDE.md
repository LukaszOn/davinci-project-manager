# DaVinci Project Manager guide

DaVinci Project Manager manages DaVinci Resolve project libraries on Windows, macOS, and Linux. Each server profile stores the PostgreSQL address, port, and username on your computer. The app uses your password for the current session.

Start from **Connections**. The header shows the connection status, active library, and server address on every page. **Users** manages library access. **Backup and restore** runs archive operations, and **Session log** shows the results. Profiles saved by Project Library Desk still work.

English is the default. To switch, select **Polski** under **Language** in the sidebar and restart the app.

## Install and connect

- **Windows x64.** Run `Windows-x64-Setup.exe` from the release. It adds a Start menu shortcut and an entry in Windows app settings. For the portable package, extract the ZIP and run `DaVinciProjectManager.exe`.
- **macOS.** Choose Apple Silicon or Intel, extract the archive, and move `DaVinci Project Manager.app` to Applications.
- **Debian and Ubuntu x64.** Install `Linux-amd64.deb`, for example with `sudo apt install ./package-name.deb`. On other distributions, extract `Linux-x64.tar.gz` and run `./DaVinciProjectManager`.

The packages include .NET. Linux also needs the system libraries listed in the [Avalonia documentation](https://docs.avaloniaui.net/docs/supported-platforms). The Windows installer is unsigned and the macOS packages are not notarized.

1. Add a profile with a name, host, port (usually `5432`), and PostgreSQL credentials. Under **Connection options**, set **Connection database** to `postgres` or the database supplied by your provider.
2. Choose **Verify TLS** for a server with a certificate. Select a CA certificate if the server uses a private CA. **Prefer TLS** is available for trusted networks where the server may lack TLS. The information icons explain the connection options.
3. Select **Connect** and choose a library. Next time, select the saved profile and enter the password again.

## Set up account management

Use an existing PostgreSQL account to connect, usually the `postgres` account created during setup. PostgreSQL logins apply across the instance. You assign access to each library separately.

- If the server already has the web panel module, the app recognizes the `resolve_access` account registry. The module can work without the web service. Panel administrator is a role in the registry. The desktop app uses the privileges of the PostgreSQL login in the selected profile.
- For a new instance, connect as a PostgreSQL superuser, select a library, and select **Set up account management**. The wizard displays the `pg_hba.conf` rules for the server administrator to add and reload. The app checks the rules before registering the library. Then you can create individual accounts. For several libraries, place all library rules above the shared rejection rules for `resolve_panel_members`.
- For ongoing management, you can use a delegated login with access to registry functions. Account management and backup permissions can be granted separately.

## PostgreSQL tools, backups, and restores

After you connect, **Connections** shows the required tool version and whether it is available. The app checks the folder selected in your profile, downloaded toolsets, and common system locations. One toolset can serve several PostgreSQL servers of the same major version.

- **Windows x64 and macOS (Apple Silicon and Intel).** **Download tools** is available for PostgreSQL 14–19. The app verifies the publisher's SHA-256 checksum and saves `pg_dump`, `pg_restore`, `psql`, and their dependencies in your user directory. Downloads start when you select the button. The original archives can be several hundred megabytes.
- **Linux.** Install a client package for your distribution, such as `postgresql-client-16` from an appropriate APT repository, then select **Check again**.
- **Custom tools.** Choose the `bin` folder in **Connection options**, save your profile, and reconnect.

The app creates PostgreSQL `.backup` files. DaVinci Resolve's Restore wizard opens `.resolve.backup` archives.

- **Save backup** writes an archive to your computer and checks that its list of objects is readable. Restore the archive and open a Resolve project for a complete check.
- **Restore a backup** creates a new database on the connected server. You can use a different instance from the source. The PostgreSQL account needs `CREATEDB`. Extensions and other objects may need additional permissions.
- After restoring a library, register it on the destination instance to assign users. A single database backup covers its database contents. Back up PostgreSQL roles and the account registry separately.

## Build from source

The application uses .NET 10 and Avalonia. To publish a self-contained Windows x64 build:

```sh
dotnet publish ProjectLibraryDesk.csproj -c Release -r win-x64 --self-contained true
```

Other runtime identifiers are `linux-x64`, `osx-arm64`, and `osx-x64`. Test packages on the target system before distributing them.

### Build installers

The installer scripts use portable packages from the same release. Windows needs the [NSIS compiler](https://nsis.sourceforge.io/Download). Debian and Ubuntu use `dpkg-deb`.

```sh
python packaging/windows/build.py Windows-x64.zip Windows-x64-Setup.exe --makensis /path/to/makensis.exe
bash packaging/linux/build-deb.sh Linux-x64.tar.gz Linux-amd64.deb
```

## License and package sources

The app is independent of Blackmagic Design and available under the [MIT license](LICENSE). It downloads PostgreSQL client tools from [theseus-rs/postgresql-binaries](https://github.com/theseus-rs/postgresql-binaries/releases). A local installation, such as the [EDB binaries](https://www.enterprisedb.com/download-postgresql-binaries), also works when the required tools are available. The [package catalog](PgToolPackages.json) records pinned URLs and SHA-256 checksums. See [third-party notices](THIRD-PARTY-NOTICES.md) for dependency sources and licenses.
