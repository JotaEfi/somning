---
quick_id: 261029-ldb
type: quick
---
# Quick 261029-ldb: Polir animacao da pilula de escuta

Tasks
1. Core (pure, tested): BarSmoother (exp. attack/release), OverlayTransition (fade+scale), OverlayWaves (idle/shimmer/pulse), OverlayAnimator + OverlayFrame; xunit tests.
2. Platform: OverlayWindowManager runs a ~60 fps loop (MsgWaitForMultipleObjects + timeBeginPeriod) only while visible/fading; AA pill, dot halo, capsule bars, transcribing shimmer, cached DIB/font, opacity via SourceConstantAlpha. Window styles and paste/hotkey logic untouched.
3. Headless render tests; build -warnaserror + Core tests.
