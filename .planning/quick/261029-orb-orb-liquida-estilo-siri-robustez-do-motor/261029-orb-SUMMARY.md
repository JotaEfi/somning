---
quick_id: 261029-orb
status: complete
---
# Summary 261029-orb

## Causa de (A) "barras nao reagem a voz"
O loop de 60 fps consumia o array corretamente (nao era threading). O problema era escala: `AudioLevelTracker.Update(rms)` recebia RMS cru (fala normal 0.01-0.2) e as barras eram `0.15 + 0.85*nivel` (~0.19 com RMS 0.05), enquanto o ripple ocioso da pilula chegava a 0.27 e `Math.Max(idle, live)` escondia a voz. Fix: `VoiceLevelNormalizer` (dBFS, piso adaptativo lento, teto adaptativo) alimenta o tracker; `AudioLevelTracker.LevelFromBars` devolve o nivel ao orb. Teste `Root_cause_...` documenta o caminho antigo; testes de pipeline garantem crescimento/deformacao visivel para RMS 0.01, 0.05, 0.2.

## (B) "Transcrevendo infinito"
- `EffectRunner.TranscribeAsync`: teto duro (120 s + 3x duracao do audio, injetavel) -> `transcribe_timeout`, retryable (WAV e sidecar preservados).
- Qualquer falha de transcricao (engine_failed/engine_retry_failed/engine_exception/timeout/cloud nao classificado) mostra aviso ambar ("Falha na transcricao, audio salvo" / "Tempo esgotado, audio salvo", 4 s) em vez de esconder em silencio. Falhas de captura/clipboard mantem o comportamento antigo.
- Hide agendado do aviso so age se ainda for um aviso (nao fecha gravacao nova).

## Orb
Gota de vidro translucida: contorno radial com harmonicos modulados pela voz, gradiente de volume, borda fresnel, sombra interna, caustica inferior, realces especulares, glow externo, bandas de cor liquidas (azul/rosa/laranja/branco; ciano/violeta em transcrevendo com espiral; ambar/vermelho no erro). Entrada elastica (spring sub-amortecida, overshoot) com borda desfocada; saida encolhe e dissolve; respira no silencio. Legenda GDI so em Transcrevendo/avisos. Janela 280x176 logicos, DPI-aware, sem foco.

CPU (Release, medido no PC em uso): ~0.8 ms/frame a 100% DPI (~4.7% de um nucleo a 60 fps), ~1.4 ms a 150% (~8%), ~1.9 ms a 200% (~11%); acima de 1.3x o orb e sombreado a 96 dpi e ampliado bilinear.

## Verificado
- `dotnet build Somning.sln -warnaserror`: 0 erros.
- Core.Tests: 366 passam. Integration: OverlayRobustness, OverlayWindowManager, EffectRunner, OverlayCaptionPreview (22) passam.
- Visual: PNGs de previa inspecionados (sequencia: entrada/overshoot, silencio, voz, transcrevendo, erro, saida; fundos escuro e claro; legenda com texto real GDI).
## Nao verificado
- Janela real na tela (UpdateLayeredWindow ao vivo, posicao com DPI real), animacao a 60 fps vista por olhos, fala real do dono no microfone, o app em execucao nao foi reiniciado/substituido.

Preview: .planning/quick/261029-orb-orb-liquida-estilo-siri-robustez-do-motor/preview/ (sheet-dark.png, sheet-light.png, close-*.png, full-*.png, upscaled-2x-*.png). Regenerar: SOMNING_PREVIEW_DIR=<dir> rodando OrbPreviewTests (Core.Tests) e OverlayCaptionPreviewTests (Integration.Tests).

Commits: 02b8441 (orb), 1fc9522 (robustez/ganho).
