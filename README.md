# DaVinci Project Manager

Projekt powstał w stylu *vibe coding* z pomocą AI. Mam doświadczenie w programowaniu i tworzeniu oprogramowania, a AI pomagało mi w implementacji.

DaVinci Project Manager to bezpłatna aplikacja na Windows, macOS i Linux do zarządzania bibliotekami projektów DaVinci Resolve w PostgreSQL. Możesz zapisać profile kilku serwerów, przełączać się między bibliotekami, zarządzać dostępem zespołu oraz robić i odtwarzać kopie baz. Interfejs jest po polsku.

## Pobieranie

Instalatory i paczki przenośne znajdziesz w [wydaniu beta](https://github.com/LukaszOn/davinci-project-manager/releases/tag/v0.4.0-beta.1).

- **Windows x64.** Uruchom `Setup.exe` albo wypakuj ZIP i otwórz `DaVinciProjectManager.exe`.
- **macOS.** Wybierz paczkę Apple Silicon lub Intel, wypakuj ją i przenieś `.app` do folderu Aplikacje.
- **Linux x64.** Na Debianie i Ubuntu użyj pakietu `.deb`. Dostępna jest też paczka przenośna `.tar.gz`.

Pakiety beta dla Windows i macOS mogą wyświetlić ostrzeżenie systemowe. Instalator Windows jest niepodpisany, a wydanie macOS nie ma notaryzacji Apple.

## Pierwsze połączenie

W zakładce **Połączenia** podaj adres istniejącego PostgreSQL oraz login i hasło. Po połączeniu wybierz bibliotekę z listy. Do zarządzania kontami potrzebujesz modułu dostępu i odpowiednich uprawnień. W zakładce **Kopie i odtwarzanie** skorzystasz z klienckich narzędzi PostgreSQL. Szczegóły konfiguracji znajdziesz w [instrukcji](GUIDE.md).

Planowany projekt **DaVinci Resolve Project Server** ma uzupełnić aplikację desktopową o stos Docker z PostgreSQL i panelem WWW. Punktem wyjścia jest [Docker-Davinci-Resolve-Project-Server autorstwa elliotmatson](https://github.com/elliotmatson/Docker-Davinci-Resolve-Project-Server).

Kod jest dostępny na licencji [MIT](LICENSE). Projekt jest niezależny od Blackmagic Design.
