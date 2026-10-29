# Quick 261029-goo — Resumo

## O que mudou
- Legenda menor: `DropLayout` fonte 12,6 dip (era 18, x0,7); `CaptionPainter` altura 21, gap 7, deslize 4,2 (x0,7). Mesmo estilo.
- Entrada viscosa (so Listening; avisos/transcrevendo mantem o pop-in):
  - `Core/Overlay/DropEmergence.cs`: monte incha na borda (0-90 ms), gota puxada num pescoco que afina e rompe em 320 ms,
    caudas recolhem, monte afunda, gota passa do ponto e assenta com oscilacao amortecida. Total 640 ms.
  - `OverlayAnimator`: VoiceGate segura o audio ate assentar; IsCalm falso na entrada; gravacao nao muda.
  - `DropRenderer.Goo.cs`: SDF smooth-min gota + filamento concavo + monte, normal por gradiente, mesmo vidro.
  - `OverlayWindowManager`: janela vai ate o fundo do monitor so durante a entrada.
- Saida (dissolver) inalterada.

## Bench (entrance)
100%: 1,48 ms/quadro (8,9% de um nucleo); 150%: 2,26 ms; 200%: 2,82 ms — so por ~0,64 s.

## Previas
`previews/entrance-*.png` (a cada 50 ms, fundos escuros e claro, 2x) e `previews/caption-*-2x.png`.

## Verificacao
build Release -warnaserror 0/0; Core.Tests 367/367; Integration filtrada (OverlayAnimatedRenderTests, OverlayBackdropRenderTests) 24/24.

## Nao verificado
Visual ao vivo no monitor real (taskbar, multi-monitor, DPI misto); captura do fundo com a janela alta durante a entrada.