using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SyncStreamAPI.Helper
{
    public class Encryption
    {
        public static string Sha256(string randomString)
        {
            var crypt = new SHA256Managed();
            string hash = String.Empty;
            byte[] crypto = crypt.ComputeHash(Encoding.ASCII.GetBytes(randomString));
            foreach (byte theByte in crypto)
            {
                hash += theByte.ToString("x2");
            }
            return hash;
        }

        public static string SHA256CheckSum(string filePath)
        {
            using (SHA256 SHA256 = SHA256Managed.Create())
            {
                using (FileStream fileStream = File.OpenRead(filePath))
                {
                    return Convert.ToBase64String(SHA256.ComputeHash(fileStream));
                }
            }
        }

        public static string CreateMD5(string input)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] inputBytes = Encoding.ASCII.GetBytes(input);
                byte[] hashBytes = md5.ComputeHash(inputBytes);
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < hashBytes.Length; i++)
                {
                    sb.Append(hashBytes[i].ToString("X2"));
                }
                return sb.ToString();
            }
        }
    }
}
