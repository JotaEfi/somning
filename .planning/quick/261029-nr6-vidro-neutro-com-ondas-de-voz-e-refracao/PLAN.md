---
quick_id: 261029-nr6
mode: quick
status: complete
---

# Vidro neutro, ondas de voz e refração de lente

Contexto: `.planning/quick/HANDOFF-codex-maestro.md`, lido integralmente com as referências do dono. Execução inline conforme adaptação Codex da skill gsd-quick; manter a branch atual e não incluir arquivos de estado/configuração proibidos ou mudanças preexistentes.

1. Atualizar `src/Somning.Core/Overlay/OverlayAnimator.cs`, `DropStretch.cs`, `DropPalette.cs` e `BackdropCache.cs`: ataque mais rápido, splash nos transientes, paleta neutra e textura com detalhes preservados. Manter volume, entrada/saída e captura em memória a 15 fps.
2. Atualizar `src/Somning.Platform.Windows/Overlay/DropRenderer.cs`, `CaptionPainter.cs`, `DropLayout.cs` e redução do fundo: retirar bolhas/cores decorativas de escuta, ondas cinza guiadas pelo envelope da voz, lente com reversão periférica, legenda apenas tipográfica translúcida. Preservar ciano/azul e avisos âmbar.
3. Atualizar testes comportamentais e `tools/Somning.OverlayPreview/` para fundos sintéticos com texto/relógio, documento e código. Validar build Release com warnings como erros, Core e apenas classes headless de integração. Gerar/inspecionar PNGs, medir render, revisar alterações e fazer commits por caminhos explícitos. Registrar SUMMARY e STATE, reiniciar o app sem interação de foco/teclado e informar os limites da validação.

Aceite visual final depende do dono: refração real do desktop, fluidez e resposta ao microfone não são comprovadas por PNGs.
