# DaVinci Project Manager

[English](README.md) · [Polski](README.pl.md)

DaVinci Project Manager to aplikacja desktopowa do obsługi sieciowych bibliotek projektów DaVinci Resolve. Zacząłem ją tworzyć, żeby w jednym miejscu przełączać się między bibliotekami na różnych serwerach, zarządzać dostępem zespołu i robić kopie baz.

Przy tworzeniu aplikacji korzystałem z AI do pisania kodu. To nadal beta i mogą się zdarzyć błędy, zwłaszcza w konfiguracjach, których dotąd nie testowałem. Jeśli coś nie działa, daj znać w [Issues](https://github.com/LukaszOn/davinci-project-manager/issues).

## Instalacja

Gotowe paczki są dostępne w [GitHub Releases](https://github.com/LukaszOn/davinci-project-manager/releases).

| System | Paczki |
| --- | --- |
| Windows x64 | Instalator `Setup.exe` albo przenośny ZIP. Po rozpakowaniu ZIP-a uruchom `DaVinciProjectManager.exe`. |
| macOS | Wybierz Apple Silicon albo Intel. Rozpakuj archiwum i przenieś aplikację do folderu Aplikacje. |
| Linux x64 | Pakiet `.deb` dla Debiana i Ubuntu albo przenośne archiwum `.tar.gz`. |

Windows i macOS mogą wyświetlić ostrzeżenie przy uruchamianiu tej bety. Instalator Windows jest niepodpisany, a paczki macOS nie mają notaryzacji Apple.

## Jak to działa

W zakładce **Połączenia** zapisujesz profil istniejącego serwera PostgreSQL. Po zalogowaniu wybierasz bibliotekę projektów. Możesz zapisać kilka profili i przełączać się między serwerami.

Kontami i dostępem do poszczególnych bibliotek zarządzasz przez moduł dostępu w PostgreSQL. Do pierwszej konfiguracji potrzebujesz administratora serwera. Szczegóły są w [instrukcji konfiguracji](GUIDE.md).

Do ręcznego zapisywania i odtwarzania kopii aplikacja korzysta z `pg_dump` i `pg_restore`. Na Windows i macOS może pobrać odpowiednie narzędzia dla obsługiwanych wersji PostgreSQL. Na Linuksie korzysta z narzędzi zainstalowanych w systemie lub ze wskazanego katalogu.

Domyślnym językiem jest angielski. Aby przejść na polski, wybierz **Polski** w polu **Language** na pasku bocznym i uruchom aplikację ponownie.

Aplikacja jest napisana w C# na .NET 10. Interfejs jest zbudowany w Avalonia UI, a połączenia z PostgreSQL obsługuje Npgsql.

DaVinci Project Manager jest niezależnym projektem na licencji [MIT](LICENSE).

## DaVinci Resolve Project Server

Jeśli chcesz uruchomić własny serwer dla bibliotek Resolve, w repozytorium [DaVinci Resolve Project Server](https://github.com/LukaszOn/davinci-resolve-project-server) znajdziesz gotowe pliki Docker Compose i instrukcję konfiguracji.
