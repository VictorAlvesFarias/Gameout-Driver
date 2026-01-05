# Gameout Driver - Desktop Application

Aplicação desktop que roda em segundo plano com ícone na system tray.

## 🚀 Funcionalidades

- **System Tray**: Ícone na área de notificação do Windows
- **Background Workers**: 
  - `DriveWebSocketClientWorker`: Conexão WebSocket com o backend
  - `AppFileSyncWorker`: Sincronização de arquivos
- **Menu de Contexto**: Clique direito no ícone para opções
- **Execução Silenciosa**: Sem janela de console

## 📦 Como Executar

### Desenvolvimento
```bash
cd Gameout-Driver/DesktopApp
dotnet restore
dotnet run
```

### Build Release
```bash
cd Gameout-Driver/DesktopApp
dotnet build -c Release
```

O executável estará em: `bin/Release/net6.0-windows/GameoutDriver.exe`

### Publicar (Single File)
```bash
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

## ⚙️ Configuração

Edite `appsettings.json`:
```json
{
  "BackendApi": {
    "BaseUrl": "https://seu-backend.com"
  },
  "ApiKey": "sua-api-key"
}
```

## 🎯 Menu System Tray

- **Status**: Exibe o status da conexão
- **Abrir Configurações**: Mostra informações da aplicação
- **Sair**: Encerra a aplicação

Duplo clique no ícone também mostra informações.

## 🔧 Instalar como Serviço Windows

```powershell
# Criar serviço
sc.exe create GameoutDriver binPath="C:\caminho\para\GameoutDriver.exe" start=auto

# Iniciar serviço
sc.exe start GameoutDriver

# Parar serviço
sc.exe stop GameoutDriver

# Remover serviço
sc.exe delete GameoutDriver
```

## 📋 Requisitos

- .NET 6.0 Runtime (Windows)
- Windows 7 ou superior

## 🏗️ Estrutura

```
DesktopApp/
├── Program.cs                          # Entry point
├── DesktopApp.csproj                   # Projeto desktop
├── appsettings.json                    # Configurações
├── Services/
│   └── SystemTrayHostedService.cs      # Gerencia system tray
└── README.md
```

## 🔗 Diferenças do Projeto Api

| Aspecto | Api (Web) | DesktopApp |
|---------|-----------|------------|
| SDK | Microsoft.NET.Sdk.Web | Microsoft.NET.Sdk |
| OutputType | - | WinExe |
| Target | net6.0 | net6.0-windows |
| Interface | HTTP/WebSocket Server | System Tray |
| Console | Sim | Não |

## 📝 Notas

- A aplicação roda completamente em background
- Não abre janela de console
- Logs são gravados conforme configuração em `appsettings.json`
- Para debug, pode trocar `OutputType` para `Exe` temporariamente
