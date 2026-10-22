namespace Somning.Core.Trigger;

public enum TriggerCommand
{
    None,
    StartRecording,
    StopRecording
}

public enum TriggerMode
{
    Unknown,
    Toggle,
    Hold
}

public enum StopReason
{
    Tap,
    Release,
    MissedKeyUp,
    MaxLength
}

public readonly record struct TriggerResult(TriggerCommand Command, TriggerMode Mode, StopReason? Reason = null);
