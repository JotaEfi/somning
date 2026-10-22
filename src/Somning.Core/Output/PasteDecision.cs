namespace Somning.Core.Output;

public readonly record struct PasteDecision(
    bool ShouldPaste,
    string? DeferReason = null);
