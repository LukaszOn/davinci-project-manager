# DaVinci Project Manager

[English](README.md) · [Polski](README.pl.md)

DaVinci Project Manager to aplikacja desktopowa do zarządzania bibliotekami sieciowymi DaVinci Resolve. Zacząłem ją tworzyć, żeby w jednym miejscu przełączać się między bibliotekami na różnych serwerach, zarządzać dostępem zespołu i robić kopie baz.

Przy tworzeniu aplikacji korzystałem z AI do pisania kodu. To nadal beta i mogą się zdarzyć błędy, zwłaszcza w konfiguracjach, których dotąd nie testowałem. Jeśli coś nie działa, daj znać w [Issues](https://github.com/LukaszOn/davinci-project-manager/issues).

## Instalacja

Otwórz [wydania na GitHubie](https://github.com/LukaszOn/davinci-project-manager/releases) i w sekcji **Assets** wybranego wydania wybierz paczkę dla swojego systemu.

| System | Paczki |
| --- | --- |
| Windows x64 | Instalator `Setup.exe` albo przenośny ZIP. Po rozpakowaniu ZIP-a uruchom `DaVinciProjectManager.exe`. |
| macOS | Wybierz Apple Silicon albo Intel. Rozpakuj archiwum i przenieś aplikację do folderu Aplikacje. |
| Linux x64 | Pakiet `.deb` dla Debiana i Ubuntu albo przenośne archiwum `.tar.gz`. |

Windows i macOS mogą wyświetlić ostrzeżenie przy uruchamianiu tej bety. Instalator Windows jest niepodpisany, a paczki macOS nie mają notaryzacji Apple.

## Jak to działa

W zakładce **Połączenia** zapisujesz profil istniejącego serwera PostgreSQL. Po zalogowaniu wybierasz bibliotekę projektów. Możesz zapisać kilka profili i przełączać się między serwerami.

Jeśli korzystasz z naszego zestawu Docker Compose, jako bazę startową zostaw `postgres`. Po połączeniu wybierz bibliotekę utworzoną w Resolve, np. `editing_projects`. [Ustawienia połączenia z Dockerem](https://github.com/LukaszOn/davinci-resolve-project-server/blob/main/README.pl.md#połączenie-z-desktopowym-managerem)

Do pierwszej konfiguracji modułu dostępu zaloguj się jako superużytkownik PostgreSQL. Później do zakładania kont i przydzielania bibliotek wystarczy login z odpowiednimi uprawnieniami. [Instrukcja konfiguracji po angielsku](GUIDE.md#set-up-account-management)

W **Użytkownicy → Grupy użytkowników** przydzielisz zespołowi dostęp do kilku bibliotek na jednym serwerze. Każdy nadal loguje się w Resolve własnym kontem. [Instrukcja obsługi grup po angielsku](GUIDE.md#user-groups)

W zakładce **Kopie i odtwarzanie** wybierz bibliotekę, żeby sprawdzić, kiedy Docker potwierdził udaną kopię. Manager pokazuje też datę ostatniej kopii, którą zapisał na tym komputerze.

Do ręcznego zapisywania i odtwarzania kopii aplikacja korzysta z `pg_dump` i `pg_restore`. Na Windows i macOS może pobrać odpowiednie narzędzia dla obsługiwanych wersji PostgreSQL. Na Linuksie korzysta z narzędzi zainstalowanych w systemie lub ze wskazanego katalogu.

Domyślnym językiem jest angielski. Aby przejść na polski, wybierz **Polski** w polu **Language** na pasku bocznym i uruchom aplikację ponownie.

Aplikacja jest napisana w C# na .NET 10. Interfejs jest zbudowany w Avalonia UI, a połączenia z PostgreSQL obsługuje Npgsql.

DaVinci Project Manager jest niezależnym projektem na licencji [MIT](LICENSE).

## DaVinci Resolve Project Server

Aplikacja desktopowa łączy się bezpośrednio z Twoim PostgreSQL. Jeśli chcesz też zakładać konta i przydzielać biblioteki z przeglądarki, na serwerze możesz uruchomić panel WWW. Obie aplikacje mogą korzystać z tego samego rejestru kont. [Plik Compose serwera z panelem WWW](https://github.com/LukaszOn/davinci-resolve-project-server/blob/main/davinci-resolve-project-server-with-manager.yml) · [Instrukcja instalacji serwera](https://github.com/LukaszOn/davinci-resolve-project-server/blob/main/README.pl.md#instalacja-z-jednego-pliku)
