# Quick 261029-goo: entrada viscosa da gota e legenda 30% menor

## Pedido do dono
1. Legenda "Transcrevendo" 30% menor (fonte/glifos x0,7), mesmo estilo, posicao ajustada proporcionalmente.
2. Nova entrada ao acionar o atalho: a gota sai da borda inferior do monitor como gosma viscosa (estilo Venom):
   monte na borda, pescoco/filamento que afina e rompe (pinch-off), gota assenta com oscilacao elastica.
   450-700 ms, gravacao nao atrasa, audio so deforma depois de assentar. Saida (dissolver) inalterada.

## Tarefas
1. Core: `DropEmergence` (linha do tempo pura, deterministica) + campos novos em `DropFrame`
   (Rise, Squash, Goo, Neck, TopTail, BottomTail, Pool); `OverlayAnimator` usa a emergencia so no modo Listening,
   segura o audio (VoiceGate) e nao reporta IsCalm durante a entrada. Testes xunit.
2. Render: `DropRenderer.Goo.cs` — campo SDF com smooth-min (gota + pescoco concavo + monte na borda), normal pelo
   gradiente, profundidade pelo raio local, mesmo `ShadeGlass`; pixels fora do alcance da gosma usam `ShadePixel`
   original. Sombra de contato aparece so quando a gota se solta.
3. Janela: durante a entrada a janela/buffer se estende ate a borda inferior do monitor (redimensiona so no inicio e
   no fim); estilos nao-ativavel/click-through inalterados.
4. Legenda: fonte 18 -> 12,6 dip, altura 30 -> 21, gap 10 -> 7, deslize 6 -> 4,2.
5. Previas (tira a cada 50 ms, fundos escuro/claro, 2x, legenda) e bench da entrada.

## Verificacao
build Release -warnaserror; Core.Tests; Integration filtrada (OverlayAnimatedRenderTests, OverlayBackdropRenderTests).
