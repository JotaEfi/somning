---
status: complete
---
# Summary 261029-liv: gota viva

Tecnica
- Stretch: Core/DropStretch. Elongacao horizontal (0..0,5) com mola subamortecida (zeta 0,5), envelope ataque 60 ms / queda 300 ms, transiente rapido de fonemas, lean de lagrima pela velocidade, ripple. Volume 3D constante: largura x(1+e), altura e profundidade x1/sqrt(1+e). Silencio volta ao circulo que respira. Lobulos reduzidos (0,6x) para nao voltar ao aspecto borrachudo.
- Reflexo: IBackdropSource -> BackdropTexture reduzida (~4 dip/texel, max 96x96, 2 blurs 3x3), BackdropCache (15 fps, suaviza, degrada a 0 apos 3 falhas, Reset ao esconder). ScreenBackdropSource: BitBlt do DC da tela (sem CAPTUREBLT) + SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE) na janela; DIB zerado apos cada captura; nada persistido/logado. Desligavel: ctor(useScreenBackdrop:false) ou SOMNING_OVERLAY_BACKDROP=0. Testavel via FuncBackdropSource.
- Render: refracao tipo lente (amostra deslocada pela normal), liquido tingido pelo entorno (16%), rim fresnel com reflexo do entorno e dispersao cromatica (2 amostras), rim light duplo, oclusao interna, subsurface, microondulacoes, bolhas, sombra ampla + de contato tingida pela luminancia do fundo. Pontas elipticas organicas.
- Legenda: CaptionPainter, pilula de vidro fosco (fundo borrado, escurecido/saturado, borda clara, sombra), Segoe UI Variable Text (fallback Segoe UI) semibold, pontos animados, fade + slide.
- Correcao: delegate do WndProc agora estatico (instancias anteriores coletadas crashavam a criacao da proxima janela).
- Custo: IsCalm -> 30 fps em silencio/transcrevendo/aviso.

CPU medido (bench, 1 thread, esta maquina; o drop anterior media 7-8%)
- 100% DPI render+cache a 60 fps: silencio 9,6%, voz alta 12,5%, transcrevendo 12,1%, aviso 19,2% (de 1 nucleo). Em calmo (30 fps): ~4,8% / 6% / 9,6%.
- Captura real da tela: 5,7 ms de parede (espera do DWM) mas 0,86 ms de CPU => 1,3% a 15 fps (100%), 1,9% (150%), 2,6% (200%).
- Meta <8% atendida so em estados calmos; falando (60 fps) fica ~14% com captura. Nao reduzi a resolucao interna para nao amolecer a gota.

Nao verificado: reflexo real no desktop vivo (so fundos falsos + captura real isolada no bench), WDA_EXCLUDEFROMCAPTURE com a janela visivel, multimonitor/DPI reais, jitter dos 5,7 ms de BitBlt no thread de render, 30 fps calmo ao vivo, Somning.App nao reconstruido (processo aberto bloqueia a DLL).
