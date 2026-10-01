using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace DictaMeeting.Infrastructure.Security;

/// <summary>
/// Almacenamiento seguro de secretos locales (ej. API Key de OpenRouter) utilizando Windows Data Protection API (DPAPI).
/// No almacena secretos en texto plano, ni en repositorios, ni en Git, ni en variables no protegidas.
/// </summary>
public class WindowsDpapiSecureStorageService : ISecureStorageService
{
    private readonly string _storageFolder;
    private readonly ILogger<WindowsDpapiSecureStorageService>? _logger;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DictaMeeting.DPAPI.Entropy.v1");

    public WindowsDpapiSecureStorageService(ILogger<WindowsDpapiSecureStorageService>? logger = null)
    {
        _logger = logger;
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _storageFolder = Path.Combine(localAppData, "DictaMeeting", "secure");

        if (!Directory.Exists(_storageFolder))
        {
            Directory.CreateDirectory(_storageFolder);
        }
    }

    public void SaveSecret(string key, string secret)
    {
        try
        {
            var filePath = GetSecretFilePath(key);
            if (string.IsNullOrEmpty(secret))
            {
                if (File.Exists(filePath)) File.Delete(filePath);
                return;
            }

            var plainBytes = Encoding.UTF8.GetBytes(secret);
            var cipherBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);

            File.WriteAllBytes(filePath, cipherBytes);
            _logger?.LogInformation("Secreto con clave '{Key}' guardado de forma segura con DPAPI.", key);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error al guardar el secreto '{Key}' con DPAPI.", key);
            throw;
        }
    }

    public string? GetSecret(string key)
    {
        try
        {
            var filePath = GetSecretFilePath(key);
            if (!File.Exists(filePath)) return null;

            var cipherBytes = File.ReadAllBytes(filePath);
            var plainBytes = ProtectedData.Unprotect(cipherBytes, Entropy, DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error al recuperar el secreto '{Key}' con DPAPI.", key);
            return null;
        }
    }

    public void DeleteSecret(string key)
    {
        try
        {
            var filePath = GetSecretFilePath(key);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logger?.LogInformation("Secreto '{Key}' eliminado.", key);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error al eliminar el secreto '{Key}'.", key);
        }
    }

    private string GetSecretFilePath(string key)
    {
        var safeFileName = Convert.ToBase64String(Encoding.UTF8.GetBytes(key))
            .Replace('/', '_')
            .Replace('+', '-');
        return Path.Combine(_storageFolder, $"{safeFileName}.dat");
    }
}
