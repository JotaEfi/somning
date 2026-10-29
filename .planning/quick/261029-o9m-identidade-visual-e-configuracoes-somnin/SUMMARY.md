---
status: complete
---
# Quick 261029-o9m: identidade visual, configurações e preparação 1.0

Iniciada pelo Codex (cota esgotada) e concluída pelo Claude. O dono aprovou a animação da gota da quick 261029-nr6 ("gostei, ficou muito bom") e pediu logo, configurações melhores e a versão 1.0. Durante a execução ele retirou o "registro do aplicativo" do escopo; a entrada em Aplicativos e recursos (HKCU Uninstall) que o Codex havia rascunhado foi removida.

## Commits
- 9b60ce2 feat: janela de configurações em WPF com a identidade da gota, logo e ícone na bandeja
- 072adc8 build: versão 1.0.0, manifesto, ícone do executável, pacote de produção, instalação e desinstalação mais seguras
- 55579f5 docs: README, INSTALACAO, PRIVACIDADE, RELEASE-1.0, CHANGELOG, TERCEIROS

## O que mudou
- SettingsView.xaml(.cs) substitui a janela Win32: vidro grafite com acentos ciano, logo, cartões Atalho / Microfone / Motor e nuvem / Comportamento, teclas de acesso, foco visível. Persistência mantida (microfone, provedor, chave DPAPI com máscara, vocabulário, colar automático, sons, iniciar com o Windows). O atalho exibido vem de HotkeyDisplay e mostra o atalho efetivo, Ctrl + Shift + Espaço. Como o hook não lê UserSettings.Hotkey, a janela informa que o atalho é fixo nesta versão.
- SettingsWindowManager hospeda a janela WPF num dispatcher STA próprio.
- Logo em SVG e PNG, .ico com tamanhos de 16 a 256. O ícone aparece no exe, na bandeja e no atalho do Menu Iniciar.
- Correções de build: UseWPF na Platform removeu o using implícito de System.IO (foi adicionado <Using> explícito). UseWPF saiu do App. O pacote ProtectedData saiu porque já vem no WindowsDesktop.
- O desinstalador só remove atalhos e a chave Run quando eles apontam para o diretório alvo, e só apaga o diretório quando encontra o marcador installation.json.

## Validação
- dotnet build Somning.sln -c Release -warnaserror: 0 avisos.
- Core.Tests: 360/360. Integration filtrado em Settings.*: 11/11, com renderização off-screen e sem mostrar janela.
- Prévias settings-preview.png e logo-preview.png nesta pasta, inspecionadas.
- tools/package.ps1: gera publish/Somning-1.0.0-win-x64.zip mais o .sha256. O texto exclusivo do harness não aparece no Somning.App.dll. FileVersion 1.0.0.0.
- install.ps1 e uninstall.ps1 testados em %TEMP%\Somning teste instalação çã\app dir, sem atalho nem autostart. A instalação e a remoção funcionaram, e o atalho real, a chave Run e os modelos ficaram intactos.

## Não verificado / pendências
- Janela de configurações aberta de verdade no desktop (navegação por Tab, DPI 125–150%): avaliação do dono.
- Colagem no Word, Windows Terminal, UAC, suspensão e bloqueio reais continuam como verificações manuais. A animação usa ~10–11% de um núcleo de CPU enquanto o dono fala.
- Os binários não têm assinatura digital, então o SmartScreen pode avisar. Instalação numa VM limpa ainda não foi feita.
- O seletor de atalho não está implementado (o atalho é fixo).
