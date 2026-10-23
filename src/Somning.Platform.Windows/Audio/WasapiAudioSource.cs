using System.Diagnostics;
using System.Runtime.ExceptionServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Somning.Core.Ports;

namespace Somning.Platform.Windows.Audio;

public sealed class WasapiAudioSource : IAudioSource
{
    private static readonly TimeSpan FirstBufferTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);
    private readonly object lifecycle = new();
    private readonly object callbackGate = new();
    private readonly byte[] convertedBuffer = new byte[8_192];
    private readonly TaskCompletionSource firstData = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource recordingStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private WasapiRecorder? recorder;
    private BufferedWaveProvider? inputBuffer;
    private IWaveProvider? converter;
    private IPcmSink? sink;
    private MMDevice? selectedDevice;
    private Task? stopTask;
    private int faultRaised;
    private bool started;
    private bool disposed;

    /// <summary>
    /// <paramref name="deviceId"/> selects a capture endpoint for this source; <c>null</c>, blank or a device that
    /// disappeared falls back to the Windows default (AUD-02).
    /// </summary>
    public WasapiAudioSource(string? deviceId = null) =>
        DeviceId = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId.Trim();

    /// <summary>Normalized endpoint id, or <c>null</c> when this source uses the default device.</summary>
    public string? DeviceId { get; }


    public event Action<float>? Level;
    public event Action<AudioFault>? Faulted;

    /// <summary>
    /// Builds the recorder for the selected endpoint. A stale/removed device id must never break dictation, so any
    /// failure while opening it retries once with the Windows default device.
    /// </summary>
    private WasapiRecorder BuildRecorder()
    {
        if (DeviceId is not { } id)
            return CreateBuilder().Build();

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDevice(id);
            try
            {
                var activeRecorder = CreateBuilder().WithDevice(device).Build();
                selectedDevice = device;
                return activeRecorder;
            }
            catch
            {
                device.Dispose();
                throw;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return CreateBuilder().Build();
        }
    }

    private static WasapiRecorderBuilder CreateBuilder() => new WasapiRecorderBuilder().WithBufferLength(30);

    public async Task<AudioStartInfo> StartAsync(IPcmSink destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        cancellationToken.ThrowIfCancellationRequested();
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (started || stopTask is not null)
                throw new InvalidOperationException("Audio source can only be started once.");
            started = true;
        }

        var startupWatch = Stopwatch.StartNew();
        WasapiRecorder activeRecorder;
        try
        {
            activeRecorder = BuildRecorder();
        }
        catch (Exception exception)
        {
            throw new AudioDeviceUnavailableException("no_device", "No default audio capture device is available.", exception);
        }

        WaveFormat format;
        BufferedWaveProvider buffered;
        IWaveProvider converted;
        try
        {
            format = activeRecorder.WaveFormat;
            buffered = new BufferedWaveProvider(format, TimeSpan.FromSeconds(5))
            {
                ReadFully = false,
                DiscardOnBufferOverflow = false
            };
            converted = CaptureConverter.Create(buffered);
        }
        catch (Exception setupException)
        {
            try
            {
                await activeRecorder.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException("Audio setup failed and recorder cleanup also failed.", setupException, cleanupException);
            }
            ExceptionDispatchInfo.Capture(setupException).Throw();
            throw;
        }

        Exception? recordingStartFailure = null;
        var stoppedDuringSetup = false;
        lock (lifecycle)
        {
            if (disposed || stopTask is not null)
            {
                stoppedDuringSetup = true;
            }
            else
            {
                recorder = activeRecorder;
                inputBuffer = buffered;
                converter = converted;
                sink = destination;
                activeRecorder.DataAvailable += OnDataAvailable;
                activeRecorder.RecordingStopped += OnRecordingStopped;
                try
                {
                    activeRecorder.StartRecording();
                }
                catch (Exception exception)
                {
                    activeRecorder.DataAvailable -= OnDataAvailable;
                    activeRecorder.RecordingStopped -= OnRecordingStopped;
                    recorder = null;
                    inputBuffer = null;
                    converter = null;
                    sink = null;
                    recordingStartFailure = exception;
                }
            }
        }

        if (stoppedDuringSetup)
        {
            await activeRecorder.DisposeAsync().ConfigureAwait(false);
            throw new OperationCanceledException("Capture was stopped before the microphone started.", cancellationToken);
        }

        if (recordingStartFailure is not null)
        {
            await activeRecorder.DisposeAsync().ConfigureAwait(false);
            throw new AudioDeviceUnavailableException("no_device", "The default audio capture device could not start.", recordingStartFailure);
        }
        try
        {
            await FirstDataGuard.WaitAsync(firstData.Task, FirstBufferTimeout, cancellationToken).ConfigureAwait(false);
            return new AudioStartInfo(startupWatch.ElapsedMilliseconds, format.ToString());
        }
        catch (Exception startException)
        {
            try
            {
                await StopAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException("Audio startup failed and capture cleanup also failed.", startException, cleanupException);
            }

            ExceptionDispatchInfo.Capture(startException).Throw();
            throw;
        }
    }

    public Task StopAsync()
    {
        lock (lifecycle)
            return stopTask ??= StopCoreAsync();
    }

    private async Task StopCoreAsync()
    {
        WasapiRecorder? activeRecorder;
        lock (lifecycle)
        {
            activeRecorder = recorder;
            if (activeRecorder is null) return;
        }

        Exception? stopFailure = null;
        try
        {
            activeRecorder.StopRecording();
            await recordingStopped.Task.WaitAsync(StopTimeout).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            stopFailure = exception;
            RaiseFault(new AudioFault("device_lost", $"Capture did not stop cleanly: {exception.Message}"));
        }

        try
        {
            lock (callbackGate)
                _ = DrainConvertedAudio();
        }
        catch (Exception exception)
        {
            stopFailure ??= exception;
            RaiseFault(new AudioFault("callback_error", exception.Message));
        }
        finally
        {
            activeRecorder.DataAvailable -= OnDataAvailable;
            activeRecorder.RecordingStopped -= OnRecordingStopped;
            try
            {
                await activeRecorder.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                stopFailure ??= exception;
                RaiseFault(new AudioFault("device_lost", exception.Message));
            }

            lock (lifecycle)
            {
                recorder = null;
                inputBuffer = null;
                converter = null;
                sink = null;
            }
        }

        if (stopFailure is not null)
            ExceptionDispatchInfo.Capture(stopFailure).Throw();
    }

    private void OnDataAvailable(ReadOnlySpan<byte> data, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        Exception? callbackFailure = null;
        lock (callbackGate)
        {
            if (inputBuffer is null || sink is null || Volatile.Read(ref disposed) || stopTask is not null)
                return;

            try
            {
                CaptureConverter.AppendPacket(inputBuffer, data, flags);
                if (DrainConvertedAudio())
                    firstData.TrySetResult();
            }
            catch (Exception exception)
            {
                callbackFailure = exception;
                firstData.TrySetException(exception);
            }
        }

        if (callbackFailure is not null)
            StopAfterCallbackFailure(callbackFailure);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        if (args.Exception is { } exception)
        {
            firstData.TrySetException(new AudioDeviceUnavailableException("device_lost", "The audio capture device stopped unexpectedly.", exception));
            RaiseFault(new AudioFault("device_lost", exception.Message));
        }
        recordingStopped.TrySetResult();
    }

    private bool DrainConvertedAudio()
    {
        var currentConverter = converter;
        var currentSink = sink;
        if (currentConverter is null || currentSink is null) return false;

        var wrotePcm = false;
        int bytesRead;
        while ((bytesRead = currentConverter.Read(convertedBuffer.AsSpan())) > 0)
        {
            if ((bytesRead & 1) != 0)
                throw new InvalidDataException("Converted PCM ended in the middle of a sample.");
            var pcm = convertedBuffer.AsSpan(0, bytesRead);
            currentSink.Write(pcm);
            Level?.Invoke(ComputeRms(pcm));
            wrotePcm = true;
        }
        return wrotePcm;
    }

    private void StopAfterCallbackFailure(Exception callbackFailure)
    {
        var activeRecorder = recorder;
        Exception? stopFailure = null;
        try
        {
            activeRecorder?.StopRecording();
        }
        catch (Exception exception)
        {
            stopFailure = exception;
        }

        var message = stopFailure is null
            ? callbackFailure.Message
            : $"{callbackFailure.Message}; stopping capture also failed: {stopFailure.Message}";
        RaiseFault(new AudioFault("callback_error", message));
    }

    private void RaiseFault(AudioFault fault)
    {
        if (Interlocked.Exchange(ref faultRaised, 1) == 0)
            Faulted?.Invoke(fault);
    }

    private static float ComputeRms(ReadOnlySpan<byte> pcm)
    {
        double squares = 0;
        var samples = pcm.Length / sizeof(short);
        for (var index = 0; index < pcm.Length; index += sizeof(short))
        {
            var sample = (short)(pcm[index] | (pcm[index + 1] << 8));
            var normalized = sample / 32768d;
            squares += normalized * normalized;
        }
        return samples == 0 ? 0 : (float)Math.Sqrt(squares / samples);
    }

    public async ValueTask DisposeAsync()
    {
        lock (lifecycle)
        {
            if (disposed) return;
            disposed = true;
        }
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            selectedDevice?.Dispose();
            selectedDevice = null;
        }
    }
}


