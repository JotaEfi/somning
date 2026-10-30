---
status: planned
---
# Configurações em vidro escuro translúcido, compactas

Pedido do dono: configurações "meio translúcidas e com sombreado parecido com a gota", menores (não tomar a tela), criativas, no estilo preto escuro da gota.

1. `SettingsView.xaml`: janela ~440 dip, altura pelo conteúdo, sem rolagem; sem chrome do Windows (WindowStyle=None, AllowsTransparency, cantos arredondados), cabeçalho arrastável, botão fechar próprio. Vidro grafite quase preto, rim prata em gradiente, realce especular no topo, sombra interna e sombra externa suave; ciano #5CE1E6 só em foco/realces. Combo, toggles (CheckBox reestilizado como chave), senha e botões customizados. Logo em um único recurso (`BrandLogo`) fácil de trocar.
2. `GlassBackdrop.cs`: SetWindowCompositionAttribute ACCENT_ENABLE_ACRYLICBLURBEHIND com tint escuro + região arredondada (SetWindowRgn, ciente de DPI); fallback para preenchimento sólido quase-preto semi-opaco com sombra externa.
3. Manter todos os controles, persistência, DPAPI, atalho fixo exibido, Tab/Esc/Enter, DPI.
4. Prévia PNG off-screen sobre papel de parede sintético com desfoque simulado; inspecionar e iterar.
5. Validar: build Release -warnaserror; testes Core/Integration filtrados em Settings. Commits por caminho explícito, SUMMARY e STATE. Relançar o app em background.
