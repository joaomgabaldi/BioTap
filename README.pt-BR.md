# BioTap

[English](README.md)

Transforma o leitor de digital do PC num atalho físico: **1, 2, N toques seguidos no sensor disparam a ação que você configurou para N.** Qualquer toque conta, mesmo de um dedo não cadastrado, e o Windows Hello continua funcionando normalmente para login e autenticação.

## O que dá para fazer

Cada ação usa um destes presets:

| Categoria | Presets |
|---|---|
| Abrir e rodar | Abrir programa, arquivo ou URL · Rodar comando ou script |
| Teclado | Atalho de teclado · Copiar · Colar · Inserir texto |
| Mídia e som | Silenciar · Tocar/pausar · Volume + / − · Definir volume · Trocar saída de áudio |
| Janelas | Sempre no topo · Minimizar as outras · Bloquear o Windows |
| Ferramentas | Captura de tela (monitor ou janela) · QR code do texto copiado |
| Combinar | Sequência (até 20 passos, com esperas) |

Além dos toques, cada ação também pode ser disparada pelo painel da bandeja. Uma pequena confirmação aparece acima da barra de tarefas sem roubar o foco, e não aparece em silenciar e volume, porque o próprio Windows já mostra o aviso.

## Requisitos

- Windows 10 2004 (19041) ou mais novo, 64 bits.
- Leitor de digital compatível com o Windows Hello.
- Conta de administrador: o app roda elevado e o serviço roda como SYSTEM.

## Instalação

Baixe o `BioTap-Setup-<versão>.exe` da [versão mais recente](https://github.com/joaomgabaldi/BioTap/releases/latest) e rode. O instalador registra o serviço, cria a tarefa de logon que abre o app sem pedir UAC e já abre o app. O instalador não é assinado: o Windows SmartScreen pode avisar ("Mais informações" → "Executar assim mesmo"). Para desinstalar, use Configurações → Aplicativos.

Para gerar o instalador a partir do código, você precisa do [.NET 10 SDK](https://dotnet.microsoft.com/download) e do [Inno Setup 7](https://jrsoftware.org/isinfo.php):

```powershell
.\install\build-installer.ps1
```

O resultado fica em `artifacts\installer\`.

As ações ficam em `C:\ProgramData\BioTap\config.json` e o log em `C:\ProgramData\BioTap\logs\app.log`.

## Como funciona

```
sensor ─► BioTap.Service (SYSTEM) ─ named pipe (só SYSTEM e Admins) ─► BioTap.exe (WinUI 3, admin)
          conta os toques e libera                                       bandeja, painel, preferências,
          o sensor para o Windows Hello                                  confirmação; executa as ações
```

- **O serviço só conta toques.** Ele nunca executa nada, e um teste garante isso.
- **Programas, arquivos e URLs abrem sem elevação**, pelo Explorer. Só "Rodar comando" com a opção de administrador roda elevado.
- **A configuração e o log só podem ser gravados por administradores.** O app confere as permissões antes de usar.
- **O log nunca grava** texto digitado, conteúdo da área de transferência nem argumentos de comandos.

## Desenvolvimento

```powershell
dotnet build
dotnet test
```

| Projeto | Papel |
|---|---|
| `src/BioTap.Core` | Lógica testável: contagem, presets, configuração, validação |
| `src/BioTap.Service` | Serviço do Windows que lê o sensor |
| `src/BioTap.App` | App WinUI 3: bandeja, painel, preferências, confirmação |
| `src/BioTap.Probe` | Console de diagnóstico do sensor (admin) |
| `tests/` | Testes xUnit do Core e do Serviço |

Stack: .NET 10, Windows App SDK, CsWin32, H.NotifyIcon, QRCoder.

## Licença

[GPL-3.0](LICENSE)
