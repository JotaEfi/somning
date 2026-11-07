# Como contribuir com o Somning

Obrigado pelo interesse! O Somning é um ditado por voz em português do Brasil para Windows, feito para um uso pessoal e simples. Contribuições são bem-vindas, desde que mantenham essa simplicidade: pouca interface, sem recursos que não façam parte do ditado.

Ao participar, você concorda em seguir o [Código de Conduta](CODE_OF_CONDUCT.md).

## Antes de começar

- **Procure primeiro** nas [issues](https://github.com/JotaEfi/somning/issues) se o assunto já foi tratado.
- Para **uma mudança grande** (novo recurso, mudança de comportamento, novo estilo de gota), abra uma issue antes e conte a ideia. Isso evita trabalho perdido.
- Para **bugs**, use o modelo de issue e informe a versão do Windows e a **arquitetura (x64 ou ARM64)**. Não envie gravações pessoais, chaves de API nem logs sem revisar: eles podem conter trechos do que você ditou.
- Vulnerabilidades **não** vão em issues públicas. Veja a [política de segurança](SECURITY.md).

## Preparando o ambiente

Você precisa de Windows 10 ou 11 e do SDK do .NET indicado em `global.json`.

```powershell
git clone https://github.com/JotaEfi/somning.git
cd somning
dotnet build Somning.sln -c Release -warnaserror
.\tests\Somning.Core.Tests\bin\Release\net10.0\Somning.Core.Tests.exe
```

O build trata **avisos como erros**: ele deve terminar com 0 avisos e 0 erros. Os modelos de voz não vêm no repositório; use `tools/fetch-models.ps1` se for rodar o aplicativo.

Para gerar os instaladores, veja a seção [Desenvolvimento](README.md#desenvolvimento) do README (`tools/package.ps1` e `tools/setup.ps1`, com `-Arch x64` ou `-Arch arm64`).

## Testes

- **Todo código novo precisa de teste.** Comece pelo teste, veja-o falhar e então implemente.
- Os testes do **Core** (`Somning.Core.Tests`) não dependem do Windows e são a primeira linha de defesa.
- Os testes de **integração** renderizam a gota e as Configurações. Os marcados com `[Trait("Category", "Desktop")]` usam a área de trabalho de verdade (foco de janela, teclado, área de transferência, app aberto) e falham se você mexer no PC enquanto rodam. Para rodar tudo, use `powershell -NoProfile -File tools/testes.ps1`, que pergunta onde rodar esses testes:
  1. **pular** e rodar só o resto localmente (pode usar o PC);
  2. **no GitHub Actions** (workflow *Testes de área de trabalho*), em paralelo com o resto local (pode usar o PC; exige o commit enviado ao GitHub);
  3. **localmente**, junto com o resto (não use o PC até terminar).
- Um teste novo que use foco, teclado, área de transferência ou abra o app precisa da marca `Desktop`.
- Não enfraqueça nem apague um teste para fazer uma mudança passar. Se um teste está errado, explique o motivo na descrição do pull request.

## Estilo de código

- C# moderno, com *nullable* ligado. Siga o estilo do código ao redor e rode `dotnet format` nos arquivos que você alterar.
- **Prefira imutabilidade**: devolva uma cópia nova em vez de alterar o objeto recebido.
- Funções pequenas (menos de 50 linhas) e arquivos coesos (menos de 800 linhas). Evite aninhamento profundo e valores mágicos: use constantes nomeadas.
- Trate erros de forma explícita; nunca engula uma exceção em silêncio.
- **Nada de segredos no código.** Chaves ficam protegidas por DPAPI no usuário do Windows.
- O código roda em **x64 e ARM64**: não use instruções específicas de uma arquitetura sem uma alternativa.

## Estilos da gota

Um estilo novo é um arquivo `DropRenderer.Style.<Id>.cs` que pinta **a gota de fala e a de transcrição**, mais o registro em `DropStyles.cs` e no despachante `DropRenderer.Styles.cs`. O contrato de `DropStyleContractTests` vale para todos e exige, entre outras coisas, a **faixa escura reativa no centro** e uma bolha de transcrição que seja a mesma gota. O passo a passo está no [README](README.md#adicionar-um-estilo-de-gota).

## Mensagens de commit

Use o formato `tipo: descrição`, com um corpo opcional explicando o porquê.

| Tipo | Quando usar |
| :--- | :--- |
| `feat` | funcionalidade nova |
| `fix` | correção de bug |
| `refactor` | mudança interna sem alterar o comportamento |
| `docs` | documentação |
| `test` | testes |
| `perf` | desempenho |
| `ci` / `chore` | automação e manutenção |

## Pull requests

1. Faça um *fork* e crie uma branch a partir de `main`.
2. Mantenha o pull request **focado em um assunto**.
3. Confirme que o build passa sem avisos e que os testes passam.
4. Preencha o modelo do pull request, informando como você testou e **em qual Windows e arquitetura**.
5. Atualize o `CHANGELOG.md` e a documentação quando a mudança for visível para quem usa o programa.

Se você não puder testar em ARM64 ou no Windows 11, diga isso na descrição. Hoje esses dois cenários ainda não foram validados em hardware real, e essa informação ajuda.
