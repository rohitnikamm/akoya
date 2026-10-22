# Akoya for Windows: GPU spike results and plan

Spike for the spec's "GPU (optional speed-up)" section
(`docs/superpowers/specs/2026-10-20-akoya-windows-design.md`). Done on Rohit's PC, 2026-10-20, in about an hour.
Spike code: `spike/gpu/SherpaBench` (sherpa-onnx end to end, any provider, optional replacement native folder) and
`spike/gpu/OrtBench` (the Parakeet encoder alone through ONNX Runtime + DirectML, synthetic features).

**Recommendation: do not build a GPU path now.** The CPU already transcribes a typical dictation in about 0.3 s on this
PC. The only GPU route that works (sherpa-onnx's CUDA build) saves about 0.2 s per dictation, but it needs a separate
fp16 model (+1.07 GB download) and about 1.2 GB of app-local CUDA/cuDNN DLLs, and it crashes on clips longer than
about 25 s. Do the cheap CPU fix below instead, and revisit GPU only through Windows ML (see "Later").

## Machine

- `nvidia-smi`: NVIDIA GeForce RTX 5070 (Blackwell), 12 GB, driver 610.88, WDDM, "CUDA UMD Version: 13.3".
- 32 logical processors, so `SherpaTranscriber` gets `ProcessorCount / 2` = 16 threads.
- .NET 10 SDK only: no Visual Studio, CMake or CUDA toolkit (none was installed).

## What sherpa-onnx offers on Windows (checked against v1.13.8, the version Akoya uses)

- **NuGet:** `org.k2fsa.sherpa.onnx` + `org.k2fsa.sherpa.onnx.runtime.win-x64` (8.1 MB, CPU only). There is no CUDA or
  DirectML NuGet package.
- **CUDA:** GitHub release assets `sherpa-onnx-v1.13.8-cuda-12.x-cudnn-9.x-onnxruntime1.28.2-win-x64-cuda.tar.bz2`
  (568 MB) and `...cuda-13.x...` (456 MB). They hold `sherpa-onnx-c-api.dll` (4.4 MB), `onnxruntime.dll` (15.5 MB),
  `onnxruntime_providers_cuda.dll` (255 MB), `onnxruntime_providers_shared.dll` and a TensorRT provider stub.
  They do **not** include the CUDA runtime: `onnxruntime_providers_cuda.dll` imports `cudart64_13`, `cublas64_13`,
  `cublasLt64_13`, `cufft64_12`, `cudnn64_9` (+ nvrtc). Those come app-local from NVIDIA's PyPI wheels (plain zips, no
  install): `nvidia-cuda-runtime` 13.4.92, `nvidia-cublas` 13.8.0.4, `nvidia-cufft` 12.4.0.43,
  `nvidia-cudnn-cu13` 9.27.0.42. They are NVIDIA-redistributable, but large: cublasLt 471 MB, cufft 222 MB,
  cuDNN ~600 MB across its DLLs. **The full app-local CUDA folder is 1,620 MB on disk, 1,256 MB zipped.** The CUDA 13
  build is the one to use on Blackwell (RTX 50) cards. Users need only an NVIDIA driver new enough for CUDA 13.
- **DirectML:** sherpa-onnx's code has a `directml` provider, but only behind the CMake option
  `SHERPA_ONNX_ENABLE_DIRECTML` (off by default). No release asset or NuGet package is built with it; asking a stock
  build for `directml` logs "Fallback to cpu". Using it means building sherpa-onnx ourselves with MSVC (a Windows CI
  job). Also: `Microsoft.ML.OnnxRuntime.DirectML` has stopped at 1.24.4 (main ORT is at 1.31); DirectML is in
  maintenance and Microsoft points to Windows ML instead.
- **ONNX Runtime directly:** `Microsoft.ML.OnnxRuntime.DirectML` 1.24.4 (11.9 MB nupkg; output adds `DirectML.dll`
  17.7 MB + `onnxruntime.dll` 16.5 MB). It cannot share a process with sherpa-onnx's own `onnxruntime.dll`, so this
  route means rewriting feature extraction and TDT decoding ourselves. Measured below as encoder-only.

## Measurements

Model: Parakeet TDT 0.6B v2 from `%LocalAppData%\Akoya\models\sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8`.
`0.wav` is 7.4 s; longer clips repeat it with 0.5 s gaps. Times are wall clock for one `Decode` (features + encoder +
greedy TDT decode). "First" = first time that clip length is seen; every real dictation has a new length, so the
first-time number is the one users feel on the GPU (CUDA picks kernels per shape). Akoya's tray app was running.

### End to end, sherpa-onnx 1.13.8

| Setup | Load | 7.4 s first / again | 25 s | 120 s | Peak RAM | GPU RAM |
| --- | --- | --- | --- | --- | --- | --- |
| CPU int8, 16 threads (today) | 1.3-1.6 s | 420-650 / 530-650 ms | 1,030 ms | 5.2-6.2 s | 2.6 GB | 0 |
| CPU int8, 8 threads | ~1.4 s | ~320 ms | 815-860 ms | 6.5 s | 2.6 GB | 0 |
| CPU int8, 4 threads | ~1.4 s | ~320 ms | - | 7.6 s | - | 0 |
| CUDA 13, int8 model | 4.4 s | 707 / 686 ms | - | 5.0 s | 2.5 GB | +3.9 GB |
| CPU fp16 model, 8 threads | 3.1 s | 284 / 281 ms | - | 5.0 s | 3.6 GB | 0 |
| **CUDA 13, fp16 model** | 1.6 s | **141 / 48 ms** | **222 / 137 ms** | **crash** | 1.6 GB | +2.6 GB |

(Peak RAM of 2.6 GB on CPU is driven by the 120 s clip; at 25 s it is 1.1 GB.)

- **int8 on CUDA is no faster than CPU.** The int8 model is dynamically quantized (`DynamicQuantizeLinear` /
  `MatMulInteger`), which the CUDA provider mostly leaves on the CPU, so the GPU only adds copies. A GPU path needs
  the fp16 model (`sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-fp16.tar.bz2`, 1,069 MB download, 1.2 GB encoder) on top of
  the int8 one the CPU uses.
- **CUDA fp16 crashes** with an access violation (`0xC0000005` in `OfflineRecognizer.Decode`, kills the process)
  on clips of 30 s or more; 25 s and under work every time. The same model on CPU and the int8 model on CUDA
  handle 120 s fine. Not debugged (likely fp16 overflow in long attention producing bad token ids, or an ORT/cuDNN
  bug). It is a native crash, so a try/catch fallback cannot catch it; clips would have to be cut into ≤20 s chunks.
- The warm-up (0.5 s silence) on CUDA takes 0.6-1.0 s, and the first decode of each new length costs ~2-3x the
  repeat.

### Encoder only, ONNX Runtime 1.24.4 + DirectML (`OrtBench`, synthetic features)

| Encoder | 7.4 s first / again | 10 s | 120 s | Peak RAM |
| --- | --- | --- | --- | --- |
| int8, CPU 16 threads | 158 / 149 ms | 197 ms | 4,310 ms | 2.6 GB |
| int8, DirectML | 318-774 / 280-289 ms | 301-313 ms | 568-647 ms | 0.8 GB |
| fp16, DirectML | 239 / 184 ms | 213 ms | 341-371 ms | 1.3 GB |

DirectML has a floor of ~200-300 ms per call, so it is **slower than the CPU for normal dictations** (under ~20 s)
and only wins on long ones (120 s: 0.35 s vs 4.3 s). DirectML ran 120 s without crashing.

## Side finding: the CPU thread count

16 threads is slower than 8 for short clips on this 32-thread CPU (7.4 s: ~600 ms vs ~320 ms; 25 s: 1,030 vs
840 ms), while 16 helps only very long clips (120 s: 5.2-6.2 s vs 6.5 s). Most dictations are short. Suggested
follow-up (own small change, not GPU): use `Math.Clamp(Environment.ProcessorCount / 2, 1, 8)` and confirm on CI and on
a 6-core machine that short clips do not get slower.

## Cost of each route

| Route | Works today? | Added download | Short dictation gain here | Notes |
| --- | --- | --- | --- | --- |
| sherpa CUDA 13 + fp16 model | Yes, ≤25 s | ~1.2 GB DLLs (zipped) + 1.07 GB model | ~0.3 s → ~0.15 s | NVIDIA only; native crash ≥30 s |
| sherpa built with DirectML | Not built (needs MSVC CI job) | ~35 MB | none expected (encoder alone ≥ CPU) | Any DX12 GPU; DirectML frozen |
| ORT DirectML + own decoder | Encoder only | ~35 MB (+ fp16 model for best speed) | none (≥ CPU) | Rewrite fbank + TDT decode in C# |
| Windows ML (EP catalog) | Not tried | small; Windows fetches the GPU provider | unknown | See "Later" |

None of these can go in `Setup.exe` (86 MB today) except DirectML, and DirectML is not faster for the dictations
Akoya sees. The CUDA route would have to be an optional "GPU pack" downloaded on demand, more than 25x the
installer.

## If it is built anyway (CUDA route)

How `Auto` would choose and fall back:

1. `Auto` = GPU only if all of: an NVIDIA adapter is present (DXGI vendor id 0x10DE), the GPU pack and the fp16
   model are downloaded and verified (SHA-256, like the int8 model), and the driver supports CUDA 13 (load
   `nvcuda.dll`, `cuDriverGetVersion` ≥ 13000). Otherwise CPU with the int8 model, silently.
2. Create the GPU recognizer on a background thread after the CPU one is ready, so dictation works at once. Any
   exception or a sherpa "Fallback to cpu" log while loading keeps the CPU recognizer. Run the warm-up on both.
3. Split audio into ≤20 s pieces at the quietest 0.3 s near each cut before sending it to the GPU (avoids the ≥30 s
   crash); join the texts with a space.
4. A native crash cannot be caught, so load and first decode run in a child process the first time (a
   `--gpu-selftest` switch on Akoya.exe decoding `test_wavs/0.wav`); only a clean exit with the right text enables
   the GPU. Record a failed self-test in settings and do not retry until the app or driver version changes.
5. `Cpu` always uses the CPU recognizer.

Build steps: (1) GPU pack builder script in `windows/scripts/` that downloads the sherpa CUDA 13 tarball and the four
NVIDIA wheels, keeps only the DLLs listed above, and zips them with a manifest of SHA-256s, published as a separate
release asset (never inside `Setup.exe`); (2) a `GpuPackStore` beside the model store, downloading on a user's
"Use GPU" action in Settings rather than silently; (3) load sherpa's C API from the pack folder with
`NativeLibrary.SetDllImportResolver` (as `SherpaBench` does) — this is per process, so the CPU and GPU recognizers
cannot both live in one process with different `sherpa-onnx-c-api.dll` builds, but the CUDA build also runs `cpu`, so
the GPU-pack process uses it for both; (4) the self-test child process; (5) chunking; (6) Settings text and
`docs/FEATURES.md` (Mac: Different on purpose, Core ML/ANE path is separate); (7) manual checks on a non-NVIDIA PC
and with the pack missing or corrupted.

Risks: 2.3 GB extra download and disk; NVIDIA-only; driver-version coupling (the CUDA 12 build would not run on RTX
50 cards, the CUDA 13 build needs recent drivers); native crashes that take the tray app down; first-decode-per-length
latency; ~2.6 GB of VRAM held while the app idles in the tray (competes with games); NVIDIA EULA notices to ship.

## Later

- **Windows ML** (Windows App SDK `Microsoft.Windows.AI.MachineLearning`): Windows downloads the vendor's execution
  provider (e.g. NVIDIA TensorRT-RTX) itself, so nothing CUDA ships with Akoya. It needs sherpa-onnx running on the
  Windows ML ONNX Runtime (or our own decoder), Windows 11 24H2+, and the Windows App SDK runtime. Worth a second spike
  if GPU becomes important, e.g. if users with slow CPUs report long waits.
- Long recordings (up to the 300 s cap) are where GPU helps most (120 s: CPU 5-6 s). Chunked decoding on the CPU
  across cores might give much of that gain without a GPU; measure before choosing.

## Reproducing

```
dotnet build spike/gpu/SherpaBench -c Release
spike/gpu/SherpaBench/bin/Release/net10.0/win-x64/SherpaBench.exe <modelDir> cpu [threads]
spike/gpu/SherpaBench/bin/Release/net10.0/win-x64/SherpaBench.exe <fp16ModelDir> cuda 8 <folder with the sherpa CUDA 13 lib/*.dll + the NVIDIA wheel DLLs>
# SPIKE_LONG_SECONDS=25 sets the long clip length (default 120)
dotnet build spike/gpu/OrtBench -c Release
spike/gpu/OrtBench/bin/Release/net10.0/win-x64/OrtBench.exe <encoder.onnx> cpu|dml
```
