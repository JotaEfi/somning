---
status: fixed
depth: quick
reviewers: [code-reviewer, csharp-reviewer]
critical: 0
high: 0
medium: 1
remaining: 0
---

# Revisão da quick 261029-nr6

Escopo: alterações em Core/Overlay, Platform.Windows/Overlay, testes de Backdrop/OverlayAnimation/OverlayBackdropRender e cenários OverlayPreview. Revisores independentes leram o diff; não editaram fontes nem executaram ações de desktop. A revisão C# também verificou build e formatação.

## MEDIUM — salto periódico nas ondas (corrigido)

A fórmula nova `Time * 3.8 + RipplePhase * 0.6` combinava harmônicos fracionários com RipplePhase, que subtrai 16π periodicamente. A volta do contador não corresponde a um período inteiro daqueles harmônicos; fala sustentada podia produzir um salto visual a cada ~8,7 segundos.

Correção: a geometria das ondas agora usa apenas Time contínuo, já acelerado pela voz. Ripple permanece responsável pela amplitude e espessura. A fase antiga continua disponível para os efeitos legados.

Regressão: `Long_utterance_keeps_wave_motion_continuous_across_the_ripple_clock_wrap` simula 20 segundos, exige pelo menos duas voltas e compara pixels entre frames vizinhos. Foi demonstrada falha com a fórmula antiga (5,31 níveis de cinza/pixel) e sucesso com a correção. Build final e as 29 integrações headless passaram.

Os dois revisores não apontaram outros defeitos concretos de C#, buffers, premultiplicação, DPI, alocações ou privacidade. O aceite visual, desempenho ao vivo e foco real continuam manuais.
