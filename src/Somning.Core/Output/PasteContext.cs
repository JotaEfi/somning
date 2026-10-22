namespace Somning.Core.Output;

public readonly record struct PasteContext(
    bool ClipboardSet,
    bool ModifiersReleased,
    PasteTarget Current,
    PasteTarget Target,
    int OwnIntegrityRid);
