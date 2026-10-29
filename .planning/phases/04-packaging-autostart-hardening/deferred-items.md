# Deferred items (Phase 04)

- Flaky desktop E2E `PasteDeferredE2ETests.Focus_change_inside_same_window_after_capture_defers_paste` (failed 2 of 6 isolated runs: outcome `pasted` or reason `foreground_changed` instead of `focus_changed`).
  - **Root cause (hypothesis, pending empirical confirmation in plan 04-06 Task 3):** the test called `TwoEditWindow.FocusEdit(1)` and released the delivery barrier immediately. `FocusEdit` marshals to the window's own thread, so the paste check could run (a) before focus actually moved -> `pasted`, or (b) after another window took the foreground -> `foreground_changed`. The initial capture also used the window-only `EnsureForeground`, so the captured focused control was not guaranteed to be edit 0.
  - **Fix (04-06 Task 1):** initial state uses `EnsureForegroundFocus` plus `WaitUntilFocused(0)` (honest skip via `Requires.That`, fail in strict mode); after `FocusEdit(1)` the test polls (`TwoEditWindow.WaitUntilFocused`, 3 s) for focus handle == edit 1 AND foreground == test window before releasing the barrier; assertion messages now include `ForegroundHelper.Describe` (foreground title/class, focused control). The expected reason stays `focus_changed`; no retries.
  - **Status:** fixed in code and compiled; NOT yet re-run (interactive). Confirmation happens in the final interactive block (04-06 Task 3).

- Word paste-matrix row (`PasteTargetMatrixTests` id `word`): FAIL in 3 interactive runs (read-back returned the sentinel, nothing pasted).
  - **Evidence (screenshots, 04-06):** Word opens the command-line `.docx` with the File > Open backstage ("Abrir") on top, in "Modo de Compatibilidade"; Ctrl+V therefore never reaches the document body.
  - **Tried without success:** Escape before pasting; launching without `/n`; launching without `/q`.
  - **Applied (compiled, NOT yet verified interactively):** a fuller `.docx` (settings.xml with `compatibilityMode=15`, relationships part), plus a precondition that the focused control is Word's document body (`_WwG`). When it is not, the row is reported SKIPPED with the reason (not a flaky FAIL). Strict mode (`SOMNING_STRICT=1`) still fails it.
  - **Open:** a reliable way to put focus in the body (UI Automation or a click) is not implemented. Until then the Word row cannot be PASS.
