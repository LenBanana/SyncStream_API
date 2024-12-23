﻿using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace SyncStreamAPI.Helper;

public class LinuxBash
{
    public static bool Bash(string cmd)
    {
        try
        {
            Console.WriteLine($"Bash command {cmd}");
            var escapedArgs = cmd.Replace("\"", "\\\"");

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"-c \"{escapedArgs}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            var result = process.StandardOutput.ReadToEnd();
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
    ///     Downloads a file from the specified URI
    /// </summary>
    /// <param name="uri"></param>
    /// <returns>Returns a byte array of the file that was downloaded</returns>
    /// <exception cref="InvalidOperationException"></exception>
    public static async Task<byte[]> DownloadFileBytesAsync(string uri)
    {
        var client = new HttpClient();
        using (client)
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var uriResult))
                throw new InvalidOperationException("URI is invalid.");

            var fileBytes = await client.GetByteArrayAsync(uri);
            return fileBytes;
        }
    }

    public static async Task DownloadYtDlp(string directoryPath = "")
    {
        if (string.IsNullOrEmpty(directoryPath)) directoryPath = Directory.GetCurrentDirectory();

        var downloadUrl = "";
        var downloadLocation = Path.Combine(directoryPath, "yt-dlp");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            downloadUrl = $"{General.YtDlpUrl}.exe";
            downloadLocation += ".exe";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) downloadUrl = $"{General.YtDlpUrl}_macos";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) downloadUrl = $"{General.YtDlpUrl}_linux";
        if (File.Exists(downloadLocation)) return;
        var data = await DownloadFileBytesAsync(downloadUrl);
        await File.WriteAllBytesAsync(downloadLocation, data);
    }
}