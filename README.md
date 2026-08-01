# Lorekeeper

Lorekeeper is a Dalamud plugin for Final Fantasy XIV that translates NPC dialogue into Polish and displays the result in an ImGui overlay and an optional OBS Browser Source.

## Current features

- Captures dialogue from the `Talk` addon.
- Supports Unending Journey dialogue handled through the normal Talk path.
- Translates through the user's OpenAI account.
- Stores completed translations in a local cache.
- Displays the same `DialogueSnapshot` in ImGui and OBS.
- Automatically hides the overlay when the dialogue closes.
- Serves the OBS overlay locally at `http://127.0.0.1:19742/`.
- Opens plugin settings with `/lore`.

## Privacy

The dialogue text and speaker name are sent to OpenAI for translation. The API key and translation cache are stored locally in the plugin configuration directory and are not included in release packages.

## Installation from the custom repository

Add the repository URL in:

```text
/xlsettings -> Experimental -> Custom Plugin Repositories
```

Then install Lorekeeper normally from `/xlplugins`.

See `PUBLISHING.md` for repository setup and automatic release instructions.

## Building locally

1. Open `Lorekeeper.sln` in Visual Studio.
2. Select `Debug | x64` for local development or `Release | x64` for a distributable package.
3. Rebuild the solution.
4. Debug output is created under `Lorekeeper/bin/x64/Debug/`.
5. Release packaging is created under `Lorekeeper/bin/x64/Release/Lorekeeper/`.
