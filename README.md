# Somning

Ditado por voz em português do Brasil para Windows. Pressione o atalho, fale e leve o texto ao aplicativo em que você está trabalhando.

Versão **1.0.0**, Windows 10 x64. A interface e a marca seguem a gota de vidro do indicador de voz: grafite, ondas e acentos ciano.

## Usar

1. Inicie o Somning pelo Menu Iniciar. Ele fica na bandeja do Windows.
2. Clique em um campo de texto. Segure **Ctrl + Shift + Espaço**, fale e solte para transcrever. Também é possível usar o modo de alternância com toque curto no atalho.
3. O texto fica na área de transferência e, com a opção de colagem automática ativada, é colado no campo ativo. Use Ctrl+V se a colagem automática não ocorrer.
4. Abra **Configurações** pelo ícone da bandeja para escolher microfone, sons, colagem, início com o Windows, vocabulário e provedor de transcrição.

O processamento local usa Whisper. Groq e OpenAI são alternativas opcionais e exigem sua própria chave. Ao selecionar um serviço de nuvem, o áudio é enviado ao provedor escolhido.

## Instalar e conhecer os dados

- [Instalação e primeiro uso](docs/INSTALACAO.md)
- [Privacidade e armazenamento local](docs/PRIVACIDADE.md)
- [Notas da versão](CHANGELOG.md)
- [Preparação da publicação 1.0 e limitações conhecidas](docs/RELEASE-1.0.md)
- [Componentes de terceiros](docs/TERCEIROS.md)

O pacote de produção inclui o runtime .NET; o usuário não precisa instalar o SDK. Os modelos de voz não vêm no pacote: execute `Baixar-modelos.cmd` antes do primeiro ditado (veja a instalação). Os binários não são assinados digitalmente, então o Windows SmartScreen pode mostrar um aviso. Não copie seus arquivos de configurações, gravações, logs ou chaves para o pacote público.

## Compilar

Requer o SDK definido em `global.json` e Windows para os projetos de plataforma.

```powershell
dotnet build Somning.sln -c Release -warnaserror
dotnet test --project tests/Somning.Core.Tests -c Release
powershell -NoProfile -File tools/package.ps1
```

Não distribua builds Debug nem pacotes criados com `-WithHarness`. Os comandos de integração que movimentam foco/teclado devem ser executados apenas em um desktop separado ou explicitamente liberado para testes.

A licença de distribuição do código e a identificação do titular serão definidas pelo responsável pela publicação; este repositório não concede uma licença aberta por inferência.
