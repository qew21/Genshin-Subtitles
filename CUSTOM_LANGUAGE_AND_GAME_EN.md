# Custom Language Packs and Game Configuration

This document explains how to manually add a language that is not listed in the app, such as Polish `PL`, and how to configure multilingual subtitle support for a game that is not included in the built-in game list.

## How It Works

The app does not use an online translation service. It uses local TextMap files:

1. OCR reads the current text from the game screen.
2. The app searches the input language TextMap for the closest matching source text.
3. It uses the same text key to find the corresponding entry in the output language TextMap.
4. The translated text is displayed in the subtitle overlay.

Because of this, a custom language or custom game can work as long as the input and output TextMap files share the same keys and use the expected JSON format.

## Data Directory

User configuration files and language packs are stored in:

```text
%AppData%\GI-Subtitles
```

A typical structure looks like this:

```text
%AppData%\GI-Subtitles
├─ Config.json
├─ Games.json
├─ Genshin.json
├─ MyGame.json
└─ MyGame
   ├─ TextMapEN.json
   └─ TextMapPL.json
```

## Scenario 1: Add a Non-Listed Output Language for an Existing Game

For example, suppose you already have a Polish language pack named `TextMapPL.json` and want the app to recognize English text and display Polish subtitles.

### 1. Place the Language Pack

For the game `Genshin`, place the Polish file here:

```text
%AppData%\GI-Subtitles\Genshin\TextMapPL.json
```

You also need the input language file, for example:

```text
%AppData%\GI-Subtitles\Genshin\TextMapEN.json
```

### 2. Check the TextMap Format

Both input and output language files should be simple JSON dictionaries:

```json
{
  "1001": "Start game",
  "1002": "Settings"
}
```

Example Polish output file:

```json
{
  "1001": "Rozpocznij grę",
  "1002": "Ustawienia"
}
```

Important requirements:

- Both files must use the same keys.
- The values should contain the text in each language.
- The file name must follow `TextMap<LanguageCode>.json`, for example `TextMapPL.json`.

### 3. Edit Config.json

Open:

```text
%AppData%\GI-Subtitles\Config.json
```

Set:

```json
{
  "Game": "Genshin",
  "Input": "EN",
  "Output": "PL",
  "Output2": ""
}
```

Save the file and restart the app.

### 4. Optional: Configure Download URLs

If you manually place `TextMapPL.json`, download URLs are not required.

If you want the app to generate download links or download the file automatically, check the game configuration file, for example:

```text
%AppData%\GI-Subtitles\Genshin.json
```

If the remote repository uses the file name `TextMapPL.json`, no extra mapping may be needed. If the remote file uses another language name or code, add a `LanguageMapping` entry:

```json
{
  "RepoUrl": "",
  "RepoType": "",
  "InputUrlTemplate": "https://example.com/TextMap{Language}.json",
  "OutputUrlTemplate": "https://example.com/TextMap{Language}.json",
  "MediumUrlTemplate": "",
  "TestFile": "",
  "Warning": "",
  "LanguageMapping": {
    "PL": "pl"
  }
}
```

## Scenario 2: Add Multilingual Support for a Non-Listed Game

For example, suppose the new game is called `MyGame`, the game text is in English, and you have:

```text
TextMapEN.json
TextMapPL.json
```

The goal is to recognize English text and display Polish subtitles.

### 1. Add the Game to the Game List

Open or create:

```text
%AppData%\GI-Subtitles\Games.json
```

Add the new game:

```json
[
  {
    "Name": "MyGame",
    "DisplayNames": {
      "zh-CN": "My Game",
      "en-US": "My Game",
      "ja-JP": "My Game"
    }
  }
]
```

If the file already contains other games, append this object to the existing array instead of replacing the whole list.

`Name` is the internal game ID. The folder name and game configuration file name must use the same value.

### 2. Create the Game Language Pack Folder

Create:

```text
%AppData%\GI-Subtitles\MyGame
```

Place the language packs inside:

```text
%AppData%\GI-Subtitles\MyGame\TextMapEN.json
%AppData%\GI-Subtitles\MyGame\TextMapPL.json
```

### 3. Create the Game Configuration File

Create:

```text
%AppData%\GI-Subtitles\MyGame.json
```

If you only use local language packs and do not need automatic downloads, this minimal configuration is enough:

```json
{
  "RepoUrl": "",
  "RepoType": "",
  "InputUrlTemplate": "",
  "OutputUrlTemplate": "",
  "MediumUrlTemplate": "",
  "TestFile": "",
  "Warning": "",
  "LanguageMapping": {}
}
```

If you have a remote TextMap repository, configure download templates:

```json
{
  "RepoUrl": "https://example.com/MyGame",
  "RepoType": "",
  "InputUrlTemplate": "https://example.com/MyGame/TextMap{Language}.json",
  "OutputUrlTemplate": "https://example.com/MyGame/TextMap{Language}.json",
  "MediumUrlTemplate": "",
  "TestFile": "",
  "Warning": "",
  "LanguageMapping": {
    "EN": "EN",
    "PL": "PL"
  }
}
```

`{Language}` will be replaced with the current language code.

### 4. Edit Config.json

Open:

```text
%AppData%\GI-Subtitles\Config.json
```

Set the current game and languages:

```json
{
  "Game": "MyGame",
  "Input": "EN",
  "Output": "PL",
  "Output2": ""
}
```

Save the file and restart the app.

## Dual Output Languages

The app supports up to two output languages. For example, to display Polish and Simplified Chinese at the same time:

```json
{
  "Game": "MyGame",
  "Input": "EN",
  "Output": "PL",
  "Output2": "CHS"
}
```

The following files must exist:

```text
%AppData%\GI-Subtitles\MyGame\TextMapEN.json
%AppData%\GI-Subtitles\MyGame\TextMapPL.json
%AppData%\GI-Subtitles\MyGame\TextMapCHS.json
```

## Notes and Limitations

- The language list in the current UI is built in. A non-listed language such as `PL` may not be selectable from the UI, so you may need to edit `Config.json` manually.
- After manually setting a non-listed language, avoid clicking the output language Apply button in the UI, because it may overwrite the setting with a built-in language.
- Polish as an output language can work if the TextMap file is valid. Polish as an OCR input language is not the same level of support, because the OCR model and recognition dictionary are not specifically configured for Polish.
- A custom game can work only if its TextMap files can be represented as JSON dictionaries with shared keys.
- If the TextMap keys do not match between input and output files, the app cannot map source text to translated text.
- If the game's font, outline, resolution, or UI layout makes OCR unstable, you may need to reselect the recognition region or adjust OCR-related settings.

## How to Check Whether It Works

The configuration is likely correct if:

1. The selected game folder contains both input and output TextMap files.
2. The app loads a meaningful number of key-value entries after startup.
3. When OCR recognizes English text, the subtitle overlay displays Polish text.
4. If nothing is displayed, first check that `Game`, `Input`, and `Output` in `Config.json` match the folder name and file names.

