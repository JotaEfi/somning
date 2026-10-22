using Somning.Core.Trigger;
namespace Somning.Platform.Windows.Hook;
internal struct ChordTracker
{
    private bool leftCtrl, rightCtrl, genericCtrl, leftShift, rightShift, genericShift, owned;
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
