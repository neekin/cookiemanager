using System;
using System.IO;
using System.Text.Json;

namespace CookieManager.Models
{
    public class AppConfig
    {
        public string ServerUrl { get; set; } = "http://localhost:3001";
        public string WebSocketUrl { get; set; } = "ws://localhost:3001";
        public bool UseHttps { get; set; } = false;
        public int ConnectionTimeout { get; set; } = 30000; // 30秒
        public bool AutoConnect { get; set; } = true;
        public string LastUsedServerUrl { get; set; } = "";

        private static readonly string ConfigFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CookieManager",
            "config.json"
        );

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    var json = File.ReadAllText(ConfigFilePath);
                    var config = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
                    
                    // 确保WebSocket URL与HTTP URL匹配
                    config.UpdateWebSocketUrl();
                    
                    return config;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载配置失败: {ex.Message}");
            }

            return new AppConfig();
        }

        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(ConfigFilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory!);
                }

                // 更新WebSocket URL
                UpdateWebSocketUrl();

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                var json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(ConfigFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存配置失败: {ex.Message}");
            }
        }

        public void UpdateWebSocketUrl()
        {
            if (string.IsNullOrEmpty(ServerUrl))
                return;

            try
            {
                var uri = new Uri(ServerUrl);
                var scheme = uri.Scheme == "https" ? "wss" : "ws";
                WebSocketUrl = $"{scheme}://{uri.Host}:{uri.Port}";
                UseHttps = uri.Scheme == "https";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"更新WebSocket URL失败: {ex.Message}");
                // fallback
                WebSocketUrl = ServerUrl.Replace("http://", "ws://").Replace("https://", "wss://");
            }
        }

        public bool IsValidConfiguration()
        {
            return !string.IsNullOrEmpty(ServerUrl) && 
                   !string.IsNullOrEmpty(WebSocketUrl) &&
                   Uri.IsWellFormedUriString(ServerUrl, UriKind.Absolute) &&
                   Uri.IsWellFormedUriString(WebSocketUrl, UriKind.Absolute);
        }

        public static AppConfig GetDefaultConfig()
        {
            return new AppConfig
            {
                ServerUrl = "http://localhost:3001",
                WebSocketUrl = "ws://localhost:3001",
                UseHttps = false,
                ConnectionTimeout = 30000,
                AutoConnect = true,
                LastUsedServerUrl = ""
            };
        }
    }
}
