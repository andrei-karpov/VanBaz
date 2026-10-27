using System.Security.Cryptography;
using System.Text;

namespace VanBaz;

public static class SecretStore
{
    public static string Protect(string value) => value.Length == 0 ? "" : Convert.ToBase64String(
        ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    public static string Reveal(string value) => value.Length == 0 ? "" : Encoding.UTF8.GetString(
        ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser));
}
