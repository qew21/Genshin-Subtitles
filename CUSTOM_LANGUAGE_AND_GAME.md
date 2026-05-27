# 自定义语言包与游戏配置说明

本文说明如何在当前程序中手动接入未内置的语言包，例如波兰语 `PL`，以及如何为未出现在游戏列表中的新游戏配置多语言字幕。

## 基本原理

程序不是调用在线翻译接口，而是通过本地 TextMap 语言包完成匹配和显示：

1. OCR 识别游戏画面中的当前语言文本。
2. 在输入语言包中查找最接近的原文。
3. 通过相同的文本 key 找到输出语言包中的翻译。
4. 在字幕窗口中显示目标语言文本。

因此，只要输入语言包和输出语言包使用相同 key，并且 JSON 格式正确，程序就可以手动支持未内置语言或未内置游戏。

## 数据目录

所有用户配置和语言包位于：

```text
%AppData%\GI-Subtitles
```

常见文件结构如下：

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

## 场景一：为已有游戏添加未内置输出语言

例如用户已经有某个游戏的波兰语语言包 `TextMapPL.json`，希望实现英语识别、波兰语显示。

### 1. 放置语言包

以游戏 `Genshin` 为例，将文件放到：

```text
%AppData%\GI-Subtitles\Genshin\TextMapPL.json
```

同时需要已有输入语言包，例如：

```text
%AppData%\GI-Subtitles\Genshin\TextMapEN.json
```

### 2. 确认语言包格式

输入语言包和输出语言包都应是简单 JSON 字典：

```json
{
  "1001": "Start game",
  "1002": "Settings"
}
```

波兰语输出语言包示例：

```json
{
  "1001": "Rozpocznij grę",
  "1002": "Ustawienia"
}
```

关键要求：

- 两个文件必须使用相同 key。
- key 对应的 value 分别是不同语言的文本。
- 文件名需要符合 `TextMap<语言码>.json`，例如 `TextMapPL.json`。

### 3. 修改 Config.json

打开：

```text
%AppData%\GI-Subtitles\Config.json
```

设置：

```json
{
  "Game": "Genshin",
  "Input": "EN",
  "Output": "PL",
  "Output2": ""
}
```

保存后重启程序。

### 4. 可选：配置自动下载地址

如果只是手动放置 `TextMapPL.json`，可以不配置下载地址。

如果希望程序能够生成下载链接或自动下载，需要检查该游戏的配置文件，例如：

```text
%AppData%\GI-Subtitles\Genshin.json
```

如果远程仓库的波兰语文件名是 `TextMapPL.json`，一般不需要额外映射。若远程文件名不是 `PL`，则需要添加 `LanguageMapping`，例如：

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

## 场景二：为未内置游戏添加多语言支持

例如新游戏 `MyGame` 的游戏文本是英语，用户有：

```text
TextMapEN.json
TextMapPL.json
```

目标是英语 OCR 识别，显示波兰语字幕。

### 1. 添加游戏列表项

打开或创建：

```text
%AppData%\GI-Subtitles\Games.json
```

加入新游戏：

```json
[
  {
    "Name": "MyGame",
    "DisplayNames": {
      "zh-CN": "我的游戏",
      "en-US": "My Game",
      "ja-JP": "My Game"
    }
  }
]
```

如果文件中已经有其他游戏，请在原数组中追加该对象，不要删除已有项目。

`Name` 是程序内部使用的游戏 ID，后续目录名和配置文件名都要与它一致。

### 2. 创建游戏语言包目录

创建目录：

```text
%AppData%\GI-Subtitles\MyGame
```

放入语言包：

```text
%AppData%\GI-Subtitles\MyGame\TextMapEN.json
%AppData%\GI-Subtitles\MyGame\TextMapPL.json
```

### 3. 创建游戏配置文件

创建：

```text
%AppData%\GI-Subtitles\MyGame.json
```

如果只使用本地语言包，不需要自动下载，可以使用：

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

如果有远程语言包仓库，可以配置下载模板：

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

模板中的 `{Language}` 会被当前语言码替换。

### 4. 修改 Config.json

打开：

```text
%AppData%\GI-Subtitles\Config.json
```

设置当前游戏和语言：

```json
{
  "Game": "MyGame",
  "Input": "EN",
  "Output": "PL",
  "Output2": ""
}
```

保存后重启程序。

## 双输出语言

程序支持最多两个输出语言。比如同时显示波兰语和中文：

```json
{
  "Game": "MyGame",
  "Input": "EN",
  "Output": "PL",
  "Output2": "CHS"
}
```

此时需要同时存在：

```text
%AppData%\GI-Subtitles\MyGame\TextMapEN.json
%AppData%\GI-Subtitles\MyGame\TextMapPL.json
%AppData%\GI-Subtitles\MyGame\TextMapCHS.json
```

## 注意事项

- 当前界面中的语言列表是内置的，未内置语言如 `PL` 可能无法在界面中直接选择，需要手动修改 `Config.json`。
- 手动设置未内置语言后，尽量不要在界面中重新点击输出语言的 Apply，否则可能会被改回内置语言。
- 波兰语作为输出语言通常可行；波兰语作为 OCR 识别语言不等于完整支持，因为 OCR 模型和识别字典未专门适配波兰语。
- 新游戏是否可用，主要取决于 TextMap 文件是否能整理成相同 key 的 JSON 字典。
- 如果 TextMap key 不一致，程序无法通过输入文本找到对应输出文本。
- 如果游戏画面字体、描边、分辨率或 UI 布局导致 OCR 识别不稳定，需要重新选择识别区域或调整 OCR 相关设置。

## 判断是否配置成功

配置成功后，程序应能完成以下流程：

1. 当前游戏目录下存在输入和输出 TextMap 文件。
2. 程序启动后可以加载到足够数量的 key-value。
3. OCR 识别到英语文本后，字幕窗口显示波兰语文本。
4. 如果没有显示，优先检查 `Config.json` 中的 `Game`、`Input`、`Output` 是否与目录和文件名一致。

