# Componentes de terceiros

Inventário inicial baseado nos projetos e em `Directory.Packages.props`. Os componentes mantêm seus próprios termos. Antes da distribuição pública, conferir e incluir os textos de licença/avisos efetivamente exigidos pelos arquivos redistribuídos; este inventário não substitui esses textos.

| Componente | Versão no projeto | Uso |
|---|---|---|
| .NET / Windows Desktop runtime | Resolução no build self-contained | Execução do app |
| Whisper.net e runtimes CPU/Vulkan | 1.9.1 | Transcrição local e bibliotecas nativas |
| NAudio.Core / NAudio.Wasapi | 3.3.0 | Captura de áudio |
| Serilog | 4.4.0 | Diagnóstico |
| Serilog.Sinks.File | 7.0.0 | Logs em arquivo |
| System.Security.Cryptography.ProtectedData | 10.0.13 | DPAPI |
| Microsoft.Windows.CsWin32 | 0.3.367 | Interop gerado em compilação |

xUnit é ferramenta de testes e não deve integrar o pacote de produção. Os modelos Whisper e Silero são baixados separadamente; suas origens, revisões, tamanhos e hashes estão em `fetch-models.ps1`. A identidade do Somning não altera as licenças dos componentes ou modelos.
