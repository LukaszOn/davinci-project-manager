# DaVinci Project Manager

[English](README.md) · [Polski](README.pl.md)

DaVinci Project Manager is a desktop app for DaVinci Resolve network project libraries. I started it so I could switch between libraries on different servers, manage team access, and back up databases from one place.

I used AI to help write the code. The app is still early, and there may be bugs, especially in setups I have not tested. If something breaks, report it on [GitHub Issues](https://github.com/LukaszOn/davinci-project-manager/issues).

## Installation

Download a package from [GitHub Releases](https://github.com/LukaszOn/davinci-project-manager/releases).

| System | Packages |
| --- | --- |
| Windows x64 | Run the `Setup.exe` installer or extract the portable ZIP and run `DaVinciProjectManager.exe`. |
| macOS | Choose the Apple Silicon or Intel archive, extract it, and move the app to Applications. |
| Linux x64 | Use the `.deb` package for Debian or Ubuntu, or extract the portable `.tar.gz` archive. |

The Windows installer is unsigned. The macOS packages are not notarized by Apple.

## How it works

In **Connections**, save a profile for an existing PostgreSQL server. Sign in and select a project library. You can keep several server profiles and switch between them.

The PostgreSQL access module manages accounts and library assignments. A server administrator sets it up the first time. See the [setup guide](GUIDE.md).

The app uses `pg_dump` and `pg_restore` to back up and restore libraries. On Windows and macOS, it can download client tools for supported PostgreSQL versions. On Linux, use system packages or select a folder containing the tools.

English is the default. Select **Polski** from **Language** in the sidebar and restart the app to switch.

The app uses C#, .NET 10, Avalonia UI, and Npgsql. It is an independent project under the [MIT license](LICENSE).

## DaVinci Resolve Project Server

For a self-hosted server, the companion repository includes Docker Compose files and setup instructions. [DaVinci Resolve Project Server](https://github.com/LukaszOn/davinci-resolve-project-server)
