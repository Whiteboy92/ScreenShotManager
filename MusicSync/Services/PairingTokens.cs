using System;
using System.Security.Cryptography;
using System.Text;

namespace ScreenShotManager.MusicSync.Services;

/// <summary>Generates and checks the bearer token the phone receives at pairing.</summary>
public static class PairingTokens
{
    private const string BearerPrefix = "Bearer ";

    public static string Generate() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(Models.MusicSyncConstants.TokenBytes)).ToLowerInvariant();

    /// <summary>
    /// True when <paramref name="authorizationHeader"/> is <c>Bearer &lt;expected&gt;</c>.
    /// Constant-time so the token can't be recovered byte-by-byte from response timing.
    /// </summary>
    public static bool IsAuthorized(string? authorizationHeader, string expectedToken)
    {
        if (string.IsNullOrEmpty(expectedToken)
            || authorizationHeader == null
            || !authorizationHeader.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var presented = Encoding.UTF8.GetBytes(authorizationHeader.Substring(BearerPrefix.Length).Trim());
        var expected = Encoding.UTF8.GetBytes(expectedToken);
        return CryptographicOperations.FixedTimeEquals(presented, expected);
    }
}
