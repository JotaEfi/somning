using Somning.Core.Trigger;
namespace Somning.Platform.Windows.Hook;
internal struct ChordTracker
{
    private bool leftCtrl, rightCtrl, genericCtrl, leftShift, rightShift, genericShift, owned;
    /// <summary>
    /// Re-synchronises the tracker with the physical keyboard after a hook reinstall (the unhooked gap can miss key events)
    /// WITHOUT forgetting an in-progress chord: zeroing the tracker made a Space pressed right after a reinstall look like a
    /// plain Space (no Ctrl+Shift known), so it leaked into the focused app. Returns true when the chord we owned ended
    /// during the gap and a TriggerUp must be emitted so the interpreter does not wait for a release that was missed.
    /// </summary>
    public bool Resync(bool ctrlDown, bool shiftDown, bool spaceDown)
    {
        genericCtrl = ctrlDown; leftCtrl = rightCtrl = false;
        genericShift = shiftDown; leftShift = rightShift = false;
        if (owned && !spaceDown) { owned = false; return true; }
        return false;
    }

    public bool Process(uint key, bool down, out KeyEventKind kind)
    {
        kind = default;
        switch (key)
        {
            case 0x11: genericCtrl = down; return false;
            case 0xA2: leftCtrl = down; return false;
            case 0xA3: rightCtrl = down; return false;
            case 0x10: genericShift = down; return false;
            case 0xA0: leftShift = down; return false;
            case 0xA1: rightShift = down; return false;
        }
        if (key != 0x20) return false;
        if (owned)
        {
            kind = down ? KeyEventKind.TriggerRepeat : KeyEventKind.TriggerUp;
            if (!down) owned = false;
            return true;
        }
        if (!down || !(leftCtrl || rightCtrl || genericCtrl) || !(leftShift || rightShift || genericShift)) return false;
        owned = true; kind = KeyEventKind.TriggerDown; return true;
    }
}
