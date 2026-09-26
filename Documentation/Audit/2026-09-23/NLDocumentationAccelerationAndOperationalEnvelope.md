# Acceleration and Operational Evidence

## State model

`VEAccelerationState` is vendor-neutral and separates:

- Available: the backend can be opened in the current environment.
- Selected: policy selected that backend for this session.
- Active: at least one frame was produced by the selected decoder.
- BenefitMeasured: a comparable A/B run exists; null means no claim is permitted.

Software and NVIDIA NVDEC are represented from the actual FFmpeg decoder name. AMD and Intel are explicit enum values but remain unavailable until a decoder path and physical evidence exist. NVIDIA remains optional; software fallback is preserved.

## Comparable measurements

Every performance claim must validate against `NLDocumentationOperationalBenchmark.schema.json` and use the same PC, Android device, engine subset, workload, duration, codec/profile and transport for both sides of an A/B comparison. Report samples plus P50, P95 and P99; for network paths also report loss and queue depth when available.

No universal pass/fail threshold is defined here. A result may set `BenefitMeasured=true` only when both sides are complete and comparable. Throughput alone cannot establish lower latency, jitter or input quality.

## Current evidence boundary

- Software fallback: implemented and covered by automated decoder tests.
- NVIDIA NVDEC: selected and active only after decoded-frame evidence; prior physical benchmark artifacts exist but are not refreshed by this audit.
- AMD: not implemented or physically verified.
- Intel: not implemented or physically verified.
- Current physical-device benchmark: unavailable because ADB reported no connected device at audit start.
