using Somning.Core.Dictation;
using Somning.Core.Trigger;

namespace Somning.App.Dictation;

/// <summary>
/// Milestone raised by <see cref="DictationHost.Notified"/> on the loop task.
/// Kind is one of hook_installed | engine_ready | target_captured | transcribe_started | outcome.
/// </summary>
public sealed record DictationNotification(string Kind, string? Id, DictationMetrics? Metrics);

/// <summary>Loop-internal: a hook key event, converted to TriggerFired on the loop (review H8).</summary>
public sealed record KeyReceived(KeyEvent Event) : DictationEvent;

/// <summary>Loop-internal: the 50 ms timer only samples the clock and modifier state; OnTick runs on the loop.</summary>
public sealed record Tick(long NowMs, bool ModifiersDown) : DictationEvent;

/// <summary>Loop-internal: a notification posted by an adapter, raised from the loop.</summary>
public sealed record NotificationPosted(DictationNotification Notification) : DictationEvent;
