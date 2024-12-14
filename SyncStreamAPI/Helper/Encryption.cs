using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SyncStreamAPI.Helper;

public class Encryption
{
    public static string Sha256(string randomString)
    {
        var crypt = new SHA256Managed();
        var hash = string.Empty;
        var crypto = crypt.ComputeHash(Encoding.ASCII.GetBytes(randomString));
        foreach (var theByte in crypto) hash += theByte.ToString("x2");
        return hash;
    }

    public static string SHA256CheckSum(string filePath)
    {
        using (var SHA256 = System.Security.Cryptography.SHA256.Create())
        {
            using (var fileStream = File.OpenRead(filePath))
            {
                return Convert.ToBase64String(SHA256.ComputeHash(fileStream));
            }
        }
    }

    public static string CreateMD5(string input)
    {
        using (var md5 = MD5.Create())
        {
            var inputBytes = Encoding.ASCII.GetBytes(input);
            var hashBytes = md5.ComputeHash(inputBytes);
            var sb = new StringBuilder();
            for (var i = 0; i < hashBytes.Length; i++) sb.Append(hashBytes[i].ToString("X2"));
            return sb.ToString();
        }
    }
}