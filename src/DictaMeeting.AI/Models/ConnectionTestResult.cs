namespace DictaMeeting.AI.Models;

/// <summary>
/// Resultado de la verificación de conexión con un proveedor de IA.
/// </summary>
public class ConnectionTestResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ModelTested { get; set; }
    public string? Provider { get; set; }

    public static ConnectionTestResult Ok(string message, string? model = null, string? provider = null) =>
        new() { Success = true, Message = message, ModelTested = model, Provider = provider };

    public static ConnectionTestResult Failed(string message, string? model = null, string? provider = null) =>
        new() { Success = false, Message = message, ModelTested = model, Provider = provider };
}
