# Privacidade e dados do Somning 1.0

Este documento descreve o comportamento implementado. Não substitui os termos dos provedores opcionais nem define obrigações jurídicas do futuro distribuidor.

## Microfone e transcrição

O Somning captura a fala acionada pelo atalho. No modo local, o motor Whisper executa no computador. Os arquivos de áudio e os metadados de processamento podem permanecer em `%LOCALAPPDATA%\Somning\recordings`, inclusive texto transcrito e informações de recuperação. Não considere essa pasta um armazenamento efêmero nem a compartilhe sem revisão.

Groq e OpenAI são opções explícitas nas configurações. Quando uma delas é escolhida, o arquivo de áudio e o contexto de transcrição, como vocabulário configurado, são enviados ao serviço escolhido. A cobrança e o tratamento de dados pelo provedor seguem os termos da sua conta. Uma falha da nuvem não troca silenciosamente para outro provedor.

## Texto e credenciais

O texto transcrito é colocado na área de transferência e pode permanecer no histórico do Windows, caso ele esteja ativado. Com colagem automática habilitada, é enviado ao aplicativo que estava em foco.

As configurações ficam em `%LOCALAPPDATA%\Somning\settings.json`. A chave de API é protegida usando DPAPI do Windows para o usuário atual. Essa proteção não torna seguro compartilhar a pasta de dados inteira.

## Aparência da gota

Para refratar o fundo, o indicador captura uma região pequena do desktop enquanto está visível. A imagem reduzida permanece em memória; não é salva em arquivo, registrada em log nem enviada para a nuvem. A textura é descartada ao esconder o indicador. `SOMNING_OVERLAY_BACKDROP=0` desativa essa captura.

## Logs, modelos e remoção

Logs de diagnóstico ficam em `%LOCALAPPDATA%\Somning\logs`; os arquivos diários têm retenção configurada de sete arquivos. Os modelos ficam em `%LOCALAPPDATA%\Somning\models`. O download dos modelos faz conexão com os endereços fixados em `fetch-models.ps1`.

Desinstalar preserva os dados por padrão. Para excluí-los, encerre o programa e escolha conscientemente a remoção dos dados no desinstalador. A área de transferência e seu histórico são administrados pelo Windows separadamente.
