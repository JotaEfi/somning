using Somning.Core.Settings;
using Somning.Core.Trigger;

namespace Somning.Platform.Windows.Hook;

/// <summary>Tracks physical modifiers and owns a trigger key through its final key-up.</summary>
internal struct ChordTracker
{
    private uint modifierKeys;
    private uint ownedKey;
    public readonly uint OwnedKey => ownedKey;

    private readonly KeyModifiers Modifiers =>
        ((modifierKeys & 0x007) != 0 ? KeyModifiers.Control : 0) |
        ((modifierKeys & 0x038) != 0 ? KeyModifiers.Shift : 0) |
        ((modifierKeys & 0x1C0) != 0 ? KeyModifiers.Alt : 0) |
        ((modifierKeys & 0x600) != 0 ? KeyModifiers.Windows : 0);

    public bool Resync(bool ctrlDown, bool shiftDown, bool spaceDown) =>
        Resync((ctrlDown ? KeyModifiers.Control : 0) | (shiftDown ? KeyModifiers.Shift : 0), spaceDown);

    public bool Resync(KeyModifiers physicalModifiers, bool ownedKeyDown)
    {
        modifierKeys = ((physicalModifiers & KeyModifiers.Control) != 0 ? 1u : 0) |
            ((physicalModifiers & KeyModifiers.Shift) != 0 ? 8u : 0) |
            ((physicalModifiers & KeyModifiers.Alt) != 0 ? 64u : 0) |
            ((physicalModifiers & KeyModifiers.Windows) != 0 ? 512u : 0);
        if (ownedKey != 0 && !ownedKeyDown) { ownedKey = 0; return true; }
        return false;
    }

    /// <summary>Native reinstall path preserves both sides independently, including when both are held.</summary>
    public bool ResyncPhysical(Func<int, bool> keyDown)
    {
        modifierKeys = (keyDown(0xA2) ? 2u : 0) | (keyDown(0xA3) ? 4u : 0) |
            (keyDown(0xA0) ? 16u : 0) | (keyDown(0xA1) ? 32u : 0) |
            (keyDown(0xA4) ? 128u : 0) | (keyDown(0xA5) ? 256u : 0) |
            (keyDown(0x5B) ? 512u : 0) | (keyDown(0x5C) ? 1024u : 0);
        if ((modifierKeys & 6) == 0 && keyDown(0x11)) modifierKeys |= 1;
        if ((modifierKeys & 48) == 0 && keyDown(0x10)) modifierKeys |= 8;
        if ((modifierKeys & 384) == 0 && keyDown(0x12)) modifierKeys |= 64;
        if (ownedKey != 0 && !keyDown((int)ownedKey)) { ownedKey = 0; return true; }
        return false;
    }

    public bool Process(uint key, bool down, out KeyEventKind kind) => Process(key, down, HotkeyBinding.Default, true, out kind);

    public readonly bool CanStart(uint key, HotkeyBinding binding, KeyModifiers toleratedModifiers = KeyModifiers.None) =>
        (Modifiers & binding.Modifiers) == binding.Modifiers &&
        (Modifiers & ~binding.Modifiers & ~toleratedModifiers) == 0 &&
        HotkeyBindings.TryGetVirtualKey(binding, out var trigger) && key == trigger;

    public bool Process(uint key, bool down, HotkeyBinding binding, bool enabled, out KeyEventKind kind,
        KeyModifiers toleratedModifiers = KeyModifiers.None)
    {
        kind = default;
        var bit = key switch
        {
            0x11 => 1u, 0xA2 => 2u, 0xA3 => 4u,
            0x10 => 8u, 0xA0 => 16u, 0xA1 => 32u,
            0x12 => 64u, 0xA4 => 128u, 0xA5 => 256u,
            0x5B => 512u, 0x5C => 1024u, _ => 0u
        };
        if (bit != 0)
        {
            // A resync uses the generic flag; the next physical modifier event replaces it.
            if (key is 0xA2 or 0xA3) modifierKeys &= ~1u;
            if (key is 0xA0 or 0xA1) modifierKeys &= ~8u;
            if (key is 0xA4 or 0xA5) modifierKeys &= ~64u;
            modifierKeys = down ? modifierKeys | bit : modifierKeys & ~bit;
            return false;
        }
        if (ownedKey == key)
        {
            kind = down ? KeyEventKind.TriggerRepeat : KeyEventKind.TriggerUp;
            if (!down) ownedKey = 0;
            return true;
        }
        if (ownedKey != 0 || !enabled || !down || !CanStart(key, binding, toleratedModifiers)) return false;
        ownedKey = key;
        kind = KeyEventKind.TriggerDown;
        return true;
    }
}
