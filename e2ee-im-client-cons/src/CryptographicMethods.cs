using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace E2EEClientCommandLine
{
    public static class CryptographicMethods
    {
        public static string EncryptStringToBase64_AES(string plainText, byte[] key, byte[] IV)
        {
            // Check arguments
            if (plainText == null || plainText.Length <= 0) { throw new ArgumentNullException("plainText"); }
            if (key == null || key.Length <= 0) { throw new ArgumentNullException("key"); }
            if (IV == null || IV.Length <= 0) { throw new ArgumentNullException("IV"); }

            byte[] encrypted;

            // Create AES object, with specified key and initialisation vector.
            using (Aes aes = Aes.Create())
            {
                aes.Key = key;
                //aes.GenerateIV();
                aes.IV = IV;

                // Create an encryptor to perform the stream transform
                ICryptoTransform encryptor = aes.CreateEncryptor(aes.Key, aes.IV);

                // Create streams used for encryption.
                using (MemoryStream msEncrypt = new MemoryStream())
                {
                    using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                    {
                        using (StreamWriter swEncrypt = new StreamWriter(csEncrypt))
                        {
                            // Write all data to the stream.
                            swEncrypt.Write(plainText);
                        }
                        encrypted = msEncrypt.ToArray();
                    }
                }
            }

            // Return encrypted bytes converted to Base64 string.
            return Convert.ToBase64String(encrypted);
        }

        public static string DecryptStringFromBase64_AES (string cipherString, byte[] key, byte[] IV)
        {
            // Check args
            if (cipherString == null || cipherString.Length <= 0) { throw new ArgumentNullException("cipherString"); }
            if (key == null || key.Length <= 0) { throw new ArgumentNullException("key"); }
            if (IV == null || IV.Length <= 0) { throw new ArgumentNullException("IV"); }

            // Decode from base64
            byte[] cipherText = Convert.FromBase64String(cipherString);

            // String to hold decrypted text.
            string plainText = null;

            // Create AES with specified IV and key.
            using (Aes aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = IV;

                // Create decryptor
                ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV);

                // Create streams
                using (MemoryStream msDecrypt = new MemoryStream())
                {
                    using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                    {
                        using (StreamReader srDecrypt = new StreamReader(csDecrypt))
                        {
                            // Read the decrypted bytes from decrypting stream and add to stream.
                            plainText = srDecrypt.ReadToEnd();
                        }
                    }
                }
            }

            // Return decrypted string.
            return plainText;
        }

        private static readonly byte[] SALT = new byte[] { 0x26, 0xdc, 0xff, 0x00, 0xad, 0xed, 0x7a, 0xee, 0xc5, 0xfe, 0x07, 0xaf, 0x4d, 0x08, 0x22, 0x3c };

        public static byte[] DeriveKeyFromSecret(string secret)
        {
            Rfc2898DeriveBytes pdb = new Rfc2898DeriveBytes(secret, SALT);
            return pdb.GetBytes(DifHelConstants.AES_KEY_BYTES);
        }
    }
}
