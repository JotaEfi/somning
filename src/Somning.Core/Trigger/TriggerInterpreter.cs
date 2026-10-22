namespace Somning.Core.Trigger;

public sealed class TriggerInterpreter
{
    private readonly TriggerOptions options;
    private bool isKeyDown;
    private long keyDownTimestamp;
    private long recordingStartTimestamp;
    private long lastTriggerActivityTimestamp;
    private long? modifiersUpSince;

    public TriggerMode Mode { get; private set; } = TriggerMode.Unknown;
    public bool IsRecording { get; private set; }

    public TriggerInterpreter(TriggerOptions? options = null)
    {
        this.options = options ?? new TriggerOptions();
    }

    public TriggerResult OnKey(KeyEvent e)
    {
        switch (e.Kind)
        {
            case KeyEventKind.TriggerDown:
                if (!IsRecording)
                {
                    IsRecording = true;
                    Mode = TriggerMode.Unknown;
                    isKeyDown = true;
                    keyDownTimestamp = e.TimestampMs;
                    recordingStartTimestamp = e.TimestampMs;
                    lastTriggerActivityTimestamp = e.TimestampMs;
                    modifiersUpSince = null;
                    return new TriggerResult(TriggerCommand.StartRecording, TriggerMode.Unknown);
                }

                if (Mode == TriggerMode.Toggle)
                {
                    IsRecording = false;
                    var prevMode = Mode;
                    Mode = TriggerMode.Unknown;
                    isKeyDown = true;
                    return new TriggerResult(TriggerCommand.StopRecording, prevMode, StopReason.Tap);
                }

                if (isKeyDown)
                {
                    lastTriggerActivityTimestamp = e.TimestampMs;
                    return new TriggerResult(TriggerCommand.None, Mode);
                }

                return new TriggerResult(TriggerCommand.None, Mode);

            case KeyEventKind.TriggerRepeat:
                if (IsRecording)
                {
                    lastTriggerActivityTimestamp = e.TimestampMs;
                }
                return new TriggerResult(TriggerCommand.None, Mode);

            case KeyEventKind.TriggerUp:
                if (!isKeyDown)
                {
                    return new TriggerResult(TriggerCommand.None, Mode);
                }

                isKeyDown = false;

                if (!IsRecording)
                {
                    return new TriggerResult(TriggerCommand.None, Mode);
                }

                if (Mode == TriggerMode.Unknown)
                {
                    long duration = e.TimestampMs - keyDownTimestamp;
                    if (duration < options.TapThresholdMs)
                    {
                        Mode = TriggerMode.Toggle;
                        return new TriggerResult(TriggerCommand.None, TriggerMode.Toggle);
                    }

                    Mode = TriggerMode.Hold;
                    IsRecording = false;
                    var prevMode = Mode;
                    Mode = TriggerMode.Unknown;
                    return new TriggerResult(TriggerCommand.StopRecording, prevMode, StopReason.Release);
                }

                return new TriggerResult(TriggerCommand.None, Mode);

            default:
                return new TriggerResult(TriggerCommand.None, Mode);
        }
    }

    public TriggerResult OnTick(long nowMs, bool chordModifiersDown)
    {
        if (!IsRecording)
        {
            return new TriggerResult(TriggerCommand.None, Mode);
        }

        if (nowMs - recordingStartTimestamp >= options.MaxRecordingMs)
        {
            var prevMode = Mode == TriggerMode.Unknown ? TriggerMode.Hold : Mode;
            IsRecording = false;
            Mode = TriggerMode.Unknown;
            isKeyDown = false;
            modifiersUpSince = null;
            return new TriggerResult(TriggerCommand.StopRecording, prevMode, StopReason.MaxLength);
        }

        if (Mode != TriggerMode.Toggle)
        {
            if (chordModifiersDown)
            {
                modifiersUpSince = null;
            }
            else
            {
                modifiersUpSince ??= nowMs;
                if ((nowMs - modifiersUpSince.Value >= options.MissedKeyUpMs) ||
                    (nowMs - lastTriggerActivityTimestamp >= options.MissedKeyUpMs))
                {
                    var prevMode = Mode == TriggerMode.Unknown ? TriggerMode.Hold : Mode;
                    IsRecording = false;
                    Mode = TriggerMode.Unknown;
                    isKeyDown = false;
                    modifiersUpSince = null;
                    return new TriggerResult(TriggerCommand.StopRecording, prevMode, StopReason.MissedKeyUp);
                }
            }
        }

        return new TriggerResult(TriggerCommand.None, Mode);
    }
}
