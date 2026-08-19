using System.IO;
using System.Text;

namespace GetRetechData
{
    public static class LogWriter
    {
        public static string LogDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "GetRetechData", "Logs");

        public static string CurrentLogFile => Path.Combine(LogDirectory, $"log_{DateTime.Now:yyyyMMdd}.txt");

        public static void Write(string message)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(CurrentLogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {message}{Environment.NewLine}", Encoding.UTF8);
            }
            catch
            {
            }
        }
    }
}