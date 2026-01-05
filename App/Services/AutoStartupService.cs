using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Reflection;

namespace App.Services
{
    public class AutoStartupService : IHostedService
    {
        private readonly ILogger<AutoStartupService> _logger;
        private readonly IHostEnvironment _environment;
        private const string AppName = "GameoutDriver";
        private const string RegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public AutoStartupService(
            ILogger<AutoStartupService> logger,
            IHostEnvironment environment)
        {
            _logger = logger;
            _environment = environment;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Não configurar auto-start em modo Debug/Development
                if (_environment.IsDevelopment())
                {
                    _logger.LogInformation("Modo Development detectado - auto-start não será configurado");
                    return Task.CompletedTask;
                }

                ConfigureAutoStart();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao configurar inicialização automática");
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private void ConfigureAutoStart()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, writable: true);
                
                if (key == null)
                {
                    _logger.LogError("Não foi possível acessar a chave de registro para auto-start");
                    return;
                }

                // Obter o caminho do executável atual
                var exePath = Environment.ProcessPath ?? Assembly.GetExecutingAssembly().Location;
                
                // Verificar se já existe uma entrada
                var existingValue = key.GetValue(AppName) as string;

                if (existingValue != null)
                {
                    if (existingValue == exePath)
                    {
                        _logger.LogInformation("Auto-start já configurado corretamente: {Path}", exePath);
                        return;
                    }
                    else
                    {
                        _logger.LogInformation("Atualizando caminho do auto-start de '{OldPath}' para '{NewPath}'", 
                            existingValue, exePath);
                        key.SetValue(AppName, exePath, RegistryValueKind.String);
                        _logger.LogInformation("Auto-start atualizado com sucesso");
                    }
                }
                else
                {
                    // Primeira execução - perguntar ao usuário
                    _logger.LogInformation("Primeira execução detectada - perguntando ao usuário sobre auto-start");
                    
                    var result = MessageBox.Show(
                        "Deseja que o Gameout Driver inicie automaticamente com o Windows?\n\n" +
                        "Você pode alterar essa configuração depois clicando com o botão direito no ícone da bandeja do sistema.",
                        "Gameout Driver - Inicialização Automática",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question,
                        MessageBoxDefaultButton.Button1);

                    if (result == DialogResult.Yes)
                    {
                        key.SetValue(AppName, exePath, RegistryValueKind.String);
                        _logger.LogInformation("Auto-start configurado com sucesso pelo usuário: {Path}", exePath);
                        MessageBox.Show(
                            "Inicialização automática ativada com sucesso!",
                            "Gameout Driver",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                    else
                    {
                        _logger.LogInformation("Usuário optou por não configurar auto-start");
                    }
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Sem permissão para modificar o registro. Execute como administrador se necessário.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao configurar auto-start no registro");
            }
        }

        /// <summary>
        /// Verifica se o auto-start está habilitado
        /// </summary>
        public static bool IsAutoStartEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, writable: false);
                if (key == null) return false;

                var existingValue = key.GetValue(AppName);
                return existingValue != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Habilita o auto-start manualmente
        /// </summary>
        public static void EnableAutoStart(ILogger? logger = null)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, writable: true);
                
                if (key == null)
                {
                    logger?.LogError("Não foi possível acessar a chave de registro");
                    return;
                }

                var exePath = Environment.ProcessPath ?? Assembly.GetExecutingAssembly().Location;
                key.SetValue(AppName, exePath, RegistryValueKind.String);
                logger?.LogInformation("Auto-start habilitado manualmente");
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Erro ao habilitar auto-start");
                throw;
            }
        }

        /// <summary>
        /// Remove a aplicação do auto-start
        /// </summary>
        public static void DisableAutoStart(ILogger? logger = null)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, writable: true);
                
                if (key == null)
                {
                    logger?.LogError("Não foi possível acessar a chave de registro");
                    return;
                }

                var existingValue = key.GetValue(AppName);
                if (existingValue != null)
                {
                    key.DeleteValue(AppName, throwOnMissingValue: false);
                    logger?.LogInformation("Auto-start desabilitado com sucesso");
                }
                else
                {
                    logger?.LogInformation("Auto-start não estava configurado");
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Erro ao desabilitar auto-start");
                throw;
            }
        }
    }
}
