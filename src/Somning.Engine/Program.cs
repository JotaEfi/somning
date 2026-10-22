using System.Globalization;
using System.Text;
using Somning.Engine;
using Somning.Engine.Protocol;
using Whisper.net.LibraryLoader;
using Whisper.net.Logger;

Console.InputEncoding = Console.OutputEncoding = new UTF8Encoding(false);
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
var outputLock = new object();
void Write(EngineMessage message) { lock (outputLock) { Console.WriteLine(JsonLines.Serialize(message)); Console.Out.Flush(); } }
EngineOptions options;
try { options = EngineOptions.Parse(args); }
catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
{ Write(new ErrorMessage(null, ErrorCodes.BadRequest, "Invalid command line.")); return 2; }
if (!File.Exists(options.Model) || !File.Exists(options.Vad))
{ Write(new ErrorMessage(null, ErrorCodes.ModelMissing, "Required model missing.")); return 3; }
RuntimeOptions.RuntimeLibraryOrder = options.Backend == "vulkan" ? [RuntimeLibrary.Vulkan] : [RuntimeLibrary.Cpu];
var observer = new GpuLogObserver();
using var logger = LogProvider.AddLogger((level, message) =>
{
    observer.Observe(message);
    // Native diagnostic lines are operational only; decoding results are never enabled.
    if (message is not null) Console.Error.Write(message);
});
WhisperHost host;
try { host = new WhisperHost(options); await host.WarmAsync(); }
catch (Exception) { Write(new ErrorMessage(null, ErrorCodes.BackendInitFailed, "Backend initialization failed.")); return 4; }
using (host)
using (var executor = new InferenceExecutor(host, options, observer, Write))
{
#if SOMNING_TEST_HARNESS
    await FaultInjection.DelayReadyAsync();
#endif
    Write(new ReadyMessage(options.Backend, Path.GetFileName(options.Model), host.LoadMs, host.WarmMs,
        observer.GpuInUse, options.FlashAttention));
    var control = new ControlReader(options, executor, Write);
    var reader = new Thread(control.Run) { IsBackground = true, Name = "engine-control" };
    reader.Start();
    await executor.RunAsync();
}
return 0;
