using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace AcoesRapidas.Calendar;

/// <summary>Guarda a senha de aplicativo criptografada, legível só pelo usuário atual do Windows.</summary>
public sealed class NextcloudAccountStore(string path)
{
    public NextcloudAccount? Load(string server)
    {
        try
        {
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
            var account = JsonSerializer.Deserialize<NextcloudAccount>(bytes);
            return string.Equals(account?.Server, server.TrimEnd('/'), StringComparison.OrdinalIgnoreCase) ? account : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            // Sem um arquivo legível, o app só pede a autorização de novo.
            return null;
        }
    }

    public void Save(NextcloudAccount account)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(account), null, DataProtectionScope.CurrentUser);

        var temporaryPath = path + ".tmp";
        File.WriteAllBytes(temporaryPath, bytes);
        File.Move(temporaryPath, path, overwrite: true);
    }

    public void Clear() => File.Delete(path);
}
