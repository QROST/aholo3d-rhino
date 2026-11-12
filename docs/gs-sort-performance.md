# Exact GS depth sorting performance

CuriousArc · qrost. Local CPU measurement on 2026-11-11; native Rhino/Windows performance acceptance remains pending.

Camera changes prepare a globally ordered set of visible Gaussians. The previous stable merge sort repeatedly compares depths and insertion ordinals in 136-byte projected records. The native projection path emits unique increasing ordinals even across multiple instances and after visibility filtering. The new sorter recognizes that case, reverses the ordinals, then performs a stable byte-wise radix sort on the **complete positive finite 64-bit depth**. Descending ordinal input is already ready for the depth passes. Bytes that do not vary are skipped; already correctly ordered input returns after full validation.

Equal-depth compositing keeps exactly the previous reverse insertion order. No depth quantization, geometry removal, opacity change or LOD is introduced. The threshold of 4,096 chooses a sorting algorithm, not a limit on the number of Gaussians. Small inputs, arbitrary ordinal order and duplicate ordinals retain stable merge sorting. The fast path uses the existing index/scratch buffers plus 1 KiB of stack histogram storage and performs no per-Gaussian heap allocation. Cancellation checks cover validation, reversal, histogram/scatter passes and copying back; an interrupted frame remains unpublished.

## Measured scope

The checked-in [raw report](benchmarks/gs-sort-2026-11-11-arm64.json) records 20 fixed synthetic cases: 4,096, 65,536, 262,144 and 1,048,576 projected values, each with five depth/ordinal distributions. Each case warms both implementations, then alternates their run order over five samples. The old implementation is copied verbatim from source `9f7fb3b` except for class visibility/name. Every measured result is compared index-for-index with independent stable LINQ depth/ordinal ordering, including the baseline; report hashes retain the resulting index sequences. Timings exclude input allocation/reset, oracle sorting, comparison and hashing.

On this Apple Silicon Mac with .NET 8.0.29 and tiered compilation explicitly disabled for the benchmark process, the 1,048,576-value medians were:

| Distribution | Original merge | Current sorter | Original/current time |
| --- | ---: | ---: | ---: |
| random | 228.48 ms | 83.23 ms | 2.75× |
| depth-ties | 132.87 ms | 31.23 ms | 4.25× |
| coherent | 78.24 ms | 1.90 ms | 41.19× |
| reverse-depth | 93.20 ms | 27.73 ms | 3.36× |
| shuffled-ordinals | 228.11 ms | 228.98 ms | 1.00× |

All measured current-sort samples allocated zero managed heap bytes. The shuffled-ordinal case uses the fallback merge path and shows no material improvement; small fallback measurements may be modestly slower. This is not a general claim that every sort input accelerates.

These numbers measure only synthetic **CPU sorting**. They exclude parsing, SH projection, native mesh construction/upload, GPU drawing, document traversal and camera navigation. They are not Rhino FPS, GPU performance, real-World-scene quality or Windows evidence. Default host JIT settings may produce different timings. The exact candidate source-file hashes and baseline commit are stored in the report; hardware load and runtime differences can change results.

## Reproduce from a source checkout

```sh
DOTNET_TieredCompilation=0 dotnet run --project tools/Aholo3D.Reference -c Release -- --sort-benchmark artifacts/sort-performance
```

The output file is `artifacts/sort-performance/gaussian-sort.json`. Use the same environment, runtime, source and input suite for comparisons; do not use fixed millisecond thresholds as a CI acceptance gate. Cross-platform unit tests instead check exact ordering for adjacent double values, subnormal/maximum depths, non-contiguous signed ordinal extremes, stable duplicates, unchanged source records, untouched scratch tails, invalid referenced depths, unreferenced invalid records, fast/fallback cancellation and a large equal-depth closed-form transparency case.

For manual native acceptance, compare the same saved document and camera path before/after with several overlapping GS instances, including equal-depth colored groups. Check colors, opacity, clipping planes, cancellation while orbiting, complete reappearance after settling, native geometry occlusion and diagnostic preparation time on Mac and Windows. No changed pixel order or missing instances is expected. Sort-only measurements do not replace those pending host checks.
