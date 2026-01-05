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
                    // Criar o NotifyIcon
                    _notifyIcon = new NotifyIcon
                    {
                        Icon = SystemIcons.Application, // Ícone padrão do Windows
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
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro ao inicializar System Tray");
                    _initCompleted.Set();
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
                System.Windows.Forms.Application.ExitThread();
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
        }
    }
}
