using App.Workers;
using Application.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Reflection;

namespace App.Services
{
    public class SystemTrayHostedService : IHostedService, IDisposable
    {
        private readonly ILogger<SystemTrayHostedService> _logger;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly IConfiguration _configuration;
        private readonly IHostEnvironment _environment;
        private readonly DriveWebSocketClientWorker _webSocketWorker;

        private Thread? _uiThread;
        private NotifyIcon? _notifyIcon;

        private const string AppName = "GameoutDriver";
        private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public SystemTrayHostedService(
            ILogger<SystemTrayHostedService> logger,
            IHostApplicationLifetime lifetime,
            DriveWebSocketClientWorker webSocketWorker,
            IConfiguration configuration,
            IHostEnvironment environment
        )
        {
            _logger = logger;
            _lifetime = lifetime;
            _webSocketWorker = webSocketWorker;
            _configuration = configuration;
            _environment = environment;
        }

        #region Controle de Vida

        public Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                _uiThread = new Thread(() =>
                {
                    Configure();
                    System.Windows.Forms.Application.Run();
                });
                _uiThread.IsBackground = false;

                _uiThread.SetApartmentState(ApartmentState.STA);
                _uiThread.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error", $"An error occurred during process. Original error.: { ex.Message}");
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            if (_uiThread != null && _uiThread.IsAlive)
            {
                System.Windows.Forms.Application.Exit();
                _uiThread.Join(TimeSpan.FromSeconds(5));
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (_notifyIcon == null)
            {
                return;
            }

            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        #endregion

        #region ToolStripMenuItem

        private ContextMenuStrip CreateMenu()
        {
            var menu = new ContextMenuStrip();

            menu.Items.Add(CreateStatusItem());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(CreateOpenSettingsItem());
            menu.Items.Add(CreateConfigureUrlItem());
            menu.Items.Add(CreateReconnectItem());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(CreateAutoStartItem());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(CreateExitItem());

            return menu;
        }

        private ToolStripMenuItem CreateStatusItem()
        {
            return new ToolStripMenuItem("Status: Connected") { Enabled = false };
        }

        private ToolStripMenuItem CreateOpenSettingsItem()
        {
            return new ToolStripMenuItem("Open Settings", null, (_, _) =>
            {
                MessageBox.Show(
                    "Gameout Driver\nVersão 1.0.0",
                    "Gameout Driver",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            });
        }

        private ToolStripMenuItem CreateConfigureUrlItem()
        {
            return new ToolStripMenuItem("Configure Service URL", null, (_, _) =>
            {
                ShowConfigureUrlDialog();
            });
        }

        private ToolStripMenuItem CreateReconnectItem()
        {
            return new ToolStripMenuItem("Reconnect", null, (_, _) =>
            {
                _logger.LogInformation("User requested reconnection");
                _webSocketWorker.Reconect();
                
                MessageBox.Show(
                    "Reconnection requested. The driver will reconnect shortly.",
                    "Reconnect",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            });
        }

        private ToolStripMenuItem CreateAutoStartItem()
        {
            var item = new ToolStripMenuItem("Start with Windows")
            {
                Checked = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true)?.GetValue(AppName) != null
            };

            item.Click += (_, _) =>
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true);
                if (key == null)
                {
                    return;
                }

                if (item.Checked)
                {
                    key.DeleteValue(AppName, false);
                    item.Checked = false;
                }
                else
                {
                    var exePath = Environment.ProcessPath ?? Assembly.GetExecutingAssembly().Location;
                    key.SetValue(AppName, exePath);
                    item.Checked = true;
                }
            };

            return item;
        }

        private ToolStripMenuItem CreateExitItem()
        {
            return new ToolStripMenuItem("Exit", null, (_, _) =>
            {
                _lifetime.StopApplication();
            });
        }

        #endregion

        #region Dialogs

        private void ShowConfigureUrlDialog()
        {
            var backendConfiguration = _configuration.GetSection("BackendApi").Get<BackendApiConfiguration>();
            var webSocketConfiguration = _configuration.GetSection("WebSocket").Get<WebSocketConfiguration>();
            var form = new Form
            {
                Text = "Configure Service URL",
                Width = 500,
                Height = 220,
                StartPosition = FormStartPosition.CenterScreen,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };
            var label = new Label { Text = "Backend URL:", Left = 20, Top = 20, Width = 120 };
            var textBox = new TextBox { Text = backendConfiguration.BaseUrl, Left = 140, Top = 20, Width = 320 };
            
            var label2 = new Label { Text = "WebSocket URL:", Left = 20, Top = 50, Width = 120 };
            var textBox2 = new TextBox { Text = webSocketConfiguration.Url, Left = 140, Top = 50, Width = 320 };

            var label3 = new Label { Text = "API Key:", Left = 20, Top = 80, Width = 120 };
            var textBox3 = new TextBox { Text = backendConfiguration.ApiKey, Left = 140, Top = 80, Width = 320 };

            var save = new Button { Text = "Save", Left = 280, Top = 120, Width = 80, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Left = 370, Top = 120, Width = 80, DialogResult = DialogResult.Cancel };

            form.Controls.AddRange(new Control[]
            {
                label,
                textBox,
                label2,
                textBox2,
                label3,
                textBox3,
                save,
                cancel
            });
            form.AcceptButton = save;
            form.CancelButton = cancel;

            if (form.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            var newUrl = textBox.Text.Trim();
            var newUrl2 = textBox2.Text.Trim();
            var newApiKey = textBox3.Text.Trim();

            if (string.IsNullOrWhiteSpace(newUrl2) || string.IsNullOrWhiteSpace(newUrl) || string.IsNullOrWhiteSpace(newApiKey))
            {
                return;
            }

            var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            var json = File.ReadAllText(path);
            var settings = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json);

            if (settings == null)
            {
                return;
            }

            settings["BackendApi"] = new Dictionary<string, object>
            {
                { "BaseUrl", newUrl },
                { "ApiKey", newApiKey }
            };

            settings["WebSocket"] = new Dictionary<string, object>
            {
                { "Url", newUrl2 }
            };

            File.WriteAllText(
                path,
                System.Text.Json.JsonSerializer.Serialize(
                    settings,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
                )
            );

            if (_configuration is IConfigurationRoot configRoot)
            {
                configRoot.Reload();
            }

            MessageBox.Show(
                "Configuração atualizada com sucesso.",
                "Gameout Driver",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

        #endregion

        private void Configure()
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");

            _notifyIcon = new NotifyIcon
            {
                Icon = File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application,
                Text = "Gameout Driver",
                Visible = true,
                ContextMenuStrip = CreateMenu()
            };

            _notifyIcon.DoubleClick += (_, _) =>
            {
                MessageBox.Show(
                    "Gameout Driver está em execução.",
                    "Gameout Driver",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            };

            if (_environment.IsDevelopment())
            {
                return;
            }

            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true);
            if (key == null)
            {
                return;
            }

            var exePath = Environment.ProcessPath ?? Assembly.GetExecutingAssembly().Location;
            if (key.GetValue(AppName) == null)
            {
                key.SetValue(AppName, exePath);
            }
        }
    }
}
