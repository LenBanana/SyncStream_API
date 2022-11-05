using System;
using System.Diagnostics;

namespace SyncStreamAPI.Helper
{
    public class LinuxBash
    {
        public static void Bash(string cmd)
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
        }
    }
}
