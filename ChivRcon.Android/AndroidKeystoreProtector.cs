using System.Text;
using Android.Security.Keystore;
using ChivRcon.App;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;

namespace ChivRcon.Android;

/// <summary>
/// Encrypts saved passwords with an AES-GCM key held in the Android keystore. The key material
/// never leaves the keystore and is not extractable, so lifting the settings file off the
/// device yields ciphertext rather than an admin password.
/// </summary>
/// <remarks>
/// Stored form is "v1:" + base64(iv | ciphertext). Anything without that prefix is treated as
/// a legacy base64 value and read as plaintext, so a settings file copied from the desktop
/// build still opens; it is re-encrypted the next time it is saved.
/// </remarks>
public sealed class AndroidKeystoreProtector : ISecretProtector
{
    private const string KeystoreName = "AndroidKeyStore";
    private const string KeyAlias = "chivrcon.password.v1";
    private const string Transform = "AES/GCM/NoPadding";
    private const string Prefix = "v1:";
    private const int IvBytes = 12;
    private const int TagBits = 128;

    private readonly ISecretProtector _legacy = new Base64Protector();

    public string Protect(string plain)
    {
        plain ??= "";
        try
        {
            var cipher = Cipher.GetInstance(Transform)!;
            cipher.Init(CipherMode.EncryptMode, GetOrCreateKey());

            byte[] iv = cipher.GetIV()!;
            byte[] ct = cipher.DoFinal(Encoding.UTF8.GetBytes(plain))!;

            var joined = new byte[iv.Length + ct.Length];
            Buffer.BlockCopy(iv, 0, joined, 0, iv.Length);
            Buffer.BlockCopy(ct, 0, joined, iv.Length, ct.Length);
            return Prefix + Convert.ToBase64String(joined);
        }
        catch
        {
            // Never lose the user's ability to save a server because the keystore misbehaved.
            return _legacy.Protect(plain);
        }
    }

    public string Unprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return "";
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal)) return _legacy.Unprotect(stored);

        try
        {
            byte[] joined = Convert.FromBase64String(stored[Prefix.Length..]);
            if (joined.Length <= IvBytes) return "";

            byte[] iv = new byte[IvBytes];
            byte[] ct = new byte[joined.Length - IvBytes];
            Buffer.BlockCopy(joined, 0, iv, 0, IvBytes);
            Buffer.BlockCopy(joined, IvBytes, ct, 0, ct.Length);

            var cipher = Cipher.GetInstance(Transform)!;
            cipher.Init(CipherMode.DecryptMode, GetOrCreateKey(), new GCMParameterSpec(TagBits, iv));
            return Encoding.UTF8.GetString(cipher.DoFinal(ct)!);
        }
        catch
        {
            // Key rotated or wiped (app data cleared, restore to a new device): the password
            // is simply gone and has to be re-entered.
            return "";
        }
    }

    private static IKey GetOrCreateKey()
    {
        var store = KeyStore.GetInstance(KeystoreName)!;
        store.Load(null);

        if (store.GetKey(KeyAlias, null) is { } existing) return existing;

        var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, KeystoreName)!;
        var spec = new KeyGenParameterSpec.Builder(KeyAlias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)!
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)!
            // Not tied to device unlock: the reconnect timer must be able to re-auth while
            // the screen is off, and that needs the key available in the background.
            .SetUserAuthenticationRequired(false)!
            .Build();
        generator.Init(spec);
        return generator.GenerateKey()!;
    }
}
