# BetterGI OCR and dialogue-option reference

This note records the ideas reviewed from the local
`packages/better-genshin-impact` source tree and how they map to this project.
BetterGI is GPL-3.0, so this project reimplements applicable ideas and does not
copy its source code or image assets.

## Implemented here

| BetterGI idea | Local implementation |
| --- | --- |
| Locate the dialogue ellipsis icon before running OCR | `DialogueOptionDetector` generates its own ellipsis template at runtime and searches only the normalized right-side dialogue ROI. |
| Scale recognition assets from a 1920×1080 reference | Template size and OCR crop geometry scale with the captured game screen. |
| Derive the text ROI from the lowest option icon | The text crop starts after the icon and covers the option stack above it. |
| Keep OCR regions and confidence values | `PaddleOCREngine` now exposes the actual average character confidence and filters low-confidence blocks from merged text. |
| Sort/filter choices as separate OCR regions | Dialogue options remain separate candidates with their own screen rectangles. |
| Debounce dialogue state transitions | Two consecutive missing scans are required before treating the option menu as closed. |
| Avoid repeating expensive OCR on identical frames | Dialogue option crops use robust hashes; the normal OCR hash now also includes aspect ratio and text density. |

For mouse input, the cursor position after the option menu closes identifies
which recognized option was selected. Only that option is sent to translation
and AI voice playback. Keyboard/controller selection is intentionally not
guessed.

## Useful BetterGI areas reviewed

- `GameTask/AutoSkip/AutoSkipTrigger.cs`: option-icon-first ROI selection,
  option filtering, state/debounce handling.
- `GameTask/Common/Job/ChooseTalkOptionTask.cs`: reusable option recognition
  flow.
- `Core/Recognition/OCR/Paddle/PaddleOcrService.cs`: model warm-up,
  language-specific PP-OCR v4/v5/v6 selection, region confidence.
- `Core/Recognition/OCR/Paddle/Rec.cs`: recognition batching and confidence
  calculation.
- `Core/Recognition/OCR/OcrResult.cs`: deterministic Y/X reading order.
- `GameTask/AutoSkip/Audio`: process-loopback audio activity detection used to
  wait for in-game voice before advancing dialogue.

## Deferred

- **PP-OCR v5/v6 model selection:** the current installer only ships the v4
  general and Japanese recognition models. Model selection should be added only
  together with packaged, tested models and a migration/fallback path.
- **Process-loopback VAD:** BetterGI uses this to avoid skipping original game
  audio. It does not improve this project's downloaded AI voice playback and
  would add substantial native/model dependencies.
- **Keyboard/controller choice inference:** reliable support needs selected-row
  visual-state detection or input tracking. Mouse-position fallback must not be
  reused because it can voice the wrong option.
- **Background game capture:** the current application uses screen capture and
  user-selected regions. A capture backend change is a larger, independent
  feature and should be benchmarked before adoption.
