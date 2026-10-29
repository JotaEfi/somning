---
quick_id: 261029-ldb
status: complete
---
# Summary

Overlay is a Win32 layered window (software-rendered), not WPF, so the frame clock is a message-loop timer instead of CompositionTarget.Rendering.

- Smooth bars: time-based exponential smoothing, attack 45 ms / release 220 ms.
- Listening: idle breathing bars, pulsing dot halo and border tint, glow grows with voice energy.
- Enter/exit: fade (SourceConstantAlpha) + scale 0.92 to 1 (160 ms in, 140 ms out).
- Transcribing: cyan travelling shimmer on bars, faster pulse; notices unchanged (amber, no bars).
- No per-frame allocations (cached DIB section/DC/font, preallocated arrays); loop sleeps when hidden.
- Unchanged: WS_EX_* styles, IOverlayView, EffectRunner, paste, hotkey.

Verification: dotnet build -warnaserror clean; Core.Tests 323 pass; Overlay integration tests pass; headless render preview inspected.
Not verified visually: live motion/fps on screen, GDI text during fade, multi-monitor/DPI 150%.
