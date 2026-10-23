namespace Somning.Core.Trigger;
public enum KeyEventKind { TriggerDown, TriggerRepeat, TriggerUp, Cancel }
public readonly record struct KeyEvent(KeyEventKind Kind, long TimestampMs, bool IsInjectedTest);
