using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace SyncStreamAPI.Helper
{
    public class LinuxBash
    {
        public static bool Bash(string cmd)
        {
            try
            {
                Console.WriteLine($"Bash command {cmd}");
                var escapedArgs = cmd.Replace("\"", "\\\"");

                var process = new Process()
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "/bin/bash",
                        Arguments = $"-c \"{escapedArgs}\"",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    }
                };
                process.Start();
                string result = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return false;
            }
        }



        /// <summary>
        /// Downloads a file from the specified URI
        /// </summary>
        /// <param name="uri"></param>
        /// <returns>Returns a byte array of the file that was downloaded</returns>
        /// <exception cref="InvalidOperationException"></exception>
        public static async Task<byte[]> DownloadFileBytesAsync(string uri)
        {
            System.Net.Http.HttpClient client = new System.Net.Http.HttpClient();
            using (client)
            {
                if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri uriResult))
                {
                    throw new InvalidOperationException("URI is invalid.");
                }

                byte[] fileBytes = await client.GetByteArrayAsync(uri);
                return fileBytes;
            }
        }

        public static async Task DownloadYtDlp(string directoryPath = "")
        {
            if (string.IsNullOrEmpty(directoryPath)) { directoryPath = Directory.GetCurrentDirectory(); }

            string downloadUrl = "";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                downloadUrl = $"{General.YtDLPUrl}.exe";
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                downloadUrl = $"{General.YtDLPUrl}_macos";
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                downloadUrl = General.YtDLPUrl;
            }

            var downloadLocation = Path.Combine(directoryPath, Path.GetFileName(downloadUrl));
            var data = await DownloadFileBytesAsync(downloadUrl);
            File.WriteAllBytes(downloadLocation, data);
        }
    }
}
