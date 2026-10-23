using System.Diagnostics;
using Somning.Core.Output;
using Somning.Core.Ports;
using Somning.Core.Text;
using Somning.Platform.Windows.Clipboard;

namespace Somning.Platform.Windows.Paste;

public sealed class TargetPaster(
    IClipboardWriter clipboard,
    ITargetProbe targetProbe,
    IIntegrityReader integrityReader,
    IModifierWaiter modifierWaiter,
    IPasteInjector pasteInjector) : ITextOutput
{
    public async Task<DeliveryResult> DeliverAsync(string text, PasteTarget target, CancellationToken ct)
    {
        var timer = Stopwatch.StartNew();
        var clipboardResult = clipboard.SetText(TextNormalizer.ForPaste(text));
        var clipboardMs = timer.ElapsedMilliseconds;
        if (!clipboardResult.Success)
            return new("clipboard_failed", clipboardResult.Error ?? "clipboard_failed", clipboardMs, 0,
                clipboardResult.SequenceAfter);

        var modifiersReleased = await modifierWaiter.WaitForReleaseAsync(ct: ct).ConfigureAwait(false);
        var current = targetProbe.Capture();
        var decision = PasteGate.Decide(new(true, modifiersReleased, current, target, integrityReader.OwnRid()));
        if (!decision.ShouldPaste)
            return new("paste_deferred", decision.DeferReason, clipboardMs, timer.ElapsedMilliseconds,
                clipboardResult.SequenceAfter);

        // Recheck after the asynchronous modifier wait and target probe, immediately before injection.
        // A user's intervening copy must never be replaced by our Ctrl+V.
        if (clipboard.GetSequenceNumber() != clipboardResult.SequenceAfter)
            return new("paste_deferred", "clipboard_changed", clipboardMs, timer.ElapsedMilliseconds,
                clipboardResult.SequenceAfter);

        var injection = pasteInjector.SendCtrlV();
        return injection.Inserted == 4
            ? new("pasted", null, clipboardMs, timer.ElapsedMilliseconds, clipboardResult.SequenceAfter)
            : new("paste_deferred", injection.CleanupSucceeded ? "send_input_partial" : "send_input_partial_cleanup_failed",
                clipboardMs, timer.ElapsedMilliseconds,
                clipboardResult.SequenceAfter);
    }

    public Task<DeliveryResult> CopyOnlyAsync(string text, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var timer = Stopwatch.StartNew();
        var result = clipboard.SetText(TextNormalizer.ForPaste(text));
        return Task.FromResult(new DeliveryResult(result.Success ? "clipboard_set" : "clipboard_failed",
            result.Success ? null : result.Error ?? "clipboard_failed", timer.ElapsedMilliseconds, 0,
            result.SequenceAfter));
    }

}
