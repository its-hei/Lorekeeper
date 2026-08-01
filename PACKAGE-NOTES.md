# Informacje o przygotowanej paczce

Źródło: `Lorekeeper(8).zip` przesłany 1 sierpnia 2026.

## Dodane

- `.github/workflows/publish.yml` — ręczne wydawanie wersji i automatyczne aktualizowanie `repo.json`.
- `repo.json` — początkowo pusty manifest custom repository.
- `PUBLISHING.md` — instrukcja publikacji i instalacji.
- rozszerzony workflow kompilacyjny dla gałęzi `main` i `master`.

## Zmienione

- `Lorekeeper/Lorekeeper.json` — autor, polski opis, informacja o OpenAI i metadane instalatora.
- `.gitignore` — ignorowanie `.vs`, `bin`, `obj`, skrótów i plików lokalnych.
- `README.md` — opis instalacji z custom repository i lokalnej kompilacji.
- `MainWindow.cs` oraz `Lorekeeper.csproj` — użycie domyślnego fontu Dalamuda zamiast osobnego pliku TTF.

## Usunięte z paczki publikacyjnej

- `.vs/`, `bin/`, `obj/` — lokalne pliki Visual Studio i wyniki kompilacji.
- `translations.json.lnk` — skrót prowadzący do lokalnego cache na komputerze autora.
- osobny plik fontu — plugin korzysta teraz z fontu dostarczanego przez Dalamuda.

Kod tłumaczenia, OpenAI, cache, OBS MVP i `DialogueState` nie został przebudowany funkcjonalnie w ramach przygotowania publikacji.
