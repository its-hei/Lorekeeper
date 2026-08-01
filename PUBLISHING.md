# Lorekeeper — własne repozytorium Dalamuda z automatycznymi aktualizacjami

Ta kopia została przygotowana bezpośrednio na podstawie `Lorekeeper(8).zip`.
Kod tłumaczenia, OBS MVP i `DialogueState` pozostały bazą projektu.

Mechanizm działa podobnie do Penumbry:

1. użytkownik ręcznie dodaje adres `repo.json` w zakładce `Experimental`,
2. instaluje Lorekeepera przez `/xlplugins`,
3. kolejne wydania pojawiają się jako zwykłe aktualizacje Dalamuda.

Plugin nie trafia do oficjalnego katalogu. Każda osoba znająca adres repozytorium może go jednak technicznie dodać, ponieważ custom repositories nie obsługują hasła ani prywatnego dostępu.

## 1. Utwórz repozytorium GitHub

Utwórz publiczne repozytorium, najlepiej o nazwie:

```text
Lorekeeper
```

Nie dodawaj automatycznie nowego README, `.gitignore` ani licencji — są już w tej paczce.

## 2. Wrzuć projekt na GitHub

Najprościej przez GitHub Desktop:

1. Rozpakuj tę paczkę.
2. W GitHub Desktop wybierz `File -> Add local repository`.
3. Wskaż folder zawierający `Lorekeeper.sln`.
4. Jeżeli program poprosi o utworzenie repozytorium Git, zaakceptuj.
5. Zrób pierwszy commit.
6. Kliknij `Publish repository`.
7. Odznacz `Keep this code private`.

Repozytorium musi być publiczne, ponieważ Dalamud musi pobrać `repo.json` i paczkę ZIP zwykłym żądaniem HTTP.

## 3. Nadaj workflow prawo zapisu

Na stronie repozytorium otwórz:

```text
Settings
-> Actions
-> General
-> Workflow permissions
```

Zaznacz:

```text
Read and write permissions
```

Zapisz ustawienia.

Workflow potrzebuje tego prawa, aby utworzyć GitHub Release i zaktualizować `repo.json`.

## 4. Pierwsze wydanie

1. Wejdź w zakładkę `Actions`.
2. Wybierz `Publish Lorekeeper`.
3. Kliknij `Run workflow`.
4. W polu wersji wpisz:

```text
0.1.0.0
```

5. W opisie zmian wpisz na przykład:

```text
Pierwsza prywatna wersja testowa.
```

Workflow automatycznie:

- pobierze aktualne pliki deweloperskie Dalamuda,
- przywróci zależności w trybie locked,
- zbuduje `Release | x64`,
- wygeneruje prawidłowy manifest wewnątrz ZIP-a,
- sprawdzi DLL, manifest, bibliotekę OpenAI i overlay OBS,
- utworzy GitHub Release z plikiem `Lorekeeper.zip`,
- wpisze nową wersję i link pobierania do `repo.json`,
- zapisze zaktualizowany `repo.json` w głównej gałęzi.

## 5. Adres repozytorium dla żony

Przy repozytorium `Lorekeeper` i gałęzi `main` adres będzie wyglądał tak:

```text
https://raw.githubusercontent.com/TWOJ_LOGIN/Lorekeeper/main/repo.json
```

Na komputerze żony:

1. Usuń Lorekeepera z `Dev Plugin Locations`, jeżeli był wcześniej dodany ręcznie.
2. W grze wpisz `/xlsettings`.
3. Otwórz zakładkę `Experimental`.
4. W sekcji `Custom Plugin Repositories` wklej adres `repo.json`.
5. Kliknij przycisk `+` i zapisz ustawienia.
6. Otwórz `/xlplugins`.
7. Wyszukaj `Lorekeeper` i kliknij `Install`.
8. W konfiguracji pluginu wpisz klucz API OpenAI.

Nie trzeba włączać `Get plugin testing builds`. Lorekeeper jest prywatnym custom repository, ale używa zwykłego kanału aktualizacji wewnątrz tego repozytorium.

## 6. Kolejne aktualizacje

Po każdej zmianie:

1. Wyślij nowy kod do głównej gałęzi GitHub.
2. Uruchom ponownie `Actions -> Publish Lorekeeper`.
3. Podaj numer wyższy od poprzedniego, na przykład:

```text
0.1.0.1
0.1.0.2
0.1.0.3
```

Dalamud porówna `AssemblyVersion` z wersją zainstalowaną i pokaże aktualizację u żony.

## Ważne

- Nie publikuj klucza API OpenAI w kodzie, manifeście ani GitHub Secrets. Klucz wpisuje się lokalnie w konfiguracji pluginu.
- `repo.json` jest początkowo pustą tablicą `[]`. Pierwsze uruchomienie workflow uzupełni go automatycznie.
- `IsTestingExclusive` jest wyłączone celowo. Zakładka `Experimental` służy tutaj do ręcznego dodania custom repository.
- Paczka wydania zawiera manifest wygenerowany podczas kompilacji. Jest to wymagane przez Dalamud API 15.
- Jeżeli główna gałąź nazywa się inaczej niż `main`, zmień nazwę gałęzi w adresie `raw.githubusercontent.com`.
