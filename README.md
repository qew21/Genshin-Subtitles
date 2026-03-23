# Genshin-Subtitles Documentation

[**DeepWiki**](https://deepwiki.com/qew21/Genshin-Subtitles)

[**English**](./README.md) | [**中文简体**](./README_CN.md)

## Overview

Genshin-Subtitles is an OCR-based application that provides real-time multi-language subtitles for games such as Genshin Impact and Honkai: Star Rail. It captures game text using optical character recognition technology, matches it against known text databases, and displays translated subtitles in a configurable overlay.

### Core Functionality

Genshin-Subtitles enables players to:

1. Display dual-language subtitles during gameplay
2. Enjoy voice acting in one language while reading text in another
3. Select specific screen regions for text recognition
4. Configure subtitle appearance, position, and language preferences
5. Extract and process subtitles from video files

### System Requirements

- Windows 10/11 64-bit
- CPU with AVX instruction set support (required by PaddleOCR)
- .NET Framework 4.8 or higher
- At least 2GB available RAM
- Compatible games: Genshin Impact, Honkai: Star Rail, Honkai Impact 3rd

## How It Works

```
┌─────────────────┐    ┌──────────────┐    ┌─────────────────┐
│ Game Screen     │ -> │ OCR Engine   │ -> │ Text Matcher    │
│ Capture         │    │ (PaddleOCR)  │    │ (Optimized)     │
└─────────────────┘    └──────────────┘    └─────────────────┘
                                                  │
                                                  v
┌─────────────────┐    ┌──────────────┐    ┌─────────────────┐
│ Subtitle        │ <- │ Translation  │ <- │ Language Pack   │
│ Overlay Display │    │ Lookup       │    │ Database        │
└─────────────────┘    └──────────────┘    └─────────────────┘
```

### Key Components

| Component | Description |
|-----------|-------------|
| **OCR Engine** | PaddleOCRSharp (PP-OCRv4 mobile) for text recognition |
| **OptimizedMatcher** | n-gram indexed fuzzy matching with Levenshtein distance |
| **VideoProcessor** | Extract subtitles from video files automatically |
| **Frame Detector** | Smart change detection to avoid redundant OCR |
| **Language Maps** | JSON files containing game text in 13+ languages |
| **Subtitle Overlay** | Customizable WPF overlay for displaying translations |

## Core Features

### 🚀 High-Performance Text Matcher (OptimizedMatcher)

- **n-gram Indexing**: 2-gram for Chinese, 4-gram for English - drastically reduces candidate set
- **FNV-1a Hashing**: Avoids creating millions of string objects, reduces memory footprint
- **Smart Line Parsing**: Automatically handles multi-line text, identifies headers and content
- **Fuzzy Matching**: Tolerates OCR recognition errors with Levenshtein distance

### 📹 Video Subtitle Extraction

- Automatically extract subtitles from video files
- Configurable recognition region (via `demo_region.json`)
- Batch process video content to generate bilingual subtitles

### 🎯 Frame Detection Optimization

- Intelligent screen change detection
- Triggers OCR only when text actually changes
- Significantly reduces CPU usage and power consumption

### 🔊 AI Voice Support

- Local AI voice file loading
- Online voice synthesis API support
- Automatically skips blank text

## Version History

### 1.6.3 (Current)
✅ Upgraded OCR models to PP-OCRv4 mobile (detection + recognition)  
✅ New OptimizedMatcher with 10x+ performance improvement  
✅ Video subtitle extraction feature (VideoProcessor)  
✅ Frame detection for optimized OCR triggering  
✅ Skip voice synthesis for blank text  
✅ Support for Honkai: Star Rail and Honkai Impact 3rd  
✅ Full CI/CD test coverage (8 unit tests)  

### 1.6.0
- Initial release with OptimizedMatcher
- Video processor for subtitle extraction
- Frame detection optimization

## Hotkeys

| Hotkey | Function |
|--------|----------|
| `Ctrl+Shift+S` | Start/Pause OCR recognition |
| `Ctrl+Shift+R` | Select recognition region |
| `Ctrl+Shift+H` | Hide/Show subtitles |

## Development

### Building

```bash
# Restore NuGet packages
nuget restore GI-Subtitles.sln

# Build Release version
msbuild GI-Subtitles.sln -t:GI-Subtitles:Rebuild -p:Configuration=Release -p:Platform=x64
```

### Running Tests

```bash
# Run unit tests
dotnet vstest GI-Test\bin\Release\GI-Test.dll
```

### CI/CD

This project uses GitHub Actions for continuous integration:

- Automatic unit tests on every Push/PR
- OCR models downloaded from Release (not committed to Git)
- Test coverage: 8 test cases (7 passing, 1 conditionally skipped)

## Architecture Details

### OptimizedMatcher Algorithm

The matcher uses a two-stage approach:

1. **Indexing Phase** (build time):
   - Normalize all keys (lowercase, remove punctuation)
   - Build n-gram hash index (2-gram for CN, 4-gram for EN)
   - Use FNV-1a hash for fast lookup without string allocation

2. **Matching Phase** (runtime):
   - Extract n-grams from OCR input
   - Look up candidate entries via hash index
   - Score candidates with Levenshtein distance
   - Return best match above threshold

### Video Processing Pipeline

1. Load video file and region configuration
2. Extract frames at configurable interval
3. Run OCR on each frame
4. Match recognized text against language pack
5. Export subtitles with timestamps

## Language Support

Genshin-Subtitles supports translation between 13+ languages including:

- Chinese (Simplified & Traditional)
- English
- Japanese
- Korean
- French, German, Spanish, Russian, and more

Translation is accomplished by matching recognized text against a comprehensive database of game dialogues.

## Troubleshooting

### Common Issues

**OCR not recognizing text:**
- Ensure recognition region is correctly selected (`Ctrl+Shift+R`)
- Check that game is running in windowed or borderless mode
- Verify OCR models are downloaded (check `inference/` folder)

**High CPU usage:**
- Frame detection should minimize redundant processing
- Reduce recognition region size
- Ensure you're using the latest version with OptimizedMatcher

**Subtitles not matching:**
- Some text may have slight OCR errors - the fuzzy matcher handles most cases
- Check that language packs are up to date (right-click tray icon)

## License

This project is for educational and research purposes only.

## Acknowledgments

- [PaddleOCRSharp](https://github.com/raoyutian/PaddleOCRSharp) - OCR engine
- [Genshin_Datasets](https://github.com/AI-Hobbyist/Genshin_Voice_Sorting_Scripts) - Game text data
- [Dimbreath/AnimeGameData](https://gitlab.com/Dimbreath/AnimeGameData) - TextMap language packs
