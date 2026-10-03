using System.Security.Cryptography;
using Android.Security.Keystore;
using Balancia.Storage;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using CipherMode = Javax.Crypto.CipherMode;

namespace Balancia.Android;

/// <summary>A Keystore-held wrapping key protects the portable snapshot key in app-private storage.</summary>
internal static class AndroidSnapshotKeyStore
{
    private const string Alias = "balancia-snapshot-key-v1";

    private static IKey GetWrappingKey()
    {
        using var store = KeyStore.GetInstance("AndroidKeyStore")!;
        store.Load(null, null);
        if (store.ContainsAlias(Alias))
        {
            return store.GetKey(Alias, null)!;
        }

        using var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, "AndroidKeyStore")!;
        using var spec = new KeyGenParameterSpec.Builder(Alias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
            .SetKeySize(256)
            .Build();
        generator.Init(spec);
        return generator.GenerateKey()!;
    }

    internal static SnapshotKey? Load(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        if (new FileInfo(path).Length is < 28 or > 4096)
        {
            throw new InvalidDataException("The remembered snapshot key is invalid.");
        }

        var blob = File.ReadAllBytes(path);
        using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        using var wrappingKey = GetWrappingKey();
        using var spec = new GCMParameterSpec(128, blob[..12]);
        cipher.Init(CipherMode.DecryptMode, wrappingKey, spec);
        var secret = cipher.DoFinal(blob[12..])!;
        try
        {
            return SnapshotKey.ImportSecret(secret);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    internal static void Save(string path, SnapshotKey key)
    {
        var secret = key.ExportSecret();
        try
        {
            using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
            using var wrappingKey = GetWrappingKey();
            cipher.Init(CipherMode.EncryptMode, wrappingKey);
            var iv = cipher.GetIV()!;
            var encrypted = cipher.DoFinal(secret)!;
            var temporary = path + ".tmp";
            try
            {
                using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    output.Write(iv);
                    output.Write(encrypted);
                    output.Flush(flushToDisk: true);
                }

                File.Move(temporary, path, true);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }
}
