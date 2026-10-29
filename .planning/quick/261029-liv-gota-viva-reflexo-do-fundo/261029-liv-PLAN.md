# Quick 261029-liv: gota viva (stretch pela voz, legenda de vidro fosco, reflexo do fundo, mais realismo)

Feedback do dono sobre a gota de vidro: (1) deformar/alongar com a voz, (2) legenda "Transcrevendo" mais bonita/translucida/moderna, (3) refletir/refratar as cores do que esta atras no desktop, (4) mais realismo estilo Siri do iPhone 16.

Tarefas:
1. Core: DropStretch (elongacao com volume 3D constante, envelope ataque/queda assimetrico, transiente de fonemas, lean de lagrima, ripple), OverlayAnimator/DropFrame (Elongation, Lean, Ripple), IsCalm (30 fps quando calmo), FastMath. Testes xunit.
2. Core+Windows: backdrop injetavel (IBackdropSource, BackdropTexture/Reducer/Cache, FuncBackdropSource; ScreenBackdropSource com BitBlt + WDA_EXCLUDEFROMCAPTURE; so textura reduzida em memoria, DIB zerado, nada persistido/logado; desligavel por ctor/SOMNING_OVERLAY_BACKDROP=0).
3. DropRenderer: refracao tipo lente, rim com reflexo do entorno e dispersao cromatica, rim light duplo, oclusao interna, subsurface, microondulacoes, bolhas, sombra de contato tingida pelo fundo; CaptionPainter: pilula de vidro fosco com blur do fundo, borda fina, sombra, Segoe UI Variable/Semibold, pontos animados, fade+slide.
4. Preview (fundos falsos: papel de parede, documento claro, codigo escuro, vermelho) + bench (render e captura real); validar build -warnaserror e testes headless.
