using System.Collections.Concurrent;
using System.Threading.Channels;
using Somning.Core.Trigger;
using Somning.Platform.Windows.Hook;

namespace Somning.Integration.Tests.Support;

public sealed class HookTriggerLoop : IAsyncDisposable
{
    private readonly ChannelReader<KeyEvent> reader;
    private readonly TriggerInterpreter interpreter;
    private readonly CancellationTokenSource cts = new();
    private readonly Task loopTask;

    public ConcurrentQueue<(long TimeMs, TriggerResult Result)> Results { get; } = new();

    public HookTriggerLoop(ChannelReader<KeyEvent> reader, TriggerOptions? options = null)
    {
        this.reader = reader;
        this.interpreter = new TriggerInterpreter(options);
        this.loopTask = Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        var token = cts.Token;

        // Each pending wait is kept until it completes: PeriodicTimer allows only one
        // outstanding waiter, and an abandoned ReadAsync would swallow a key event.
        Task<KeyEvent>? readTask = null;
        Task<bool>? timerTask = null;

        try
        {
            while (!token.IsCancellationRequested)
            {
                readTask ??= reader.ReadAsync(token).AsTask();
                timerTask ??= timer.WaitForNextTickAsync(token).AsTask();

                var completed = await Task.WhenAny(readTask, timerTask);

                long now = Environment.TickCount64;

                if (completed == readTask)
                {
                    if (!readTask.IsCompletedSuccessfully)
                    {
                        break;
                    }

                    var keyEvent = await readTask;
                    readTask = null;
                    var result = interpreter.OnKey(keyEvent);
                    if (result.Command != TriggerCommand.None)
                    {
                        Results.Enqueue((now, result));
                    }
                }
                else
                {
                    await timerTask;
                    timerTask = null;
                    bool modifiersDown = PhysicalKeyState.AreChordModifiersDown();
                    var result = interpreter.OnTick(now, modifiersDown);
                    if (result.Command != TriggerCommand.None)
                    {
                        Results.Enqueue((now, result));
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        cts.Cancel();
        try
        {
            await loopTask;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cts.Dispose();
        }
    }
}
