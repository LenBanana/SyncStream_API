using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ScreenIT.Helper
{
    public class FileCheck
    {
        public static async Task WaitForFile(string filePath)
        {
            var file = new FileInfo(filePath);
            if (!IsFileLocked(file))
                return;
            var watcher = new FileSystemWatcher(file.Directory.FullName);

            watcher.NotifyFilter = NotifyFilters.LastWrite;
            watcher.Filter = file.Name;

            var fileWrittenEvent = new ManualResetEvent(false);

            watcher.Changed += (sender, e) =>
            {
                if (e.FullPath == filePath && !IsFileLocked(file))
                {
                    fileWrittenEvent.Set();
                }
            };

            watcher.EnableRaisingEvents = true;
            await Task.Run(() => fileWrittenEvent.WaitOne());
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        public static bool CheckOverrideFile(string outputPath)
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
                return true;
            }
            return false;
        }

        private static bool IsFileLocked(FileInfo file)
        {
            try
            {
                using (var stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    stream.Close();
                }
                return false;
            }
            catch (IOException)
            {
                return true;
            }
        }
    }
}
