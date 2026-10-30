# Publicação inicial — Somning 1.0.0

## Estado

Preparação local da versão 1.0.0 autorizada pelo dono. Identidade inspirada na gota aprovada em 29/10/2026. Código do protótipo autorizado para publicação pública em github.com/JotaEfi/somning. Os pacotes binários continuam sendo preparados localmente.

## Conteúdo esperado do pacote

- Aplicativo Release win-x64 com runtime .NET, sem harness de testes.
- Motor na subpasta `engine` e suas dependências.
- Scripts de instalação/desinstalação e preparação dos modelos.
- Documentação de uso, instalação, privacidade e componentes de terceiros.
- Manifesto de versão e resumo SHA-256 dos artefatos de distribuição.

## Antes da publicação externa

- Confirmar o canal de distribuição.
- Definir titular/autor, contato de suporte e licença/termos de distribuição. Nenhum nome jurídico foi inventado.
- Conferir avisos/licenças redistribuíveis das dependências e dos modelos. `TERCEIROS.md` é o inventário inicial, não uma liberação jurídica.
- Os binários não têm assinatura digital: o Windows SmartScreen pode exibir um aviso ao abrir o instalador ou o aplicativo baixado. Sem certificado, não anunciar binários como assinados.
- Validar instalação em perfil ou VM limpos, sem SDK, incluindo preparação dos modelos e primeiro ditado.
- Avaliar visualmente a nova janela e o logo no Windows real e em escalas DPI diferentes.

## Limitações conhecidas herdadas

A fase de distribuição anterior foi aceita com ressalvas. A colagem automática no Microsoft Word não foi verificada. Edge não foi validado naquela máquina; Windows Terminal, colagem em alvos elevados/UIPI, comportamento UAC, suspensão real, bloqueio/desbloqueio real e inicialização após logon têm itens manuais pendentes. Isso não é apresentado como cobertura automatizada completa.

O indicador da gota foi aprovado pelo dono. Durante a fala a animação consome cerca de 10–11% de um núcleo de CPU (acima da meta antiga de 8%); os tempos medidos dependem de DPI, hardware e captura do desktop. Não prometer consumo ou latência universais.

## Reprodução

```powershell
dotnet build Somning.sln -c Release -warnaserror
dotnet test --project tests/Somning.Core.Tests -c Release
powershell -NoProfile -File tools/package.ps1
```

Não use `-WithHarness` para distribuição. Não inclua dados reais, chaves, WAVs do usuário, logs, configurações pessoais ou modelos sem verificar sua distribuição. O arquivo SUMMARY da quick task registra os resultados efetivamente obtidos e o pacote gerado.
