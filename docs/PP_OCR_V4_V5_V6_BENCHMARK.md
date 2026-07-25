# PP-OCRv4 / PP-OCRv5 / PP-OCRv6 benchmark

Date: 2026-07-25

Environment:

- CPU: Intel Core i7-7700K @ 4.20 GHz
- OS: Windows 10 22H2 (10.0.19045)
- Runtime: ONNX Runtime / OpenVINO 1.24.1
- Models:
  - PP-OCRv4 mobile detection + recognition
  - PP-OCRv5 mobile detection + unified recognition
  - PP-OCRv6 small detection + unified recognition
- Dataset: 11 manually transcribed game subtitle screenshots (10 Chinese, 1 English)
- Excluded: the Japanese screenshot (`JP (1).jpg`)
- Timing: 10 measured passes after warm-up, 110 OCR calls per configuration
- Accuracy: whitespace-insensitive character error rate (CER); punctuation is retained
- Memory: isolated `vstest` process per configuration

## Results

| Model | Provider | Character accuracy | CER | Exact images | Average latency | Initialization | Warm private delta | Peak private delta |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| V4 | ORT CPU | 98.036% | 1.964% | 4/11 | 227.233 ms | 468.044 ms | 92.551 MiB | 251.633 MiB |
| V4 | OpenVINO CPU | 98.036% | 1.964% | 4/11 | 137.821 ms | 1048.841 ms | 316.000 MiB | 1246.660 MiB |
| V5 | ORT CPU | 97.616% | 2.384% | 6/11 | 312.205 ms | 664.273 ms | 100.879 MiB | 258.531 MiB |
| V5 | OpenVINO CPU | 97.616% | 2.384% | 6/11 | 214.091 ms | 1064.422 ms | 333.340 MiB | 1189.965 MiB |
| V6 | ORT CPU | 99.299% | 0.701% | 9/11 | 374.667 ms | 666.324 ms | 109.488 MiB | 266.363 MiB |
| V6 | OpenVINO CPU | 99.299% | 0.701% | 9/11 | 281.474 ms | 1131.544 ms | 375.270 MiB | 1161.199 MiB |

CPU and OpenVINO produced identical recognized text for every image with all
three model versions.

Compared with V4, V6 improves character accuracy by 1.263 percentage points and
reduces total character errors by about 64%. Its two non-exact images only differ
in leading/trailing ellipsis representation. V5 has more exact images than V4,
but several larger omissions make its aggregate CER slightly worse on this
small corpus. In particular, it drops characters in one Chinese subtitle and
misspells several English words in the single long English screenshot.

V6 is also heavier. Its average latency is about 65% higher with ORT CPU and
104% higher with OpenVINO than V4 on this machine. V5 is about 37% and 55%
slower than V4 respectively, without an accuracy gain on this corpus.
OpenVINO reduces average latency by 39% for V4, 31% for V5, and 25% for V6,
at the cost of much higher initialization and private memory. Peak values
include native runtime memory arenas accumulated across variable image sizes
and 110 calls, so they should not be interpreted as steady-state resident
memory.

The ONNX file sizes are:

| Model | Detection | Recognition | Total |
|---|---:|---:|---:|
| V4 mobile | 4.54 MiB | 10.33 MiB | 14.87 MiB |
| V5 mobile | 4.55 MiB | 15.75 MiB | 20.30 MiB |
| V6 small | 9.46 MiB | 20.18 MiB | 29.64 MiB |

## Recommendation

Use PP-OCRv6 as the default because subtitle correctness is the primary goal
and the measured 281 ms OpenVINO latency remains suitable for subtitle refresh.
Keep PP-OCRv4 available in settings for lower-latency or compatibility-sensitive
systems. The current evidence does not justify exposing V5 as another runtime
choice. If V6 initialization fails, the application automatically falls back
to V4.

This is a small game-subtitle corpus with only one English screenshot, so the
numbers describe this workload rather than general OCR quality. Add more English
screenshots before drawing a broad Chinese/English model-quality conclusion.

## PP-OCRv6 tiny follow-up

Two additional OpenVINO configurations were measured with the same 11-image,
10-round protocol. The existing V4 and V6 small measurements were reused.

| Detection | Recognition | Character accuracy | CER | Exact images | Average latency | Initialization |
|---|---|---:|---:|---:|---:|---:|
| V4 mobile | V4 mobile | 98.036% | 1.964% | 4/11 | 137.821 ms | 1048.841 ms |
| V6 tiny | V6 tiny | 92.707% | 7.293% | 4/11 | 107.444 ms | 866.254 ms |
| V6 tiny | V6 small | 94.109% | 5.891% | 6/11 | 265.316 ms | 1042.100 ms |
| V6 small | V6 small | 99.299% | 0.701% | 9/11 | 281.474 ms | 1131.544 ms |

The full tiny pair is 22% faster than V4 but loses too much accuracy. Replacing
only the detector reduces V6 small latency by just 6%, showing that recognition
dominates this multi-line subtitle workload. It also introduces a serious line
ordering error in `jql1.JPG`, and misses a complete English `Boss` line in
`star.jpg`. The tiny detector is therefore not recommended for the production
subtitle path.

## Reproduce

Build `GI-Test` in Release and run each benchmark method in a separate
`vstest.console` invocation:

- `GI_Test.OCRBenchmarkTests.BenchmarkV4Cpu`
- `GI_Test.OCRBenchmarkTests.BenchmarkV4OpenVino`
- `GI_Test.OCRBenchmarkTests.BenchmarkV5Cpu`
- `GI_Test.OCRBenchmarkTests.BenchmarkV5OpenVino`
- `GI_Test.OCRBenchmarkTests.BenchmarkV6Cpu`
- `GI_Test.OCRBenchmarkTests.BenchmarkV6OpenVino`

Set `OCR_BENCHMARK_ROUNDS` to change the default 10 rounds. Each run writes a
detailed `benchmark-<model>-<provider>.json` file beside `GI-Test.dll`.
