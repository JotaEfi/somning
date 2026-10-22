namespace Somning.Core.Output;

public static class PasteGate
{
    public static PasteDecision Decide(PasteContext ctx)
    {
        if (!ctx.ClipboardSet)
        {
            return new PasteDecision(false, "clipboard_failed");
        }

        if (ctx.Target.ForegroundHwnd == 0)
        {
            return new PasteDecision(false, "no_target");
        }

        if (!ctx.ModifiersReleased)
        {
            return new PasteDecision(false, "modifiers_held");
        }

        if (ctx.Current.ForegroundHwnd != ctx.Target.ForegroundHwnd || ctx.Current.ProcessId != ctx.Target.ProcessId)
        {
            return new PasteDecision(false, "foreground_changed");
        }

        if (!ctx.Target.FocusQueried || !ctx.Current.FocusQueried || ctx.Target.FocusHwnd == 0 || ctx.Current.FocusHwnd == 0)
        {
            return new PasteDecision(false, "focus_unknown");
        }

        if (ctx.Current.FocusHwnd != ctx.Target.FocusHwnd)
        {
            return new PasteDecision(false, "focus_changed");
        }

        if (!ctx.Target.IntegrityRid.HasValue)
        {
            return new PasteDecision(false, "integrity_unknown");
        }

        if (ctx.Target.IntegrityRid.Value > ctx.OwnIntegrityRid)
        {
            return new PasteDecision(false, "target_elevated");
        }

        return new PasteDecision(true);
    }
}
