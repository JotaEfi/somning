using Somning.Core.Output;

namespace Somning.Core.Ports;

public readonly record struct DeliveryResult(
    string Outcome,
    string? Reason,
    long ClipboardMs,
    long PasteMs,
    uint ClipboardSequence);

public interface ITargetProbe
{
    PasteTarget Capture();
}

public interface ITextOutput
{
    Task<DeliveryResult> DeliverAsync(string text, PasteTarget target, CancellationToken ct);
    Task<DeliveryResult> CopyOnlyAsync(string text, CancellationToken ct);
}
