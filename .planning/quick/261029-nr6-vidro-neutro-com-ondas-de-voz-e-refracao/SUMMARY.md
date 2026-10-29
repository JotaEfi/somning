---
status: complete
quick_id: 261029-nr6
visual_acceptance: pending-owner
---

# Vidro neutro, ondas de voz e refração

Implementação e validação técnica concluídas; aceite visual permanece com o dono.

## Mudanças

- Escuta: material de grafite/cinza, bordas e brilho neutros; removidas as três bolhas. Ondas contínuas escuras com uma linha prateada seguem o envelope RMS e transientes da voz. São amplitudes visuais suavizadas, não análise de espectro/FFT.
- Lente: conteúdo real do fundo é amostrado com magnificação central e reversão local nas bordas. Em escuta, a amostra é dessaturada. Textura de ~1,5 DIP/texel (máximo 224 por lado), sem os dois passes extras de blur; captura continua a 15 fps em memória, com descarte ao esconder e exclusão da própria janela inalterados.
- Ataque: nível de voz ~32 ms e envelope de stretch ~35 ms, mola mais rápida, maior impulso de transientes e elongação máxima de 0,62. Mantidos volume, overshoot de entrada, dissolução de saída, avisos e ganho adaptativo.
- Transcrição preserva o fluxo ciano/azul. A legenda agora usa apenas os glifos translúcidos de “Transcrevendo”, sem placa, borda ou pontos extras; fonte Segoe UI Variable Display, fallback Segoe UI, peso 350 e 18 DIP (~13,5 pt), leve bisel e sombra dos próprios glifos.
- Prévia passou a incluir relógio e texto rasterizados em fundos sintéticos. Caminho Hi-DPI das prévias agora é o mesmo usado pelo app.

## Commits de implementação

- `c30b4b3`: Core, ataque/splash e preservação de detalhes; testes de RMS/traços de texto.
- `8db9a02`: renderer, lente, ondas neutras, legenda e testes de integração.
- `3237d64`: cenários sintéticos de relógio/documento/código.
- Plano, resumo, revisão, PNGs e STATE são registrados no commit documental seguinte.

## Validação

- `dotnet build Somning.sln -c Release -warnaserror`: passou, zero avisos/erros.
- `dotnet test --project tests/Somning.Core.Tests -c Release --no-build`: 355/355.
- `dotnet test --project tests/Somning.Integration.Tests -c Release --no-build -- --filter-class '*OverlayAnimatedRenderTests' --filter-class '*OverlayRobustnessTests' --filter-class '*OverlayBackdropRenderTests' --filter-not-method '*Overlay_window*' --filter-not-method '*Real_screen_capture*'`: 29/29. Não foi executada a suíte inteira nem os métodos que abrem janela/capturam o desktop nessa classe.
- Continuidade: simulação de 20 segundos atravessa duas voltas do relógio de ripple. O teste foi executado temporariamente com a fórmula defeituosa e falhou (salto médio de 5,31 níveis de cinza/pixel); com a fase contínua ele passou. Fonte corrigida foi restaurada e rebuild/testes finais passaram.
- Duas revisões independentes (geral e C#) localizaram o mesmo salto de fase; corrigido antes do commit. Nenhum outro defeito concreto apontado. Relato em `REVIEW.md`.
- `dotnet run --no-build --project tools/Somning.OverlayPreview -c Release -- .planning/quick/261029-nr6-vidro-neutro-com-ondas-de-voz-e-refracao/preview`: 59 PNGs, todos com fundos fictícios. Inspecionados sheets de relógio/documento/código, timeline do relógio e recortes 3x de voz/transcrição. Nenhuma captura da tela real foi persistida.
- `git diff --check`: passou. O revisor C# também reportou `dotnet format --verify-no-changes` aprovado.
- A tentativa de obter ajuda via `dotnet test ... -- --help` falhou no protocolo de ajuda do SDK; consultado o runner diretamente e utilizados os filtros headless acima, com sucesso.

## Custo medido

`dotnet run --no-build --project tools/Somning.OverlayPreview -c Release -- bench all`, nesta máquina. Render+cache com fundo sintético; captura real medida separadamente, sem gravar dados. Valores são tempos de parede do render e CPU da captura, estimativas de um núcleo, sem garantia de fluidez do desktop.

| DPI | Voz: render+cache a 60 fps | Silêncio: render+cache a 30 fps | Captura real a 15 fps (CPU) |
|---|---|---|---|
| 100% | 1,699 ms/frame (~10,2%) | 1,288 ms/frame (~3,9%) | ~1,05% |
| 150% | 2,281 ms/frame (~13,7%) | 1,868 ms/frame (~5,6%) | ~0,94% |
| 200% | 2,838 ms/frame (~17,0%) | 2,568 ms/frame (~7,7%) | ~1,41% |

Em 100%, voz+có­pia estimados em ~11,3% de um núcleo, contra ~14% no handoff. A meta antiga de <8% continua não atendida durante a fala. Transcrição em 100%: ~1,696 ms/frame; aviso: ~2,946 ms/frame (usar metade dos percentuais de 60 fps quando calmos). BitBlt levou ~5,56 ms de parede por captura, então jitter no thread de render continua dependendo do desktop. Ondas são calculadas por coluna e a escuta pula o cálculo de blobs coloridos; teste de ausência de alocação por frame passou.

## App e pendências manuais

App anterior e motor foram parados antes do build. Relançado com `dotnet run --no-build --project src/Somning.App -c Release` em processo oculto; confirmados Somning.App PID 26632 e Somning.Engine PID 32060 às 17:18 de 29/10/2026.

Não verificados: aparência da refração sobre o desktop real com overlay visível, exclusão da própria gota na captura ao vivo, fluidez/jitter, resposta ao microfone real, troca de monitores/DPI e ditado/colagem com foco real. Não houve SendInput, troca de foco, Notepad ou teste interativo. Solicitar que o dono dite uma frase com Ctrl+Shift+Espaço e dê feedback visual; não declarar aceite visual sem ele.

Configuração de energia não alterada: handoff informa monitor/suspensão em Nunca na tomada e monitor anteriormente em 60 s; restauração depende da preferência do dono.

Arquivos preexistentes excluídos dos commits: `.planning/state.json` e `.planning/quick/HANDOFF-codex-maestro.md`. Também não foram incluídos config.json, .gsd/ ou milestone.lock.
