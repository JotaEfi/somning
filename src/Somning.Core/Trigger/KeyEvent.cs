namespace Somning.Core.Trigger;
public enum KeyEventKind { TriggerDown, TriggerRepeat, TriggerUp, Cancel, Confirm }
public readonly record struct KeyEvent(KeyEventKind Kind, long TimestampMs, bool IsInjectedTest);
