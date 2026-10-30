using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;

namespace Bubble.Core;

/// <summary>What the device needs to update its backup without entering the code again.</summary>
public sealed record BackupState(string BackupId, string AuthToken, string DataKey, string CodeWrap);

/// <summary>
/// Content on the relay. The vault is encrypted with a random data key;
/// this data key is "wrapped" per unlock method (currently: backup code).
/// A passkey (WebAuthn PRF) will be added later as another entry in <see cref="Wraps"/>.
/// </summary>
public sealed record BackupBlob(int V, Dictionary<string, string> Wraps, string Data);

public static class Backup
{
    // Crockford Base32: without I, L, O, U – easy to type and suitable for password managers.
    const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    const int CodeChars = 25; // 125 Bit Zufall
    static readonly byte[] Salt = Encoding.UTF8.GetBytes("bubble/v1/backup-code");

    /// <summary>Argon2id-Kosten. Bei 125 Bit Zufall im Code eher Schutz gegen Massenangriffe als gegen Raten.</summary>
    public static int Argon2MemoryKiB { get; set; } = 64 * 1024;
    public static int Argon2Iterations { get; set; } = 3;

    public static string GenerateCode()
    {
        var bytes = Crypto.RandomBytes(CodeChars);
        var chars = bytes.Select(b => Alphabet[b & 31]).ToArray();
        return string.Join('-', Enumerable.Range(0, 5).Select(i => new string(chars, i * 5, 5)));
    }

    public static string NormalizeCode(string code)
    {
        var sb = new StringBuilder();
        foreach (var c in code.ToUpperInvariant())
        {
            var mapped = c switch { 'O' => '0', 'I' or 'L' => '1', _ => c };
            if (Alphabet.Contains(mapped)) sb.Append(mapped);
        }
        if (sb.Length != CodeChars)
            throw new FormatException("The backup code does not have the right format.");
        return sb.ToString();
    }

    /// <summary>Sets up a new backup. Returns the state the device stores in the vault.</summary>
    public static BackupState Create(string code)
    {
        var secrets = DeriveFromCode(code);
        var dataKey = Crypto.NewKey();
        var wrap = Crypto.Seal(secrets.WrapKey, dataKey, "bubble/v1/backup-wrap|code");
        return new BackupState(secrets.BackupId, secrets.AuthToken, Base64Url.Encode(dataKey), Base64Url.Encode(wrap));
    }

    public static byte[] Seal(BackupState state, Vault vault)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(vault, CoreJson.Default.Vault);
        var data = Crypto.Seal(Base64Url.Decode(state.DataKey), json, "bubble/v1/backup-data");
        var blob = new BackupBlob(1, new() { ["code"] = state.CodeWrap }, Base64Url.Encode(data));
        return JsonSerializer.SerializeToUtf8Bytes(blob, CoreJson.Default.BackupBlob);
    }

    /// <summary>Opens a backup with the code. Throws a CryptographicException for a wrong code.</summary>
    public static Vault Open(string code, byte[] blobBytes) => Open(DeriveFromCode(code), blobBytes);

    /// <summary>Like <see cref="Open(string, byte[])"/>, but with already derived values (saves an Argon2 run).</summary>
    public static Vault Open(CodeSecrets secrets, byte[] blobBytes)
    {
        var blob = JsonSerializer.Deserialize(blobBytes, CoreJson.Default.BackupBlob)
                   ?? throw new FormatException("Backup is empty.");
        var dataKey = Crypto.Open(secrets.WrapKey, Base64Url.Decode(blob.Wraps["code"]), "bubble/v1/backup-wrap|code");
        var json = Crypto.Open(dataKey, Base64Url.Decode(blob.Data), "bubble/v1/backup-data");
        var vault = JsonSerializer.Deserialize(json, CoreJson.Default.Vault) ?? throw new FormatException("Vault is empty.");
        vault.Backup = new BackupState(secrets.BackupId, secrets.AuthToken, Base64Url.Encode(dataKey), blob.Wraps["code"]);
        return vault;
    }

    public sealed record CodeSecrets(byte[] WrapKey, string BackupId, string AuthToken);

    /// <summary>Three independent values are derived from the code: key, storage ID and write token.</summary>
    public static CodeSecrets DeriveFromCode(string code)
    {
        var output = Argon2(Encoding.UTF8.GetBytes(NormalizeCode(code)), Salt, 96);
        return new CodeSecrets(
            output[..32],
            Base64Url.Encode(output[32..64]),
            Base64Url.Encode(output[64..96]));
    }

    /// <summary>Argon2id with the cost settings above. Also used for short invitation codes.</summary>
    internal static byte[] Argon2(byte[] password, byte[] salt, int length)
    {
        var parameters = new Argon2Parameters.Builder(Argon2Parameters.Argon2id)
            .WithVersion(Argon2Parameters.Version13)
            .WithIterations(Argon2Iterations)
            .WithMemoryAsKB(Argon2MemoryKiB)
            .WithParallelism(1)
            .WithSalt(salt)
            .Build();
        var generator = new Argon2BytesGenerator();
        generator.Init(parameters);
        var output = new byte[length];
        generator.GenerateBytes(password, output);
        return output;
    }
}
