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
  - PP-OCRv6 tiny detection + unified recognition
- Dataset: 11 manually transcribed game subtitle screenshots (10 Chinese, 1 English)
- Excluded: the Japanese screenshot (`JP (1).jpg`)
- Timing: 10 measured passes after warm-up, 110 OCR calls per configuration
- Accuracy: whitespace-insensitive character error rate (CER); punctuation is retained
- Memory: isolated `vstest` process per configuration

## Results after the shared detection-pipeline update

These are OpenVINO results after loading each detector's `inference.yml`,
line-height-tolerant reading-order sorting, YAML-controlled dilation, and
four-point perspective crops.

| Detection | Recognition | Character accuracy | CER | Exact images | Average latency | Initialization | Warm private delta | Peak private delta |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| V4 mobile | V4 mobile | 97.896% | 2.104% | 3/11 | 170.216 ms | 1012.643 ms | 326.867 MiB | 1351.207 MiB |
| V5 mobile | V5 mobile | 98.597% | 1.403% | 7/11 | 275.978 ms | 1130.954 ms | 359.570 MiB | 1255.215 MiB |
| V6 small | V6 small | **99.158%** | **0.842%** | **8/11** | 344.274 ms | 1184.732 ms | 384.586 MiB | 1281.090 MiB |
| V6 tiny | V6 tiny | 97.335% | 2.665% | 4/11 | **127.599 ms** | 810.941 ms | 260.430 MiB | 992.527 MiB |
| V6 tiny | V4 mobile | 97.756% | 2.244% | 2/11 | 158.211 ms | 904.019 ms | 307.746 MiB | 1300.066 MiB |
| V6 tiny | V6 small | 97.756% | 2.244% | 6/11 | 317.139 ms | 978.754 ms | 349.957 MiB | 1250.707 MiB |

CPU and OpenVINO still produce identical text in the regression corpus. Peak
values include native runtime memory arenas accumulated across variable image
sizes and should not be interpreted as steady-state resident memory.

The ONNX file sizes are:

| Model | Detection | Recognition | Total |
|---|---:|---:|---:|
| V4 mobile | 4.54 MiB | 10.33 MiB | 14.87 MiB |
| V5 mobile | 4.55 MiB | 15.75 MiB | 20.30 MiB |
| V6 small | 9.46 MiB | 20.18 MiB | 29.64 MiB |
| V6 tiny | 1.73 MiB | 4.25 MiB | 5.99 MiB |

## Recommendation

Use PP-OCRv6 tiny detection and recognition as the default while the broader
screenshot trial is in progress. It is about 25% faster than the updated V4
pipeline and uses much smaller model files. V6 small remains the best measured
accuracy option, but is substantially slower. Keep PP-OCRv4 available for
compatibility. Japanese uses the V6 tiny detector with the dedicated V4
Japanese recognizer because V6 tiny intentionally excludes Japanese; a failed
V6 initialization still falls back to the full V4 path.

This is a small game-subtitle corpus with only one English screenshot, so the
numbers describe this workload rather than general OCR quality. Add more English
screenshots before drawing a broad Chinese/English model-quality conclusion.

## Why the first tiny measurements were misleading

The initial tiny runs used hard-coded V4-oriented thresholds and exact-Y
sorting. That filtered valid tiny boxes and reversed two nearly aligned boxes
in `jql1.JPG`. Loading the tiny model's official thresholds and grouping boxes
with a line-height tolerance raised full-tiny accuracy from 92.707% to 97.335%;
the two hybrid configurations rose from 91.865%/94.109% to 97.756%. Remaining
tiny errors are primarily small unrelated UI boxes rather than the earlier
detector omissions or reading-order reversal.

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
