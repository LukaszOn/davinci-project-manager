# Instrukcja DaVinci Project Manager

Samodzielna aplikacja na Windows, macOS i Linux do obsługi bibliotek projektów DaVinci Resolve na PostgreSQL. Każdy profil zawiera adres i port instancji oraz login PostgreSQL. Po połączeniu wybierasz jedną z baz w tej instancji. Profile są zapisywane lokalnie, a hasło jest używane w bieżącej sesji.

DaVinci Project Manager is a free, independent PostgreSQL utility for DaVinci Resolve project libraries. It supports multiple server profiles, library access management, backups and restores. The current interface is in Polish. DaVinci Resolve is a product of Blackmagic Design. This project is independent of Blackmagic Design.

Zacznij w widoku **Połączenia**. Górny pasek na każdej stronie pokazuje stan połączenia, aktywną bibliotekę i adres serwera. W widoku **Użytkownicy** wybierz osobę z listy, aby zarządzać dostępem do biblioteki. W widokach **Kopie i odtwarzanie** oraz **Dziennik sesji** znajdziesz operacje na kopiach i ich wyniki. Profile zapisane w Project Library Desk są dostępne pod nową nazwą aplikacji.

## Uruchomienie

- **Windows x64.** Uruchom plik `Windows-x64-Setup.exe` z danego wydania. Instalator dodaje skrót do menu Start i wpis w ustawieniach aplikacji Windows. Wersję przenośną uruchomisz przez `DaVinciProjectManager.exe` w rozpakowanym ZIP-ie.
- **macOS.** Wybierz paczkę Apple Silicon lub Intel. Rozpakuj ją i przenieś `DaVinci Project Manager.app` do folderu Aplikacje.
- **Debian i Ubuntu x64.** Zainstaluj plik `Linux-amd64.deb` systemowym instalatorem pakietów lub przez `sudo apt install ./nazwa-pliku.deb`. W pozostałych dystrybucjach możesz rozpakować `Linux-x64.tar.gz` i uruchomić `./DaVinciProjectManager`.

Paczki zawierają środowisko .NET. Na Linuksie wymagane są biblioteki systemowe wskazane w [dokumentacji Avalonia](https://docs.avaloniaui.net/docs/supported-platforms). Wersje Windows i macOS są wydaniami beta bez podpisu wydawcy. Pakiety macOS nie są notaryzowane.

1. Dodaj profil. Wpisz nazwę, adres, port (zwykle `5432`), login i hasło PostgreSQL. W **Opcjach połączenia** jako bazę startową wpisz `postgres` lub nazwę podaną przez dostawcę.
2. Dla serwera z certyfikatem wybierz **TLS z weryfikacją**. Dodaj certyfikat CA, jeśli serwer korzysta z prywatnego CA. W zaufanej sieci z serwerem bez TLS możesz wybrać **TLS opcjonalny**. Ikony informacji przy ustawieniach wyświetlają podpowiedzi.
3. Kliknij **Połącz** i wybierz bazę z listy. Przy następnym połączeniu wystarczy wybrać zapisany profil i ponownie podać hasło.

## Konta i pierwsza konfiguracja instancji

Instancja PostgreSQL potrzebuje istniejącego konta do nawiązania pierwszego połączenia. Zwykle jest nim `postgres`, utworzony podczas instalacji PostgreSQL. Loginy są wspólne dla instancji, a dostęp do bibliotek przydzielasz osobno.

- W istniejącej instalacji panelu WWW program rozpoznaje rejestr i konta przechowywane w `resolve_access`. Moduł działa bez usługi WWW. Administrator panelu jest rolą w rejestrze kont, a uprawnienia tej aplikacji wynikają z loginu PostgreSQL użytego w profilu.
- Na serwerze bez rejestru połącz się jako superużytkownik PostgreSQL, wybierz bibliotekę i kliknij **Przygotuj obsługę kont**. Kreator pokaże reguły `pg_hba.conf`, które administrator serwera dodaje i przeładowuje. Program sprawdza reguły przed rejestracją biblioteki. Następnie możesz założyć pierwsze indywidualne konto. W przypadku kilku bibliotek umieść reguły każdej z nich przed jednym zestawem reguł odrzucających pozostałe bazy dla grupy `resolve_panel_members`.
- Do późniejszej obsługi kont możesz użyć delegowanego loginu wyposażonego w uprawnienia do funkcji rejestru. Dostęp do panelu i dostęp do kopii można nadawać osobno.

## Narzędzia PostgreSQL, kopie i odtwarzanie

Po połączeniu widok **Połączenia** pokazuje wersję wymaganych narzędzi i ich dostępność. Aplikacja sprawdza folder ustawiony w profilu, zestawy pobrane wcześniej i typowe lokalizacje narzędzi w systemie. Jeden zestaw działa ze wszystkimi serwerami tej samej głównej wersji.

- **Windows x64 i macOS (Apple Silicon i Intel).** Przycisk **Pobierz narzędzia** jest dostępny dla PostgreSQL 14–19. Aplikacja weryfikuje SHA-256 archiwum wydawcy i zapisuje programy `pg_dump`, `pg_restore`, `psql` i potrzebne pliki w katalogu użytkownika. Pobieranie zaczyna się po kliknięciu przycisku. Archiwa źródłowe zawierają też inne składniki i mogą mieć kilkaset MB.
- **Linux.** Aplikacja proponuje pakiet kliencki właściwy dla dystrybucji, np. `postgresql-client-16` na systemie z odpowiednim repozytorium APT. Po instalacji w systemie kliknij **Sprawdź ponownie**.
- **Własne narzędzia.** W **Opcjach połączenia** możesz wskazać katalog `bin`. Zapisz profil i połącz się ponownie, aby użyć go dla bieżącej sesji.

Kopie są plikami PostgreSQL `.backup`. Archiwa `.resolve.backup` otwiera kreator Restore w DaVinci Resolve.

- **Zapisz kopię** zapisuje archiwum na komputerze i sprawdza, czy da się odczytać jego spis obiektów. Pełną próbę wykonujesz przez odtworzenie i otwarcie projektu w Resolve.
- **Odtwórz kopię** używa aktualnie połączonego profilu jako celu. Możesz wybrać inną instancję niż źródłowa. Program tworzy bazę pod nową nazwą. Konto musi mieć uprawnienie `CREATEDB`. Poszczególne obiekty archiwum, np. rozszerzenia, mogą wymagać dodatkowych uprawnień.
- Odtworzenie danych i przydział użytkowników są osobnymi krokami. Po odtworzeniu biblioteki zarejestruj ją dla użytkowników na docelowej instancji. Rolę, ustawienia rejestru i konta trzeba objąć osobną kopią instancji. Kopia pojedynczej bazy obejmuje dane tej bazy.

## Budowanie

Projekt używa .NET 10 i Avalonia. Przykład kompilacji samodzielnej wersji dla Windows x64:

```sh
dotnet publish ProjectLibraryDesk.csproj -c Release -r win-x64 --self-contained true
```

Identyfikatory pozostałych wersji to `linux-x64`, `osx-arm64` i `osx-x64`. Przed publiczną dystrybucją zweryfikuj pakiet na systemie docelowym i podpisz odpowiednie wydania.

### Budowanie instalatorów

Instalatory korzystają z gotowych paczek przenośnych tego samego wydania. Na Windows podaj ścieżkę do kompilatora [NSIS](https://nsis.sourceforge.io/Download). Na Debianie lub Ubuntu użyj `dpkg-deb`.

```sh
python packaging/windows/build.py Windows-x64.zip Windows-x64-Setup.exe --makensis /ścieżka/do/makensis.exe
bash packaging/linux/build-deb.sh Linux-x64.tar.gz Linux-amd64.deb
```

## Licencja i źródła pakietów

Kod projektu jest dostępny na licencji [MIT](LICENSE). Zestawy PostgreSQL są pobierane na żądanie z [theseus-rs/postgresql-binaries](https://github.com/theseus-rs/postgresql-binaries/releases). Możesz też wskazać lokalną instalację, np. [narzędzia wydane przez EDB](https://www.enterprisedb.com/download-postgresql-binaries). [Katalog pakietów](PgToolPackages.json) zawiera przypięte adresy i sumy SHA-256. Przy aktualizacji katalogu porównaj sumy z metadanymi wydawcy. Licencje i źródła zależności są opisane w [informacjach o komponentach zewnętrznych](THIRD-PARTY-NOTICES.md).
