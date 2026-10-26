# Deferred items (Phase 04)

- Flaky desktop E2E (Phase 01 test, out of scope for 04-03): `PasteDeferredE2ETests.Focus_change_inside_same_window_after_capture_defers_paste` failed in 2 of 6 isolated runs (outcome `pasted` or reason `foreground_changed` instead of `focus_changed`), consistent with foreground-window loss when the desktop is not exclusively controlled. In the final verify-matrix run both live E2E tests failed 3/3 and most desktop tests were skipped; rerun on an idle, unlocked desktop before relying on PASTE-MATRIX.md.
