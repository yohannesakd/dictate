using Android.Content;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using Android.Security.Keystore;
using Dictate.Core;

namespace Dictate.Android;

public sealed class AndroidSecrets(Context context) : ISecretStore
{
    private const string Alias = "DictateCredentials";
    private readonly ISharedPreferences preferences = context.GetSharedPreferences("dictate-secrets", FileCreationMode.Private)!;
    private readonly object sync = new();

    public Task<string?> ReadAsync(string account, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        lock (sync)
        {
            var value = preferences.GetString(account, null);
            if (value is null) return Task.FromResult<string?>(null);
            var parts = value.Split(':', 2);
            using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
            using var spec = new GCMParameterSpec(128, Convert.FromBase64String(parts[0]));
            cipher.Init(CipherMode.DecryptMode, Key(), spec);
            return Task.FromResult<string?>(System.Text.Encoding.UTF8.GetString(cipher.DoFinal(Convert.FromBase64String(parts[1]))!));
        }
    }

    public Task WriteAsync(string account, string? value, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        lock (sync)
        {
            using var editor = preferences.Edit()!;
            if (value is null) editor.Remove(account);
            else
            {
                using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
                cipher.Init(CipherMode.EncryptMode, Key());
                var encrypted = cipher.DoFinal(System.Text.Encoding.UTF8.GetBytes(value))!;
                editor.PutString(account, Convert.ToBase64String(cipher.GetIV()!) + ":" + Convert.ToBase64String(encrypted));
            }
            if (!editor.Commit()) throw new InvalidOperationException("Android could not save this credential.");
            return Task.CompletedTask;
        }
    }

    private static IKey Key()
    {
        using var store = KeyStore.GetInstance("AndroidKeyStore")!;
        store.Load(null);
        if (store.ContainsAlias(Alias)) return store.GetKey(Alias, null)!;
        using var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, "AndroidKeyStore")!;
        using var spec = new KeyGenParameterSpec.Builder(Alias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)!.SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)!.Build()!;
        generator.Init(spec);
        return generator.GenerateKey()!;
    }
}
