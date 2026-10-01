namespace DictaMeeting.Infrastructure.Security;

public interface ISecureStorageService
{
    void SaveSecret(string key, string secret);
    string? GetSecret(string key);
    void DeleteSecret(string key);
}
