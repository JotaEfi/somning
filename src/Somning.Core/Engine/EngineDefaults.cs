namespace Somning.Core.Engine;

public static class EngineDefaults
{
    /// <summary>Vulkan GPU on x64; ARM64 has no Vulkan runtime in Whisper.net, so it starts on the CPU directly.</summary>
    public static string Backend { get; } =
        System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "cpu" : "vulkan";

    public const string ModelFile = "ggml-large-v3-turbo-q5_0.bin",
        CpuFallbackModelFile = "ggml-large-v3-turbo-q8_0.bin", VadModelFile = "ggml-silero-v6.2.0.bin";
    public const bool FlashAttention = true;
    public const int Threads = 6, BudgetP95StopToPasteMs = 3000;
    public const string Language = "pt", InitialPrompt = "Olá, tudo bem? Vamos começar a reunião às 10h.";
    public const double MinDurationSeconds = 0.4;
    public const float NoSpeechThreshold = 0.6f;

    /// <summary>
    /// Model file a worker should load. The CPU fallback uses the q8 model only when it is installed (it is optional
    /// and not downloaded by default); otherwise it reuses the required q5 model, so a restart never depends on an optional file.
    /// </summary>
    public static string ModelFileFor(string backend, string modelsDir) =>
        backend == "cpu" && System.IO.File.Exists(System.IO.Path.Combine(modelsDir, CpuFallbackModelFile))
            ? CpuFallbackModelFile
            : ModelFile;
}
