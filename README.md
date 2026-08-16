# Lorekeeper

Lorekeeper is a Dalamud plugin for Final Fantasy XIV that translates NPC dialogue and displays the translation in a dedicated overlay.

## Current features

- Reads dialogue from the `Talk` addon.
- Includes the NPC name as translation context.
- Translates dialogue through OpenAI.
- Stores completed translations in a local cache.
- Displays translated dialogue in a custom overlay.
- Opens the overlay with `/lore`.

## Building

1. Open `Lorekeeper.sln` in Visual Studio.
2. Restore dependencies and build the solution.
3. The development plugin DLL will be created under `Lorekeeper/bin/x64/Debug/`.
4. Add the generated `Lorekeeper.dll` as a Dalamud development plugin.

## Configuration

Use Dalamud's plugin configuration interface to enter the OpenAI API key and model. Translation cache data is stored locally in the plugin configuration directory.
