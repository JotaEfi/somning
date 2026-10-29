---
quick_id: 261029-orb
type: quick
---
# Quick 261029-orb: Orb liquida estilo Siri + robustez do motor

Feedback do dono: (A) barras nao reagem a voz; (B) overlay fica "Transcrevendo" para sempre quando o motor falha.

Tasks
1. Robustez (B): teto duro de transcricao no EffectRunner (transcribe_timeout, recuperavel), aviso ambar para qualquer falha de transcricao (EngineFailed / TranscribeTimeout, "audio salvo"), hide de aviso nao fecha gravacao nova.
2. Causa de (A): RMS real de microfone (0.01-0.2) passava linear para barras 0.15 + 0.85*nivel, abaixo do ripple ocioso (0.27). Fix: VoiceLevelNormalizer (dBFS, piso/teto adaptativos) no sink do overlay; testes com niveis realistas.
3. Orb: Core puro (OrbAnimator, Spring, OrbShape, OrbPalette, OrbRenderer, OrbLayout), OverlayWindowManager desenha orb + legenda GDI, previa PNG headless inspecionada.
4. Validacao: dotnet build Somning.sln -warnaserror, Core.Tests, testes de overlay/EffectRunner headless.
