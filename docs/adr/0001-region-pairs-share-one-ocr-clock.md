# Region pairs share one OCR clock

Live overlay keeps a list of region pairs (each a capture region plus its own display region), not a primary slot plus a one-shot `Region2` fallback probe. On each OCR tick, every active pair with a valid capture region may be sampled and enqueued from the same clock. This means one sampling schedule, not parallel OCR: captures are small per-pair images and OCR remains a single serial queue.

The normal settings UI allows at most four pairs. The engine also accepts hand-edited data up to an explicit hard cap of eight pairs; pairs after that cap are ignored. This distinction is intentional: four is the supported operator configuration, while eight is a defensive upper bound against unbounded work from malformed or hand-edited configuration. The implementation and tests must keep these two limits distinct.

The queue must not grow without bound when OCR takes longer than the sampling interval. A pair with work already queued or in flight is coalesced until its current result completes; a stale result must not overwrite state after the pair is cleared, disabled, or reconfigured. With one configured pair, the hot path should perform only that pair's capture/OCR work—no full-screen capture and no timer per pair.
