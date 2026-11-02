# Instalação do Somning 1.0.3

## Requisitos

Windows 10 ou 11 (x64 ou ARM64), microfone funcional e espaço para o programa e modelos. O modelo local principal ocupa aproximadamente 574 MB; o pacote do aplicativo e o runtime ocupam espaço adicional. O caminho Vulkan exige hardware e driver compatíveis; o resultado em outra máquina deve ser validado antes da distribuição ampla.

## Instalador Setup.exe

Baixe `Somning-1.0.3-Setup.exe` na [release mais recente](https://github.com/JotaEfi/somning/releases/latest). O assistente instala por usuário em `%LOCALAPPDATA%\Somning\app`, cria atalhos e registra o desinstalador. Durante a instalação, o assistente baixa e verifica os modelos de voz (cerca de 1,45 GB), em uma janela com a barra de progresso. Se a conexão cair, a janela oferece nova tentativa e o download continua de onde parou. Se o download não terminar, o Somning fica instalado e o atalho **Somning - Baixar modelos** no Menu Iniciar completa depois. Os modelos e dados pessoais ficam fora da pasta do programa e são preservados na desinstalação. O instalador não tem assinatura digital.

### Qual instalador baixar

- **PC com processador Intel ou AMD (x64):** `Somning-1.0.3-Setup.exe`.
- **PC com processador ARM (Snapdragon e similares):** `Somning-1.0.3-arm64-Setup.exe`.

Em **ARM64** o processamento local usa só a CPU, porque o Whisper não tem aceleração por GPU (Vulkan) para essa arquitetura. O ditado funciona, mas é mais lento que em um PC x64 com placa de vídeo compatível. Os instaladores ARM64 e a execução no Windows 11 ainda não foram validados em hardware real.

### Instalação silenciosa

```powershell
.\Somning-1.0.3-Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SEMMODELOS=1
```

O instalador fecha o Somning e o motor sozinho se estiverem abertos. Use `/SEMMODELOS=1` para pular o download dos modelos e `/DIR="C:\Pasta"` para escolher a pasta; sem `/DIR`, uma atualização reaproveita a pasta da instalação anterior. Para gravar um log, acrescente `/LOG="C:\caminho\setup.log"`.

## Pacote ZIP alternativo

Extraia o ZIP completo em uma pasta. Preserve a subpasta `engine`: o executável principal precisa do motor e das bibliotecas que acompanham o pacote. O pacote contém scripts PowerShell (`Instalar.cmd` executa o instalador); não é um instalador Setup.exe. Como os binários não são assinados digitalmente, o Windows SmartScreen pode exibir um aviso.

No PowerShell aberto na pasta extraída:

```powershell
powershell -NoProfile -File .\install.ps1
```

O destino padrão é `%LOCALAPPDATA%\Somning\app`, com atalho no Menu Iniciar. A instalação é por usuário. Caso a política de execução do Windows bloqueie o script, revise-o e siga a política da sua organização; não altere a política global para instalar o aplicativo.

## Modelos para o processamento local

Antes do primeiro ditado, prepare os modelos (atalho: dê dois cliques em `Baixar-modelos.cmd`). O download completo (aproximadamente 1,45 GB para os três modelos) requer internet e verifica os hashes SHA-256 definidos no script. O instalador e o atalho fazem o mesmo download. Use o atalho se a instalação não tiver concluído a etapa. Na pasta extraída:

```powershell
powershell -NoProfile -File .\fetch-models.ps1 -Only 'ggml-large-v3-turbo-q5_0.bin,ggml-large-v3-turbo-q8_0.bin,ggml-silero-v6.2.0.bin'
```

Os arquivos ficam em `%LOCALAPPDATA%\Somning\models`. O modelo q8 é o fallback de CPU, usado quando o caminho Vulkan não está disponível. Após preparar os modelos, o ditado local funciona sem enviar gravações a um provedor de transcrição.

Abra Somning pelo Menu Iniciar. Clique em um campo e segure Ctrl+Shift+Espaço para ditar. O ícone da bandeja abre o menu de configurações. Para sair, use **Sair** nesse menu.

## Atualizar e remover

Feche o Somning, extraia a nova versão e execute seu `install.ps1`. Preserve uma cópia dos seus dados caso precise voltar de versão. Para remover, use o desinstalador do pacote instalado; os dados locais são preservados por padrão. O parâmetro `-PurgeData` exclui dados e só deve ser usado quando você quiser removê-los definitivamente.

Não execute instalação/desinstalação durante um ditado. Nunca distribua a pasta `%LOCALAPPDATA%\Somning` de um usuário.

## Se algo não funcionar

- Confira o microfone em Configurações e se os modelos terminaram de baixar.
- Se a colagem não ocorrer, use Ctrl+V: o texto continua na área de transferência.
- Alvos elevados podem impedir a colagem; prefira a colagem manual nesses casos.
- Consulte **Abrir pasta de logs** pelo menu da bandeja. Revise arquivos antes de compartilhá-los.
- A colagem automática no Microsoft Word não foi verificada. Veja as demais ressalvas em `RELEASE-1.0.md`.
