namespace Somning.Core.Trigger;

public readonly record struct TriggerOptions(
    int TapThresholdMs = 300,
    int MissedKeyUpMs = 1000,
    int MaxRecordingMs = 300000)
{
    public TriggerOptions() : this(300, 1000, 300000) { }
}
