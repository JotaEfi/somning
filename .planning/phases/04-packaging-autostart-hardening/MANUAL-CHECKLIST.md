# Phase 4 manual verification checklist

These items cannot be automated honestly on a single-user desktop. Record the result under each item
(date, tester, outcome: PASS / FAIL / N/A, notes).

## 1. Windows Terminal paste

- [ ] Focus a Windows Terminal prompt, dictate a short sentence with the hotkey.
- Expected: the text is pasted at the prompt, or Somning shows the manual-paste notice and the transcript is on the clipboard (Ctrl+V / right-click pastes it).
- Result: date ____ tester ____ outcome ____

## 2. Elevated target (real UIPI)

- [ ] Open Notepad as Administrator, focus it, dictate with the hotkey.
- Expected: Somning does not paste into the elevated window, shows the paste-deferred / elevated-target notice, and the transcript remains on the clipboard.
- Result: date ____ tester ____ outcome ____

## 3. Sleep then wake

- [ ] Put the PC to sleep, wake it, wait ~10 s, focus Notepad, dictate.
- Expected: hotkey works without restarting Somning (hook reinstalled) and the text is pasted.
- Result: date ____ tester ____ outcome ____

## 4. Lock then unlock

- [ ] Lock the session (Win+L), unlock, focus Notepad, dictate.
- Expected: hotkey works without restarting Somning and the text is pasted.
- Result: date ____ tester ____ outcome ____

## 5. Clean machine install

- [ ] On a clean Windows 10 profile or VM without the .NET SDK, run the Setup/installer from the package, launch Somning, dictate into Notepad.
- Expected: installs per-user without admin, tray icon appears, models are fetched or located, dictated text is pasted.
- Result: date ____ tester ____ outcome ____

## 6. Logon autostart

- [ ] With "start with Windows" enabled, reboot and log on.
- Expected: tray icon appears without manual launch and the hotkey works (dictate into Notepad).
- Result: date ____ tester ____ outcome ____

## Acceptance record (2026-10-29)

Reported by the phase orchestrator, which quoted the owner's message "aceito, segue para o fechamento da fase" (the executor could not
verify this message itself; the phase verifier should confirm it with the owner). Under that report, the phase closes with the
following items left MANUAL or accepted as known limitations, NOT verified by automated evidence:

- Word paste row: FAIL in the matrix (read-back returns the sentinel; see `deferred-items.md`). Known limitation, not hidden.
- Microsoft Edge: not installed on this machine (SKIPPED).
- Real UIPI against an elevated target window (SKIPPED test; policy unit tests only).
- Watchdog blocked-injection case (SKIPPED: needs an elevated foreground window).
- Windows Terminal paste (checklist item 1).
- Real sleep/wake and real lock/unlock (checklist items 3 and 4; only synthetic messages tested).
- UAC dialog behaviour.
- Clean-machine install without the .NET SDK and logon autostart after reboot (checklist items 5 and 6).
