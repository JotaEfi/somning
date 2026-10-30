# Inicializacao instalada e menu da bandeja

## Diagnostico
Processo instalado e engine na sessao1, hashes identicos ao pacote. Antes do reinicio, Shell_NotifyIconGetRect retornou E_FAIL para HWND SomningTray e watchdog registrava hook probe missed/reinstalacoes. Reiniciada a MESMA instalacao: ShellGetRect retornou retangulo valido e motor pronto. Causa completa da falha de teclado inicial nao determinada; nenhuma tecla foi injetada pelo agente para testar ditado. Fotos posteriores do dono confirmam bandeja acessivel, mas revelaram multiplos menus persistentes.

## Correcoes
- Registro da bandeja no thread proprietario, cinco tentativas limitadas e erro explicito no inicio se todas falham.
- Janela top-level oculta recebe TaskbarCreated e restaura registro apos reinicio do Explorer.
- Recursos nativos liberados no finally do thread; WM_CLOSE solicita quit sem corrida com destruicao do HWND.
- Gate atomico limita popup a uma instancia. Popup recebe foreground/foco apenas quando aberto pelo usuario. Fecha por selecao, Esc e WM_ACTIVATE inativo; dispatcher encerra e libera gate em finally.
- Dispose encerra tambem dispatcher do menu.

## Verificacao
Build Release warnaserror sem avisos/erros. Core367 aprovados. Tray5 aprovados, incluindo retries transitorios, falha permanente limitada, registro real e repeticao de abertura. Sem SendInput, sem testes que tomem foco no desktop do dono. Revisao independente code-reviewer APPROVE apos corrigir dois MEDIUM de cleanup/desligamento; nenhum bloqueio restante. Fechamento real ao clicar fora e atalho ainda precisam da confirmacao do dono.

## Entrega
Commits1fcd76c e622bf63. ZIP e Setup locais regenerados e revisao instalada por cima da instalacao existente com dados preservados. App instalado relancado. Publicacao da revisao no GitHub aguardando validacao do comportamento pelo dono; release anterior nao substituida nesta tarefa. Branding nao rastreado preservado.