---
status: complete
---
# Resumo 261029-vdr — configurações em vidro escuro (candidata do concurso, NÃO aprovada)

- Janela 456 dip (vidro ~420), altura pelo conteúdo (~640 dip), sem rolagem; sem chrome do Windows, cantos 22, cabeçalho arrastável, botão fechar próprio, Esc fecha (Cancelar IsCancel), Enter salva (IsDefault).
- Vidro grafite com rim prata em gradiente, realce especular no topo, sombra interna, sombra externa (modo fallback); ciano #5CE1E6 só em foco, atalho, versão, toggles ligados e botão Salvar.
- Combo, toggles (CheckBox reestilizado), PasswordBox/TextBox em campos "inset" customizados. Mesmos nomes de controles: persistência, DPAPI e atalho fixo intactos.
- GlassBackdrop.cs: SetWindowCompositionAttribute ACCENT_ENABLE_ACRYLICBLURBEHIND com tint escuro + SetWindowRgn arredondado (DPI-aware); falha => preenchimento semi-opaco + sombra.
- Logo em um único recurso XAML: BrandLogo (SettingsView.xaml).
- Prévias: settings-preview.png (acrílico simulado sobre papel de parede sintético), settings-preview-fallback.png. Cópia: somning-settings/repo-wpf/mockup-1.png.
- Validação: build Release -warnaserror 0 avisos; Integration Settings 12/12 (novo teste de layout compacto); Core Settings 31/31.

Não verificado: acrílico real na tela (lag de arraste conhecido do acrílico no Win10, aparência do blur, região em DPI ≠ 100%), DWMWA_SYSTEMBACKDROP_TYPE do Win11 não usado (não funciona em janela em camadas; o accent cobre Win11 também), navegação por teclado/leitor de tela na janela real, popup do combo na tela.
