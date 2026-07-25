# PP-OCRv6 model provenance

The PP-OCRv6 tiny and small detection and recognition models in this directory were
converted from the official PaddleOCR inference archives:

- `PP-OCRv6_tiny_det_infer.tar`
  - Source: `https://paddle-model-ecology.bj.bcebos.com/paddlex/official_inference_model/paddle3.0.0/PP-OCRv6_tiny_det_infer.tar`
- `PP-OCRv6_tiny_rec_infer.tar`
  - Source: `https://paddle-model-ecology.bj.bcebos.com/paddlex/official_inference_model/paddle3.0.0/PP-OCRv6_tiny_rec_infer.tar`
- `PP-OCRv6_small_det_infer.tar`
  - Source: `https://paddle-model-ecology.bj.bcebos.com/paddlex/official_inference_model/paddle3.0.0/PP-OCRv6_small_det_infer.tar`
  - SHA-256: `BFB7C1E59F0FAA6B540EBDCA93AEA3F4B1F2477805B389FBEE117820D68FE9F5`
- `PP-OCRv6_small_rec_infer.tar`
  - Source: `https://paddle-model-ecology.bj.bcebos.com/paddlex/official_inference_model/paddle3.0.0/PP-OCRv6_small_rec_infer.tar`
  - SHA-256: `DA460F968CE9F88325AC3A34FA302077D6E9B0DCEFB16BA3137CD7796F879D06`

Conversion used PaddlePaddle 3.1.1, Paddle2ONNX 2.0.2rc3, ONNX opset 11,
and ONNXSlim 0.1.94.

Generated ONNX checksums:

- Tiny detection: `A10C33B7C6E3F6A762ACA82CFEA0B9EF32A9FF3C6D541FA423BC0D4B310F45A0`
- Tiny recognition: `50E611E6F4588001F3BDC7660BDCDCC807E9451C9EC4E47BEE131660A8D7EBA5`
- Small detection: `5E37E91C022FA8061D07448F4C05EF6A8419681B4AAABA0D0A52C123C72F2F18`
- Small recognition: `67F3AB66AFBEFB19A127761C7DE264A3A9AAAB159418A58B85FD6B6811B36D79`

PaddleOCR is distributed under the Apache License 2.0.
