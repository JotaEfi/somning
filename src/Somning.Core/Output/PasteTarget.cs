namespace Somning.Core.Output;

public readonly record struct PasteTarget(
    nint ForegroundHwnd,
    nint FocusHwnd,
    uint ProcessId,
    uint ThreadId,
    bool FocusQueried,
    int? IntegrityRid);
