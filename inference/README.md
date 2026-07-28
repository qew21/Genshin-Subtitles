# PP-OCRv6 model provenance

Runtime model files are restored from the versioned bundle described by
`models.json`. Run `scripts\Restore-OcrModels.ps1` after cloning the repository.
The script verifies both the archive and every extracted file with SHA-256.

The runtime bundle contains PP-OCRv6 tiny detection and recognition models,
plus the PP-OCRv4 Japanese recognizer used only for Japanese text. Larger and
legacy benchmark models are intentionally kept outside the application package.

The PP-OCRv6 tiny models were converted from the official PaddleOCR inference
archives:

- `PP-OCRv6_tiny_det_infer.tar`
  - Source: `https://paddle-model-ecology.bj.bcebos.com/paddlex/official_inference_model/paddle3.0.0/PP-OCRv6_tiny_det_infer.tar`
- `PP-OCRv6_tiny_rec_infer.tar`
  - Source: `https://paddle-model-ecology.bj.bcebos.com/paddlex/official_inference_model/paddle3.0.0/PP-OCRv6_tiny_rec_infer.tar`
Conversion used PaddlePaddle 3.1.1, Paddle2ONNX 2.0.2rc3, ONNX opset 11,
and ONNXSlim 0.1.94.

Generated ONNX checksums:

- Tiny detection: `A10C33B7C6E3F6A762ACA82CFEA0B9EF32A9FF3C6D541FA423BC0D4B310F45A0`
- Tiny recognition: `50E611E6F4588001F3BDC7660BDCDCC807E9451C9EC4E47BEE131660A8D7EBA5`

PaddleOCR is distributed under the Apache License 2.0.
