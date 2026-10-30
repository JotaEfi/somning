namespace Somning.Core.Engine;

public static class EngineDefaults
{
    public const string Backend = "vulkan", ModelFile = "ggml-large-v3-turbo-q5_0.bin",
        CpuFallbackModelFile = "ggml-large-v3-turbo-q8_0.bin", VadModelFile = "ggml-silero-v6.2.0.bin";
    public const bool FlashAttention = true;
    public const int Threads = 6, BudgetP95StopToPasteMs = 3000;
    public const string Language = "pt", InitialPrompt = "Olá, tudo bem? Vamos começar a reunião às 10h.";
    public const double MinDurationSeconds = 0.4;
    public const float NoSpeechThreshold = 0.6f;
}
