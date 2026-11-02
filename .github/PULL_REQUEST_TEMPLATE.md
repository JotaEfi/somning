## O que muda

<!-- Descreva a mudança e o porquê, em poucas linhas. Um pull request, um assunto. -->

## Tipo

- [ ] Correção de bug (`fix`)
- [ ] Recurso novo (`feat`)
- [ ] Estilo de gota novo
- [ ] Refatoração, desempenho ou manutenção
- [ ] Documentação ou testes

## Como foi testado

<!-- Comandos, testes que você rodou e o que viu na prática. -->

- Windows (versão): 
- Arquitetura: <!-- x64 / ARM64 / não testei em ARM64 -->

## Lista de verificação

- [ ] `dotnet build Somning.sln -c Release -warnaserror` termina com 0 avisos e 0 erros
- [ ] Os testes do Core passam
- [ ] Código novo tem teste, e nenhum teste existente foi enfraquecido ou apagado
- [ ] Sem segredos, chaves, gravações ou logs pessoais nos arquivos
- [ ] Sem instruções específicas de uma arquitetura (o código roda em x64 e ARM64)
- [ ] `CHANGELOG.md` e a documentação foram atualizados, se a mudança é visível para quem usa

## Estilo de gota (só se for o caso)

- [ ] A bolha de fala tem a faixa escura reativa no centro
- [ ] A bolha de transcrição é a mesma gota, no mesmo estilo
- [ ] `DropStyleContractTests` passa para o novo estilo
