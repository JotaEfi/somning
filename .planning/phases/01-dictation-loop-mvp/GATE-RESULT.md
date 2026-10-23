# Windows 10 published engine gate

Date: 2026-10-23T15:04:35.9938115Z
OS: 10.0.19045.0

Self-contained win-x64 directory publish copied to TEMP; PATH=C:\Windows\system32;C:\Windows; DOTNET_ROOT/DOTNET_ROOT_X64 nonexistent; DOTNET_MULTILEVEL_LOOKUP=0.

| backend | load_ms | warm_ms | infer_ms | WER | status | transcript / failure |
|---|---:|---:|---:|---:|---|---|
| vulkan | 2371.16 | 2978.38 | 1583.43 | 0.00% | PASS | Hoje vamos testar o reconhecimento de voz em português do Brasil. O aplicativo deve escrever esta frase com rapidez e precisão. |
| cpu | 1539.61 | 11894.77 | 12258.87 | 0.00% | PASS | Hoje vamos testar o reconhecimento de voz em português do Brasil. O aplicativo deve escrever esta frase com rapidez e precisão. |

Deployment prerequisites: natives import MSVCP140, VCRUNTIME140, VCRUNTIME140_1, VCOMP140 and vulkan-1.dll from System32. Phase 4 must ship the VC++ redistributable; the installed GPU driver supplies Vulkan.
The transcript is an explicit regression fixture, retained here as gate evidence; operational logs exclude transcript text.

## Published App layout

| check | WER | status | evidence |
|---|---:|---|---|
| installed App + Engine | 0% | PASS | sanitized PATH, self-contained win-x64, non-ASCII/space path, worker resolved at app\engine |
| Release harness | - | PASS | exit 2; harness/fault type markers absent |
