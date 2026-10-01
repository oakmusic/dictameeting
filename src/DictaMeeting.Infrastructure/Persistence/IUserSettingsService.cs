namespace DictaMeeting.Infrastructure.Persistence;

/// <summary>
/// Contrato para el servicio de persistencia de configuración y preferencias del usuario.
/// </summary>
public interface IUserSettingsService
{
    UserSettings LoadSettings();
    void SaveSettings(UserSettings settings);
}
