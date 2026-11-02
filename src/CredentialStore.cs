using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Pixelroom
{
    public sealed class CredentialStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Pixelroom.PixelDrain.ApiKey.v1");
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        private readonly string root, path;
        public CredentialStore(string root) { this.root = root; path = Path.Combine(root, "api-key.dat"); }

        public string Load()
        {
            byte[] plain = null;
            try
            {
                using (FileStream input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (input.Length < 1 || input.Length > 16384) throw new CryptographicException();
                    byte[] encrypted = new byte[(int)input.Length]; int offset = 0, read;
                    while (offset < encrypted.Length && (read = input.Read(encrypted, offset, encrypted.Length - offset)) > 0) offset += read;
                    if (offset != encrypted.Length) throw new IOException();
                    plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
                }
                string key = Utf8.GetString(plain); Validate(key); return key;
            }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
            catch (Exception ex)
            {
                if (!IsStorageError(ex) && !(ex is ArgumentException)) throw;
                throw new IOException("Your saved API key could not be read. Enter it again in Settings.");
            }
            finally { if (plain != null) Array.Clear(plain, 0, plain.Length); }
        }

        public void Save(string key)
        {
            Validate(key); byte[] plain = Utf8.GetBytes(key);
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[] encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
                Directory.CreateDirectory(root);
                using (FileStream output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { output.Write(encrypted, 0, encrypted.Length); output.Flush(true); }
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            }
            catch (Exception ex)
            {
                if (!IsStorageError(ex)) throw;
                throw new IOException("Your API key could not be saved. Turn off Remember API key to connect without saving it.");
            }
            finally
            {
                Array.Clear(plain, 0, plain.Length);
                try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        public void Delete()
        {
            try { File.Delete(path); }
            catch (DirectoryNotFoundException) { }
            catch (Exception ex)
            {
                if (!IsStorageError(ex)) throw;
                throw new IOException("Your saved API key could not be removed. Please try again.");
            }
        }

        private static void Validate(string key)
        {
            if (String.IsNullOrWhiteSpace(key) || key.Length > 512 || key != key.Trim() || key.IndexOfAny(new char[] { '\r', '\n', '\0' }) >= 0)
                throw new ArgumentException("Enter a valid PixelDrain API key.");
        }
        private static bool IsStorageError(Exception ex)
        { return ex is IOException || ex is UnauthorizedAccessException || ex is CryptographicException || ex is System.Security.SecurityException; }
    }
}
