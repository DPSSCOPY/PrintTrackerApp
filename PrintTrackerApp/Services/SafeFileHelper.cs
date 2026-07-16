using System;
using System.IO;
using System.Text;

namespace PrintTrackerApp.Services
{
    public static class SafeFileHelper
    {
        public static void WriteAllText(string filePath, string contents, Encoding? encoding = null)
        {
            if (encoding == null) encoding = Encoding.UTF8;
            byte[] data = encoding.GetBytes(contents);
            WriteAllBytes(filePath, data);
        }

        public static void WriteAllBytes(string filePath, byte[] bytes)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return;

            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string tempFilePath = filePath + ".tmp";
            try
            {
                using (var fs = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fs.Write(bytes, 0, bytes.Length);
                    fs.Flush(flushToDisk: true);
                }
                
                File.Move(tempFilePath, filePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempFilePath))
                {
                    try { File.Delete(tempFilePath); } catch { }
                }
            }
        }

        public static void WriteSafe(string filePath, Action<StreamWriter> writeAction)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return;

            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string tempFilePath = filePath + ".tmp";
            try
            {
                using (var fs = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(fs, Encoding.UTF8))
                {
                    writeAction(writer);
                    writer.Flush();
                    fs.Flush(flushToDisk: true);
                }

                File.Move(tempFilePath, filePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempFilePath))
                {
                    try { File.Delete(tempFilePath); } catch { }
                }
            }
        }
    }
}
