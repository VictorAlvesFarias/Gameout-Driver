using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace App.Services
{
    public class SystemTrayHostedService : IHostedService, IDisposable
    {
        private readonly ILogger<SystemTrayHostedService> _logger;
        private readonly IHostApplicationLifetime _applicationLifetime;
        private NotifyIcon? _notifyIcon;
        private Thread? _uiThread;
        private readonly ManualResetEvent _initCompleted = new ManualResetEvent(false);
        private readonly ManualResetEvent _shutdownCompleted = new ManualResetEvent(false);

        public SystemTrayHostedService(
            ILogger<SystemTrayHostedService> logger,
            IHostApplicationLifetime applicationLifetime)
        {
            _logger = logger;
            _applicationLifetime = applicationLifetime;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Iniciando System Tray...");

            // Criar thread STA para o NotifyIcon
            _uiThread = new Thread(() =>
            {
                try
                {
                    // Carregar o ícone personalizado do arquivo
                    Icon? customIcon = null;
                    try
                    {
                        var iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
                        if (File.Exists(iconPath))
                        {
                            customIcon = new Icon(iconPath);
                            _logger.LogInformation($"Ícone personalizado carregado de: {iconPath}");
                        }
                        else
                        {
                            _logger.LogWarning($"Arquivo de ícone não encontrado em: {iconPath}. Usando ícone padrão.");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Não foi possível carregar o ícone personalizado. Usando ícone padrão.");
                    }

                    // Criar o NotifyIcon
                    _notifyIcon = new NotifyIcon
                    {
                        Icon = customIcon ?? SystemIcons.Application, // Usa ícone personalizado ou padrão do Windows
                        Text = "Gameout Driver",
                        Visible = true
                    };

                    // Criar menu de contexto
                    var contextMenu = new ContextMenuStrip();
                    
                    var statusItem = new ToolStripMenuItem("Status: Conectado")
                    {
                        Enabled = false
                    };

                    var openItem = new ToolStripMenuItem("Abrir Configurações", null, (s, e) =>
                    {
                        _logger.LogInformation("Menu 'Abrir Configurações' clicado");
                        MessageBox.Show("Gameout Driver está rodando!\n\nVersão: 1.0.0", 
                            "Gameout Driver", 
                            MessageBoxButtons.OK, 
                            MessageBoxIcon.Information);
                    });

                    // Menu de Auto-Start com checkbox
                    var autoStartItem = new ToolStripMenuItem("Iniciar com Windows");
                    autoStartItem.Checked = AutoStartupService.IsAutoStartEnabled();
                    autoStartItem.Click += (s, e) =>
                    {
                        try
                        {
                            if (AutoStartupService.IsAutoStartEnabled())
                            {
                                AutoStartupService.DisableAutoStart(_logger);
                                autoStartItem.Checked = false;
                                MessageBox.Show("Inicialização automática desativada com sucesso!", 
                                    "Gameout Driver", 
                                    MessageBoxButtons.OK, 
                                    MessageBoxIcon.Information);
                            }
                            else
                            {
                                AutoStartupService.EnableAutoStart(_logger);
                                autoStartItem.Checked = true;
                                MessageBox.Show("Inicialização automática ativada com sucesso!", 
                                    "Gameout Driver", 
                                    MessageBoxButtons.OK, 
                                    MessageBoxIcon.Information);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Erro ao alternar auto-start");
                            MessageBox.Show($"Erro ao alterar configuração: {ex.Message}", 
                                "Erro", 
                                MessageBoxButtons.OK, 
                                MessageBoxIcon.Error);
                        }
                    };

                    var exitItem = new ToolStripMenuItem("Sair", null, (s, e) =>
                    {
                        _logger.LogInformation("Encerrando aplicação via System Tray...");
                        _applicationLifetime.StopApplication();
                    });

                    contextMenu.Items.Add(statusItem);
                    contextMenu.Items.Add(new ToolStripSeparator());
                    contextMenu.Items.Add(openItem);
                    contextMenu.Items.Add(autoStartItem);
                    contextMenu.Items.Add(new ToolStripSeparator());
                    contextMenu.Items.Add(exitItem);

                    _notifyIcon.ContextMenuStrip = contextMenu;

                    // Adicionar handler de duplo clique
                    _notifyIcon.DoubleClick += (s, e) =>
                    {
                        _logger.LogInformation("System Tray ícone clicado");
                        MessageBox.Show("Gameout Driver está rodando!\n\nDuplo clique para mais informações.", 
                            "Gameout Driver", 
                            MessageBoxButtons.OK, 
                            MessageBoxIcon.Information);
                    };

                    _initCompleted.Set();
                    _logger.LogInformation("System Tray inicializado com sucesso");

                    // Manter a thread viva para processar eventos do NotifyIcon
                    System.Windows.Forms.Application.Run();
                    
                    // Sinalizar que o shutdown foi concluído
                    _shutdownCompleted.Set();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro ao inicializar System Tray");
                    _initCompleted.Set();
                    _shutdownCompleted.Set();
                }
            })
            {
                IsBackground = false,
                Name = "SystemTrayThread"
            };

            _uiThread.SetApartmentState(ApartmentState.STA);
            _uiThread.Start();

            // Aguardar inicialização
            _initCompleted.WaitOne();

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Parando System Tray...");

            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                
                // Encerrar o message loop da aplicação Windows Forms
                if (_uiThread != null && _uiThread.IsAlive)
                {
                    System.Windows.Forms.Application.ExitThread();
                    
                    // Aguardar a thread encerrar com timeout de 5 segundos
                    if (!_shutdownCompleted.WaitOne(TimeSpan.FromSeconds(5)))
                    {
                        _logger.LogWarning("Thread UI não encerrou no tempo esperado. Abortando thread...");
                        
                        // Como último recurso, abortar a thread (não recomendado, mas necessário)
                        try
                        {
                            #pragma warning disable SYSLIB0006 // Thread.Abort é obsoleto mas necessário aqui
                            if (_uiThread.IsAlive)
                            {
                                _uiThread.Interrupt();
                            }
                            #pragma warning restore SYSLIB0006
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Erro ao tentar interromper a thread UI");
                        }
                    }
                    else
                    {
                        _logger.LogInformation("Thread UI encerrada com sucesso");
                    }
                }
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }

            _initCompleted?.Dispose();
            _shutdownCompleted?.Dispose();
        }
    }
}
