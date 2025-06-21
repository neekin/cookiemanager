using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CookieManager.Models;

namespace CookieManager
{
    public partial class ServerConfigWindow : Window
    {
        private AppConfig config;
        private readonly HttpClient httpClient;

        public AppConfig Config => config;
        public bool ConfigurationSaved { get; private set; } = false;

        public ServerConfigWindow(AppConfig? existingConfig = null)
        {
            InitializeComponent();
            
            config = existingConfig ?? AppConfig.Load();
            httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10); // 测试连接超时
            
            LoadConfiguration();
        }

        private void LoadConfiguration()
        {
            ServerUrlTextBox.Text = config.ServerUrl;
            UseHttpsCheckBox.IsChecked = config.UseHttps;
            TimeoutTextBox.Text = (config.ConnectionTimeout / 1000).ToString();
            AutoConnectCheckBox.IsChecked = config.AutoConnect;

            // 根据URL设置预设选择
            if (config.ServerUrl == "http://localhost:3001")
            {
                PresetServersComboBox.SelectedIndex = 0;
            }
            else if (config.ServerUrl == "https://localhost:3001")
            {
                PresetServersComboBox.SelectedIndex = 1;
            }
            else
            {
                PresetServersComboBox.SelectedIndex = 2;
            }

            UpdateConnectionStatus("未连接", false);
        }

        private void UseHttpsCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            UpdateServerUrlScheme(true);
        }

        private void UseHttpsCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            UpdateServerUrlScheme(false);
        }

        private void UpdateServerUrlScheme(bool useHttps)
        {
            var url = ServerUrlTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(url)) return;

            try
            {
                var uri = new Uri(url);
                var newScheme = useHttps ? "https" : "http";
                var newUrl = $"{newScheme}://{uri.Host}:{uri.Port}";
                ServerUrlTextBox.Text = newUrl;
            }
            catch
            {
                // 如果URL格式不正确，不做修改
            }
        }

        private void PresetServersComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PresetServersComboBox.SelectedItem is ComboBoxItem item && item.Tag is string url)
            {
                ServerUrlTextBox.Text = url;
                UseHttpsCheckBox.IsChecked = url.StartsWith("https");
            }
        }

        private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            var url = ServerUrlTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(url))
            {
                MessageBox.Show("请输入服务器地址", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            TestConnectionButton.IsEnabled = false;
            UpdateConnectionStatus("正在测试连接...", null);

            try
            {
                // 测试HTTP连接
                var response = await httpClient.GetAsync($"{url}/api/browser/status");
                
                if (response.IsSuccessStatusCode)
                {
                    UpdateConnectionStatus("连接成功", true);
                    MessageBox.Show("服务器连接测试成功！", "连接成功", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    UpdateConnectionStatus($"连接失败: HTTP {response.StatusCode}", false);
                    MessageBox.Show($"服务器响应错误: {response.StatusCode}", "连接失败", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (HttpRequestException ex)
            {
                UpdateConnectionStatus("连接失败: 网络错误", false);
                MessageBox.Show($"网络连接失败: {ex.Message}", "连接失败", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (TaskCanceledException)
            {
                UpdateConnectionStatus("连接失败: 超时", false);
                MessageBox.Show("连接超时，请检查服务器地址和网络连接", "连接超时", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                UpdateConnectionStatus($"连接失败: {ex.Message}", false);
                MessageBox.Show($"连接测试失败: {ex.Message}", "连接失败", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                TestConnectionButton.IsEnabled = true;
            }
        }

        private void UpdateConnectionStatus(string message, bool? isSuccess)
        {
            ConnectionStatusTextBlock.Text = message;
            
            if (isSuccess == true)
            {
                ConnectionStatusTextBlock.Foreground = System.Windows.Media.Brushes.Green;
            }
            else if (isSuccess == false)
            {
                ConnectionStatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
            }
            else
            {
                ConnectionStatusTextBlock.Foreground = System.Windows.Media.Brushes.Orange;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (ValidateAndSaveConfiguration())
            {
                ConfigurationSaved = true;
                DialogResult = true;
                Close();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private bool ValidateAndSaveConfiguration()
        {
            var serverUrl = ServerUrlTextBox.Text?.Trim();
            
            if (string.IsNullOrEmpty(serverUrl))
            {
                MessageBox.Show("请输入服务器地址", "验证错误", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                ServerUrlTextBox.Focus();
                return false;
            }

            if (!Uri.IsWellFormedUriString(serverUrl, UriKind.Absolute))
            {
                MessageBox.Show("服务器地址格式不正确", "验证错误", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                ServerUrlTextBox.Focus();
                return false;
            }

            if (!int.TryParse(TimeoutTextBox.Text, out int timeout) || timeout < 5 || timeout > 300)
            {
                MessageBox.Show("连接超时必须在5-300秒之间", "验证错误", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                TimeoutTextBox.Focus();
                return false;
            }

            // 保存配置
            config.ServerUrl = serverUrl;
            config.UseHttps = UseHttpsCheckBox.IsChecked == true;
            config.ConnectionTimeout = timeout * 1000;
            config.AutoConnect = AutoConnectCheckBox.IsChecked == true;
            config.LastUsedServerUrl = serverUrl;
            
            // 更新WebSocket URL
            config.UpdateWebSocketUrl();

            try
            {
                config.Save();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存配置失败: {ex.Message}", "保存错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            httpClient?.Dispose();
            base.OnClosed(e);
        }
    }
}
