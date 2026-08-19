using System.IO;
using System.Text.Json;

namespace GetRetechData
{
    public class AppConfig
    {
        public int AutoLoadIntervalSeconds { get; set; }
        public int ConnCheckIntervalSeconds { get; set; }
        public string? OracleHost { get; set; }
        public string? OraclePort { get; set; }
        public string? OracleSid { get; set; }
        public string? OracleUser { get; set; }
        public string? OraclePass { get; set; }
        public string? ImportConnString { get; set; }
    }

    public static class ConfigManager
    {
        public static string ConfigPath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "GetRetechData", "config.json");

        private static AppConfig _config = new AppConfig();
        private static FileSystemWatcher? _watcher;

        public static AppConfig Load()
        {
            try
            {
                string? dir = Path.GetDirectoryName(ConfigPath);
                if (dir != null) Directory.CreateDirectory(dir);
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    _config = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
                }
                else
                {
                    WriteDefaultConfig();
                    string json = File.ReadAllText(ConfigPath);
                    _config = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
                }
            }
            catch
            {
                _config = new AppConfig();
            }
            return _config;
        }

        public static void Watch(Action<AppConfig> onChanged)
        {
            try
            {
                string? dir = Path.GetDirectoryName(ConfigPath);
                if (dir == null) return;

                var watcher = new FileSystemWatcher(dir, "config.json")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                    EnableRaisingEvents = true
                };
                _watcher = watcher;

                void Reload(object sender, FileSystemEventArgs e)
                {
                    try
                    {
                        Thread.Sleep(300);
                        onChanged?.Invoke(Load());
                    }
                    catch
                    {
                    }
                }

                watcher.Changed += Reload;
                watcher.Created += Reload;
                watcher.Renamed += (s, e) => Reload(s, e);
            }
            catch
            {
            }
        }

        private static void WriteDefaultConfig()
        {
            string json = @"{
  ""AutoLoadIntervalSeconds"": 3600,
  ""ConnCheckIntervalSeconds"": 60,
  ""OracleHost"": ""10.32.159.101"",
  ""OraclePort"": ""1523"",
  ""OracleSid"": ""hbidbrms"",
  ""OracleUser"": ""rmsprd"",
  ""OraclePass"": ""rmsidbit"",
  ""ImportConnString"": ""data source=10.110.32.58;initial catalog=RMS_DataInit;MultipleActiveResultSets=True;integrated security=false;user id=app.admin;password=@dm1n_app;Connection Timeout=0;Max Pool Size=2000;TrustServerCertificate=True""
}";
            File.WriteAllText(ConfigPath, json);
        }

        public static void Save()
        {
            try
            {
                string? dir = Path.GetDirectoryName(ConfigPath);
                if (dir != null) Directory.CreateDirectory(dir);
                string json = JsonSerializer.Serialize(_config ?? new AppConfig(), new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch
            {
            }
        }
    }
}