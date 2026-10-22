# Windows 10 published engine gate

Date: 2026-10-22T18:39:32.3717637Z
OS: 10.0.19045.0

Self-contained win-x64 directory publish copied to TEMP; PATH=C:\Windows\system32;C:\Windows; DOTNET_ROOT/DOTNET_ROOT_X64 nonexistent; DOTNET_MULTILEVEL_LOOKUP=0.

| backend | load_ms | warm_ms | infer_ms | WER | status | transcript / failure |
|---|---:|---:|---:|---:|---|---|
| vulkan | 1993.87 | 2159.39 | 465.89 | 0.00% | PASS | Hoje vamos testar o reconhecimento de voz em português do Brasil. O aplicativo deve escrever esta frase com rapidez e precisão. |
| cpu | 1529.94 | 12399.40 | 13150.74 | 0.00% | PASS | Hoje vamos testar o reconhecimento de voz em português do Brasil. O aplicativo deve escrever esta frase com rapidez e precisão. |

Deployment prerequisites: natives import MSVCP140, VCRUNTIME140, VCRUNTIME140_1, VCOMP140 and vulkan-1.dll from System32. Phase 4 must ship the VC++ redistributable; the installed GPU driver supplies Vulkan.
The transcript is an explicit regression fixture, retained here as gate evidence; operational logs exclude transcript text.
