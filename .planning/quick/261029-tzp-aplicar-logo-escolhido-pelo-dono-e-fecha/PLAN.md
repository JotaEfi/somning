# Quick 261029-tzp — Logo escolhido e pacote Somning 1.0

## Escopo

Concluir a identidade visual e regenerar o pacote 1.0.0 seguindo os handoffs de 2026-10-29. A animação e a tela de configurações estão aprovadas; preservar seu comportamento e layout. A escolha do logo pertence exclusivamente ao dono.

## Etapas

1. Reunir os boards da rodada 2 de `../somning-logos/r2/` e apresentá-los em portais Maestri. Aguardar a escolha explícita do dono antes de aplicar qualquer proposta.
2. Preservar a fonte reproduzível do logo escolhido e gerar SVG, PNG transparente e ICO com tamanhos 16, 24, 32, 48, 64, 128 e 256 em `src/Somning.Platform.Windows/Branding/`. Usar o recurso `BrandLogo` existente; conferir o ícone do executável, a extração na bandeja e o vínculo do atalho ao executável.
3. Encerrar somente Somning.App/Somning.Engine para liberar as DLLs. Executar `dotnet build Somning.sln -c Release -warnaserror`, testes Core e Integration filtrados em Settings, sem SendInput nem ações que tomem foco. Revisar o diff e os recursos gerados.
4. Regenerar o pacote com `powershell -NoProfile -File tools/package.ps1`, registrar caminho e SHA256, relançar com `dotnet run --no-build --project src/Somning.App -c Release` e informar ao dono.
5. Registrar resultados em SUMMARY.md e STATE.md. Commits atômicos com caminhos explícitos; excluir `.planning/state.json`, `.planning/config.json`, `.gsd/`, `.planning/milestone.lock` e handoffs não rastreados.

## Escolha e limpeza

- Logo: dono escolheu Codex **02 / S onda** como oficial e **01 / S fluido** como alternativa de rebranding futuro. Escolha explícita recebida antes da aplicação; OpenCode dispensado por não haver mais necessidade de proposta.
- Terminais e demais itens sem uso no Maestri: encerramento autorizado pelo dono em 2026-10-29; preservar os que ainda estão entregando propostas e os boards até a escolha.
- Pastas, branches e handoffs: só remover após autorização específica do dono, conforme handoff.
- Plano de energia: oferecer restaurar o tempo original de monitor (60 s); não mudar sem resposta.

## Verificação inicial

- HEAD recebido: `a560665`, branch `agent-engine-plan01`.
- Core Release com `--no-build`: 367 aprovados, 0 falhas (antes da aplicação do logo).
- Integration Settings: 11 aprovados, 0 falhas no runner xUnit direto (antes da aplicação do logo). Excluído `SettingsWindow_Lifecycle_OpensAndClosesGracefully`, que chama Show/Activate e pode tomar foco.
- Consulta de ajuda por `dotnet test ... -- --help` falhou no host .NET (mensagem de ajuda inesperada); a consulta e a execução pelo runner direto funcionaram.
- Boards Claude Code, Claude SVG (5 variantes) e Codex apresentados no canvas; OpenCode ainda trabalhando.
- Claude Code confirmou o encerramento de Antigravity Logo, Claude Logo e Codex Logo e o fechamento dos 6 portais antigos Config*. Arquivos preservados.
