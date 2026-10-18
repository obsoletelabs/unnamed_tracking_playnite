using System;
using System.Security.Cryptography;
using System.Text;

namespace UnnamedTrackingPlaynite;

// Playnite stores extension settings as plain JSON in the user's profile. The
// sign-in credential is encrypted with Windows DPAPI for the current Windows user,
// so a copied settings file is useless on another account or machine.
internal static class CredentialProtection
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("UnnamedTrackingPlaynite.credential.v1");

    internal static string Protect(string credential) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(credential), Entropy, DataProtectionScope.CurrentUser));

    /// <summary>The stored credential, or null when it cannot be read (another user, a reset profile).</summary>
    internal static string? Unprotect(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is CryptographicException || ex is FormatException)
        {
            return null;
        }
    }
}
