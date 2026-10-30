---
status: complete
---
# Somning 1.0 — S onda oficial e pacote final

## Escolha do dono

Em 2026-10-29, o dono aprovou as duas propostas Codex da rodada 2: **02 / S onda** como logo oficial e **01 / S fluido** para eventual rebranding futuro. Aplicação feita somente após essa decisão explícita.

## Entrega

- `Branding/somning-logo.png`: exportação 512 x 512 aprovada, preservada byte a byte (SHA256 `2745c8caf32b7dec7d9b7dfe62d1db3de92f0feea68cdee8965c4c8ba704366e`).
- `Branding/somning.ico`: 16, 24, 32, 48, 64, 128 e 256 px com alfa. Ícone incorporado ao exe, extraído pela bandeja e usado pelo atalho através do exe. Extração do executável publicado em `package-icon.png` inspecionada.
- `Branding/somning-logo.svg`: contêiner com o PNG aprovado embutido, documentado como raster.
- `Branding/Sources/`: original e exportação aprovada do S onda. `Branding/Future/`: original e exportação 512 do S fluido, independentes da pasta temporária do concurso. Não entram como recursos do aplicativo.
- `Generate-Brand.ps1` regenera os recursos a partir da fonte aprovada; README registra escolha, origem e uso.
- `SettingsView.xaml` preserva o recurso BrandLogo e o layout aprovado; só comentário e descrição acessível atualizados. Animação aprovada preservada.
- Commit de implementação: `72923f4`.

## Validação

- `dotnet build Somning.sln -c Release -warnaserror`: zero avisos e erros.
- Core Release, `dotnet test --project tests/Somning.Core.Tests --no-build -c Release`: **367/367**.
- Integration, runner xUnit direto, namespace `Somning.Integration.Tests.Settings`: **11/11**. Excluído `SettingsWindow_Lifecycle_OpensAndClosesGracefully`, que chama Show/Activate, para respeitar a regra de não tomar foco. Suíte completa e SendInput não executados.
- Prévia WPF offscreen `settings-onda.png` inspecionada com o logo oficial.
- Revisão somente leitura de Claude Code #2: sem bloqueios. Detalhes e observações de baixa severidade em REVIEW.md.
- Consulta de ajuda pelo host `dotnet test ... -- --help` encontrou erro de protocolo .NET; runner direto funcionou. Nenhuma mudança de produto motivada por isso.

## Pacote final

Gerado com `powershell -NoProfile -File tools/package.ps1` após encerrar o aplicativo e publicar os recursos atuais.

- Caminho: `publish/Somning-1.0.0-win-x64.zip`.
- Tamanho: **139596977 bytes**.
- SHA256: `ac801b2a68032252a4b954e563cbb18990c3a910b8c249df80db9c66fe450e1d`.
- Sidecar: `publish/Somning-1.0.0-win-x64.zip.sha256`, conferido contra o ZIP.
- 644 entradas; presença do app, biblioteca WPF, engine, manifesto e instalador confirmada. Os nomes ZIP produzidos pelo Compress-Archive usam separador Windows; validação normalizou os separadores.
- Manifesto: versão 1.0.0, Release/win-x64, self-contained; harness false; modelos não incluídos; pacote sem assinatura, conforme fluxo existente.
- Publicado em 2026-10-30T00:41:27Z (2026-10-29 no fuso do dono).

## Aplicativo e Maestri

Relançado com `dotnet run --no-build --project src/Somning.App -c Release`, em processo oculto; Somning.App e Somning.Engine confirmados em execução na saída Release deste checkout. Não foi feita reinstalação sobre a configuração pessoal do dono.

Limpeza de terminais e itens sem uso do Maestri autorizada pelo dono: encerrados Antigravity Logo, Claude Logo, Codex Logo, OpenCode Logo e Claude Code #2 após a revisão; fechados os seis portais antigos Config* e os três boards de seleção após a escolha. `maestri list` confirmou somente Codex Maestro, sem agentes/notas/portais conectados.

Pastas temporárias, branches de design e handoffs permanecem no disco até autorização específica. O plano de energia herdado continua sem alteração; tempo original de monitor informado no handoff: 60 s.
