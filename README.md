# DaVinci Project Manager

[English](README.md) · [Polski](README.pl.md)

DaVinci Project Manager is a desktop app for DaVinci Resolve network project libraries. I started it so I could switch between libraries on different servers, manage team access, and back up databases from one place.

I used AI to help write the code. The app is still early, and there may be bugs, especially in setups I have not tested. If something breaks, report it on [GitHub Issues](https://github.com/LukaszOn/davinci-project-manager/issues).

## Installation

Open [GitHub Releases](https://github.com/LukaszOn/davinci-project-manager/releases) and choose the package for your system under **Assets**.

| System | Packages |
| --- | --- |
| Windows x64 | Run the `Setup.exe` installer or extract the portable ZIP and run `DaVinciProjectManager.exe`. |
| macOS | Choose the Apple Silicon or Intel archive, extract it, and move the app to Applications. |
| Linux x64 | Use the `.deb` package for Debian or Ubuntu, or extract the portable `.tar.gz` archive. |

The Windows installer is unsigned. The macOS packages are not notarized by Apple.

## How it works

In **Connections**, save a profile for an existing PostgreSQL server. Sign in and select a project library. You can keep several server profiles and switch between them.

If you use our Docker Compose stack, keep `postgres` as the connection database and select the library you created in Resolve, for example `editing_projects`. [Connection settings for the Docker stack](https://github.com/LukaszOn/davinci-resolve-project-server#connect-the-desktop-manager)

A PostgreSQL superuser sets up the access module. Once it is ready, use a login with the appropriate permissions to add accounts and assign libraries. [Account setup guide](GUIDE.md#set-up-account-management)

Use **Users → User groups** to give a team access to several libraries on one server. Everyone keeps their own Resolve login. [Set up and use groups](GUIDE.md#user-groups)

In **Backup and restore**, select a library to see when the Docker stack last reported a successful backup. Manager also shows the last backup it saved on this computer.

The app uses `pg_dump` and `pg_restore` to back up and restore libraries. On Windows and macOS, it can download client tools for supported PostgreSQL versions. On Linux, use system packages or select a folder containing the tools.

English is the default. Select **Polski** from **Language** in the sidebar and restart the app to switch.

The app uses C#, .NET 10, Avalonia UI, and Npgsql. It is an independent project under the [MIT license](LICENSE).

## DaVinci Resolve Project Server

The desktop app connects directly to your PostgreSQL server. If you also want to create accounts and assign libraries from a browser, you can run the web panel on the server. Both apps can use the same account registry. [View the server Compose file with the web panel](https://github.com/LukaszOn/davinci-resolve-project-server/blob/main/davinci-resolve-project-server-with-manager.yml) · [Server setup instructions](https://github.com/LukaszOn/davinci-resolve-project-server#single-file-installation)
