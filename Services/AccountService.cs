using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;

namespace TalkGPT.Services;

public static class AccountService
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 210_000;
    private const int MaximumPasswordLength = 128;

    public static Dictionary<string, string[]> Validate(AccountRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Email) ||
            request.Email.Length > 254 ||
            !MailAddress.TryCreate(request.Email.Trim(), out var email) ||
            !string.Equals(email.Address, request.Email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            errors["email"] = ["Enter a valid email address."];
        }

        if (string.IsNullOrEmpty(request.Password) ||
            request.Password.Length < 10 ||
            request.Password.Length > MaximumPasswordLength)
        {
            errors["password"] = ["Password must be between 10 and 128 characters."];
        }

        return errors;
    }

    public static (string Salt, string Hash) HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashSize);

        return (Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public static bool VerifyPassword(string password, string salt, string expectedHash)
    {
        try
        {
            var saltBytes = Convert.FromBase64String(salt);
            var expectedBytes = Convert.FromBase64String(expectedHash);
            if (saltBytes.Length != SaltSize || expectedBytes.Length != HashSize)
            {
                return false;
            }

            var actualBytes = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                saltBytes,
                Iterations,
                HashAlgorithmName.SHA256,
                HashSize);

            return CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
