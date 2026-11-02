# Política de segurança

## Versões que recebem correções

Só a versão **mais recente** recebe correções de segurança. A versão 1.0.2 foi retirada.

| Versão | Suporte |
| :--- | :--- |
| 1.0.3 e posteriores | ✅ |
| 1.0.2 e anteriores | ❌ atualize para a versão mais recente |

## Como relatar uma vulnerabilidade

**Não abra uma issue pública** para falhas de segurança.

1. Abra a aba [**Security**](https://github.com/JotaEfi/somning/security) do repositório.
2. Clique em [**Report a vulnerability**](https://github.com/JotaEfi/somning/security/advisories/new).
3. Descreva o problema, os passos para reproduzir, a versão do Somning, a versão do Windows e a arquitetura (x64 ou ARM64).

O relato fica visível só para você e para o mantenedor. Você receberá uma resposta assim que possível; este é um projeto pessoal, então não há prazo garantido, mas relatos de segurança têm prioridade.

## O que é considerado uma vulnerabilidade

Exemplos do que nos interessa:

- vazamento da **chave de API** (Groq ou OpenAI) ou falha na proteção por DPAPI;
- envio de áudio ou texto a um destino que o usuário **não escolheu** (por exemplo, enviar à nuvem com o modo local ativo);
- execução de código por meio do instalador, dos scripts de `tools/` ou do pacote ZIP;
- download de modelos sem a verificação de integridade SHA-256;
- gravações ou transcrições acessíveis a outros usuários do computador.

## O que o Somning faz com seus dados

- **Modo local:** o áudio é transcrito no computador, sem enviar nada a um provedor.
- **Modo nuvem:** o áudio é enviado ao provedor que você escolheu, com a sua chave.
- **Chaves:** protegidas por DPAPI, no usuário do Windows.

Os detalhes de onde ficam configurações, logs, modelos e áudios estão em [Privacidade e armazenamento](docs/PRIVACIDADE.md).

## Limitações conhecidas

Os binários **ainda não têm assinatura digital**, então o Windows SmartScreen pode avisar. Baixe os instaladores só da [página de releases](https://github.com/JotaEfi/somning/releases) e confira o hash com o arquivo `SHA256SUMS` publicado junto.

```powershell
Get-FileHash .\Somning-1.0.3-Setup.exe -Algorithm SHA256
```
