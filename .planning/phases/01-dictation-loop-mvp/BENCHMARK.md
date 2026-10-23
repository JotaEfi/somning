# Somning Published Engine Benchmark (STT-08)

- **Date**: 2026-10-23 07:26:07 UTC
- **Machine**: `JOTA` (12 logical processors)
- **OS**: `Microsoft Windows NT 10.0.19045.0`
- **Engine Commit**: `3345817a272afbc3d816414d5ad629463d77c834`
- **Engine SHA256**: `ffb80083275d1ae2c89ea736eea5dd4fdddb6978c46f510ac74f6fa6ea2483c1`
- **Dataset / Fixture**: `speech/longo-numeros.wav` (15 s clip, Portuguese pt-BR)

> Deployment prerequisites: natives import MSVCP140, VCRUNTIME140, VCRUNTIME140_1, VCOMP140 and vulkan-1.dll from System32. Phase 4 must ship the VC++ redistributable; the installed GPU driver supplies Vulkan.

## Full Matrix Results (12 Configurations)

| Config | Backend | Model | Flash Attention | Runs | Load (ms) | Warm (ms) | Infer p50 (ms) | Infer p95 (ms) | Stop-to-Paste p50 (ms) | Stop-to-Paste p95 (ms) | Mean Clean WER |
|---|---|---|:---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `vulkan-f16-on` | vulkan | f16 | on | 10 | 1776.4 | 1436.7 | 1643.5 | 1709.1 | 1737.8 | 1793.2 | 0.00% |
| `vulkan-f16-off` | vulkan | f16 | off | 10 | 1928.3 | 4554.9 | 3312.1 | 3414.6 | 3403.1 | 3515.8 | 0.00% |
| `vulkan-q8_0-on` | vulkan | q8_0 | on | 10 | 1288.5 | 1767.4 | 1598.9 | 1694.0 | 1685.3 | 1990.1 | 0.00% |
| `vulkan-q8_0-off` | vulkan | q8_0 | off | 10 | 1114.7 | 3115.7 | 3291.1 | 3408.5 | 3336.0 | 3579.6 | 16.67% |
| `vulkan-q5_0-on` | vulkan | q5_0 | on | 10 | 890.3 | 2053.4 | 1532.9 | 1590.2 | 1615.4 | 1665.0 | 0.00% |
| `vulkan-q5_0-off` | vulkan | q5_0 | off | 10 | 896.5 | 3229.5 | 3232.6 | 3260.9 | 3310.6 | 3343.7 | 0.00% |
| `cpu-f16-on` | cpu | f16 | on | 10 | 1973.5 | 12838.3 | 13277.3 | 14417.1 | 13344.6 | 14500.3 | 0.00% |
| `cpu-f16-off` | cpu | f16 | off | 10 | 1378.2 | 15573.4 | 15884.1 | 16309.8 | 15935.7 | 16376.1 | 0.00% |
| `cpu-q8_0-on` | cpu | q8_0 | on | 10 | 1196.7 | 10645.0 | 11504.2 | 12324.0 | 11580.0 | 12369.8 | 0.00% |
| `cpu-q8_0-off` | cpu | q8_0 | off | 10 | 880.1 | 14206.0 | 14926.3 | 15481.1 | 14968.6 | 15534.6 | 0.00% |
| `cpu-q5_0-on` | cpu | q5_0 | on | 10 | 795.3 | 14205.3 | 14794.3 | 14997.3 | 14839.6 | 15131.7 | 0.00% |
| `cpu-q5_0-off` | cpu | q5_0 | off | 10 | 602.2 | 20632.3 | 18416.2 | 19527.6 | 18462.2 | 19583.9 | 0.00% |

## Flash Attention Comparison

| Backend | Model | Infer p95 (FA ON) | Infer p95 (FA OFF) | Speedup | Stop-to-Paste p95 (FA ON) | Stop-to-Paste p95 (FA OFF) |
|---|---|---:|---:|---:|---:|---:|
| vulkan | f16 | 1709.1 ms | 3414.6 ms | 2.00x | 1793.2 ms | 3515.8 ms |
| vulkan | q8_0 | 1694.0 ms | 3408.5 ms | 2.01x | 1990.1 ms | 3579.6 ms |
| vulkan | q5_0 | 1590.2 ms | 3260.9 ms | 2.05x | 1665.0 ms | 3343.7 ms |
| cpu | f16 | 14417.1 ms | 16309.8 ms | 1.13x | 14500.3 ms | 16376.1 ms |
| cpu | q8_0 | 12324.0 ms | 15481.1 ms | 1.26x | 12369.8 ms | 15534.6 ms |
| cpu | q5_0 | 14997.3 ms | 19527.6 ms | 1.30x | 15131.7 ms | 19583.9 ms |

## Transcript Equality Notes

- All clean test fixtures (`cmd-curto`, `longo-numeros`, `tecnico`, `sim`, `e-ai`, `hoje`) were verified across models and flash attention settings.
- Example reference transcript (`speech/longo-numeros.wav`):
  > *"Abra o arquivo de configuração e salve as alterações."*

## Selected

### Primary Default: `vulkan-q5_0-on`

- **Backend**: `vulkan`
- **Model**: `q5_0` (file: `ggml-large-v3-turbo-q5_0.bin`)
- **Flash Attention**: `on`
- **Mean Clean WER**: `0.00%`
- **Latency Stop-to-Paste (15 s clip)**: p50 = `1615.41 ms`, p95 = `1664.98 ms`

### STT-08 Latency Budget

- **Budget Specification**: p95 stop-to-paste <= 3.0 s for a 15 s utterance on the selected config
- **Measured Budget**: p50 = `1615.41 ms`, p95 = `1664.98 ms` (comfortably below 3000 ms threshold)

### CPU Fallback: `cpu-q8_0-on` (degraded mode (fallback only))

- **Backend**: `cpu`
- **Model**: `q8_0` (file: `ggml-large-v3-turbo-q8_0.bin`)
- **Flash Attention**: `on`
- **Mean Clean WER**: `0.00%`
- **Latency Stop-to-Paste**: p50 = `11580.00 ms`, p95 = `12369.81 ms`

