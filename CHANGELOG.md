# Histórico de versões

## 1.0.8 — Iniciar com o Windows (2026-11-07)

- **Iniciar com o Windows:** a opção voltou a funcionar. O Somning só gravava a entrada de inicialização ao mudar a opção; se algo a apagasse, a opção seguia marcada mas o Somning não abria com o Windows nem aparecia no Gerenciador de Tarefas. Agora, ao abrir, ele recria a entrada sempre que a opção está ligada. Desativar o Somning pela aba **Inicializar** do Gerenciador de Tarefas continua sendo respeitado.
- **Gerenciador de Tarefas:** o Somning aparece como **Somning** (antes "Somning.App"), também na coluna Fornecedor.
- **Testes:** os testes que usam a área de trabalho de verdade ficaram separados e podem rodar no GitHub Actions, sem disputar o PC com quem está usando (`tools/testes.ps1`).

## 1.0.7 — fechamento sem travar (2026-11-04)

- **Sons:** corrigido um travamento ao fechar ou atualizar o Somning. A saída de áudio dos sons era encerrada enquanto a reprodução esperava pelo mesmo bloqueio, e o aplicativo podia ficar preso para sempre. Também deixa de travar quando o dispositivo de áudio some (por exemplo, ao desconectar o fone).

## 1.0.6 — instalador, menu da bandeja e cancelamento na nuvem (2026-11-04)

- **Instalador:** os modelos de voz são conferidos pelo SHA-256 depois de salvos; se não puderem ser salvos ou verificados, o instalador avisa e explica como baixar depois. Corrigido o comando do PowerShell exibido nessa mensagem, que saía quebrado.
- **Menu da bandeja:** o menu é criado uma única vez e reaproveitado, em vez de abrir uma thread nova a cada clique; fechar o Somning encerra o menu com segurança.
- **Nuvem:** cancelar uma transcrição na nuvem não é mais tratado como "tempo esgotado", então o ditado para na hora.

## 1.0.5 — atualização automática e correções (2026-11-02)

- **Atualização automática:** o Somning confere uma vez por dia (e pelo menu da bandeja, **Verificar atualizações**) se há uma versão nova no GitHub. Se houver, pergunta; ao aceitar, baixa o instalador, confere o SHA-256 publicado pelo GitHub, atualiza em silêncio e abre o Somning de novo. Nunca instala sem confirmação e nunca avisa durante um ditado. Pode ser desligada em Configurações → **Avisar sobre atualizações**. Para ser oferecida, a release precisa ser estável (não pré-lançamento) e trazer `Somning-X.Y.Z-Setup.exe` (e `-arm64-`).
- **Instalador:** os modelos de voz são baixados por uma página do próprio instalador, sem abrir o PowerShell. Se falhar, a mensagem ensina o atalho e o comando.
- **Sons:** sem estalo no fim (fade-out e saída de áudio sempre aberta) e o Bipe padrão agora toca em máquinas onde o `Console.Beep` era mudo.
- **Configurações:** vidro mais transparente, mudanças aplicadas na hora (sem Salvar) e o atalho de ditado funciona com a janela aberta.
- **Idioma do teclado:** "Manter o idioma do teclado ao usar o atalho" agora grava o layout no primeiro Ctrl/Shift do atalho, antes de o Windows poder trocá-lo, e restaura esse layout. Nada é gravado no sistema, então a troca de idioma do Windows volta ao normal ao fechar ou desinstalar.
- **Sons:** corrigido o estalo no fim dos sons dos temas Gota, Vidro e Madeira (os arquivos tinham um corte abrupto no decaimento, agora suavizado ao tocar).
- **Instalador:** o instalador reconhece os modelos já baixados (não baixa de novo) e o desinstalador remove a entrada de "Iniciar com o Windows".

## 1.0.3 — instalador corrigido e sons

- O instalador volta a baixar os modelos durante a instalação, em janela com a barra de progresso. O download retoma de onde parou se a conexão cair, e a instalação oferece nova tentativa. Se o download não terminar, o atalho **Somning - Baixar modelos** completa depois.
- Nova opção **Manter o idioma do teclado ao usar o atalho**, desligada por padrão. Não altera nenhuma configuração do Windows.
- Temas de som: bipe padrão (novo padrão), gota d'água, vidro e madeira. Som também ao concluir a transcrição.
- Configurações: fundo mais escuro no modo de desfoque, menus suspensos sem borda preta e desfoque do fundo limitado à forma arredondada.
- **Cinco estilos de gota**, escolhidos em **Configurações → Estilo da gota** e aplicados na hora: Clássica (padrão), Gel reativo, Mercúrio, Lente líquida e Água escura. Todos mantêm a faixa escura reativa no centro e têm uma bolha de transcrição do mesmo estilo.
- A gota responde ao **tom** da voz e ao **impacto** de cada início de fala, além do volume: o corpo se achata mais nas laterais, desliza para o lado conforme o tom e ondas viajam da borda ao centro a cada fonema.
- **Windows 11 e ARM64:** instaladores separados (`Somning-1.0.3-Setup.exe` para x64 e `Somning-1.0.3-arm64-Setup.exe` para ARM64). Em ARM64 o ditado local roda na CPU, sem Vulkan. Ainda não validado em hardware real.
- **Instalador:** a instalação silenciosa passa a fechar o Somning e o motor se estiverem abertos; antes abortava depois de 32 segundos com o aplicativo em execução. O desinstalador faz o mesmo.
- Configurações: mais espaço no seletor de tema de sons e nova linha **Estilo da gota**.
- `tools/package.ps1` e `tools/setup.ps1` aceitam `-Arch x64|arm64`.
- Documentação: README com a galeria dos estilos, guia de instalação para as duas arquiteturas e `.gitignore`/`.gitattributes` revisados.
- Projeto: `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `SECURITY.md`, modelos de issue e de pull request e integração contínua (compila sem avisos, roda os testes do Core e os de renderização da gota e das Configurações, e confere a publicação para ARM64).

## 1.0.2 — retirada

- Versão retirada: o instalador bloqueava a instalação quando o download dos modelos falhava. Use a versão 1.0.3.

## 1.0.1 — atalhos configuráveis e polimento (2026-10-30)

- Combinações independentes para ativar o ditado, cancelar e confirmar a gravação, com validação e atualização ao salvar.
- Confirmar encerra a captura atual para transcrever; cancelar descarta a gravação. Padrões: Ctrl+Shift+Espaço, Esc e Ctrl+Enter.
- Configurações com recorte nativo arredondado, vidro aproximadamente 40% mais transparente e título Somning.
- Registro da bandeja com novas tentativas e recuperação após reinício do Explorer.
- Menu da bandeja limitado a uma instância, com fechamento por ação, Esc e perda de ativação.
- Instalador por usuário, ZIP do aplicativo, código-fonte separado e SHA256 na release Latest.
## 1.0.0 — protótipo inicial (2026-10-29)

- Ditado local em pt-BR com Whisper, atalho global com modos segurar/alternar e processamento em processo separado.
- Texto na área de transferência e colagem automática configurável.
- Provedores opcionais Groq e OpenAI, com chave protegida pelo Windows.
- Configurações de microfone, sons, vocabulário e início com o Windows.
- Indicador de voz em vidro neutro, ondas reativas, efeito de lente e transcrição ciano/azul.
- Identidade da gota, ícones e nova apresentação das configurações.
- Pacote Windows x64 com runtime, scripts de instalação/desinstalação e documentação de primeiro uso e privacidade.

Primeira versão do protótipo. A aparência nativa e a fluidez em monitores de alta frequência seguem em validação com o dono. Os pacotes são preparados localmente. Veja `docs/RELEASE-1.0.md` para limitações e verificações pendentes.
