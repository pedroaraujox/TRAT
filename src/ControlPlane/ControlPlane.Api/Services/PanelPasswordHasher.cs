using System.Security.Cryptography;

namespace ControlPlane.Api.Services;

public sealed class PanelPasswordHasher
{
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;
    private const int DefaultIterations = 100_000;

    public PasswordHashResult HashPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Senha nao pode ficar vazia.");
        }

        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password.Trim(),
            salt,
            DefaultIterations,
            HashAlgorithmName.SHA256,
            HashSizeBytes);

        return new PasswordHashResult(
            HashBase64: Convert.ToBase64String(hash),
            SaltBase64: Convert.ToBase64String(salt),
            Iterations: DefaultIterations);
    }

    public bool VerifyPassword(string password, string hashBase64, string saltBase64, int iterations)
    {
        if (string.IsNullOrWhiteSpace(password) ||
            string.IsNullOrWhiteSpace(hashBase64) ||
            string.IsNullOrWhiteSpace(saltBase64) ||
            iterations <= 0)
        {
            return false;
        }

        byte[] expectedHash;
        byte[] salt;
        try
        {
            expectedHash = Convert.FromBase64String(hashBase64.Trim());
            salt = Convert.FromBase64String(saltBase64.Trim());
        }
        catch
        {
            return false;
        }

        var actualHash = Rfc2898DeriveBytes.Pbkdf2(
            password.Trim(),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            expectedHash.Length);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}

public sealed record PasswordHashResult(string HashBase64, string SaltBase64, int Iterations);
