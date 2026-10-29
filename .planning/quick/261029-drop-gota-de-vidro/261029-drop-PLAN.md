# Quick 261029-drop: integrar a gota de vidro (design/claude fb9004e)

Objetivo: substituir a orb pela gota de vidro 3D do concurso, mantendo a robustez do motor (EffectRunner) e UM so ganho de voz.

- Trazer Drop*/TextMask/OverlayAnimator/Spring/VoiceLevelNormalizer/OverlayWindowManager e tools/Somning.OverlayPreview; remover Orb* e testes.
- Ganho de voz: o do Claude (no animator, dB + referencia adaptativa); o sink do EffectRunner volta a entregar RMS bruto (normaliza uma vez so).
- Legenda: avisos dentro da capsula; "Transcrevendo..." em placa escura abaixo da gota.
- Deformacao mais contida com voz alta (lobulos, stretch, raio, mais amortecimento).
- Testes: RMS 0,01/0,05/0,2 movem; ruido 0,0005-0,003 quase nao; forma contida.
