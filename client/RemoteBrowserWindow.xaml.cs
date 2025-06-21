using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json;
using Microsoft.Web.WebView2.Core;
using System.IO;
using System.Text;
using WebSocketSharp;
using System.Linq;

namespace CookieManager
{
    public partial class RemoteBrowserWindow : Window
    {
        private readonly HttpClient httpClient;
        private readonly string serverUrl = "http://localhost:3001";
        private readonly string wsUrl = "ws://localhost:3001";
        private WebSocket? webSocket;
        private int instanceId;
        private string instanceUrl;
        private bool isConnected = false;

        public RemoteBrowserWindow(int instanceId, string url)
        {
            InitializeComponent();
            
            this.instanceId = instanceId;
            this.instanceUrl = url;
            this.httpClient = new HttpClient();
            
            Title = $"远程浏览器实例 - {url} (ID: {instanceId})";
            InstanceInfoTextBlock.Text = $"实例ID: {instanceId} | URL: {url}";
            UrlTextBox.Text = url;
            
            InitializeAsync();
        }        private async void InitializeAsync()
        {
            try
            {
                StatusTextBlock.Text = "正在初始化WebView...";
                await InitializeWebView();
                
                StatusTextBlock.Text = "正在连接到远程实例...";
                await ConnectToRemoteInstance();
            }
            catch (Exception ex)
            {
                ShowError($"初始化失败: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"RemoteBrowserWindow初始化失败: {ex}");
            }
        }

        private async Task InitializeWebView()
        {
            try
            {
                await RemoteWebView.EnsureCoreWebView2Async();
                RemoteWebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
                RemoteWebView.CoreWebView2.DOMContentLoaded += OnDOMContentLoaded;
            }
            catch (Exception ex)
            {
                ShowError($"WebView初始化失败: {ex.Message}");
            }
        }        private async Task ConnectToRemoteInstance()
        {
            try
            {
                // 首先检查实例是否正在运行
                var runningResponse = await httpClient.GetAsync($"{serverUrl}/api/browser/running-instances");
                
                if (runningResponse.IsSuccessStatusCode)
                {
                    var runningContent = await runningResponse.Content.ReadAsStringAsync();
                    var runningResult = JsonConvert.DeserializeObject<RunningInstancesResponse>(runningContent);
                    
                    bool isRunning = runningResult?.Success == true && 
                                   runningResult.Data?.Any(r => r.InstanceId == instanceId) == true;
                    
                    if (!isRunning)
                    {
                        // 实例没有运行，需要先启动
                        StatusTextBlock.Text = "实例未运行，正在启动...";
                        
                        var startResult = await StartRemoteInstance();
                        if (!startResult)
                        {
                            ShowError("启动远程实例失败");
                            return;
                        }
                        
                        // 等待实例启动
                        await Task.Delay(3000);
                    }
                }
                
                // 获取远程实例的内容
                var response = await httpClient.GetAsync($"{serverUrl}/api/browser/instance/{instanceId}/content");
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<RemoteContentResponse>(content);
                    
                    if (result?.Success == true)
                    {
                        await DisplayRemoteContent(result.Data);
                        ConnectWebSocket();
                        HideConnecting();
                    }
                    else
                    {
                        ShowError($"无法获取远程实例内容: {result?.Message}");
                    }
                }
                else
                {
                    ShowError($"连接失败: HTTP {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                ShowError($"连接失败: {ex.Message}");            }
        }

        private Task DisplayRemoteContent(RemoteContentData data)
        {
            try
            {
                if (!string.IsNullOrEmpty(data.Html))
                {
                    // 直接显示HTML内容
                    RemoteWebView.CoreWebView2.NavigateToString(data.Html);
                }
                else if (!string.IsNullOrEmpty(data.Url))
                {
                    // 如果没有HTML，则导航到URL
                    RemoteWebView.CoreWebView2.Navigate(data.Url);
                }
                
                StatusTextBlock.Text = "连接状态: 已连接";
                ConnectionStatusTextBlock.Text = "已连接";
                isConnected = true;
                
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                ShowError($"显示内容失败: {ex.Message}");
                return Task.CompletedTask;
            }
        }

        private void ConnectWebSocket()
        {
            try
            {
                webSocket = new WebSocket($"{wsUrl}/instance/{instanceId}");
                
                webSocket.OnOpen += (sender, e) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        StatusTextBlock.Text = "连接状态: WebSocket已连接";
                        ConnectionStatusTextBlock.Text = "实时同步";
                    });
                };

                webSocket.OnMessage += (sender, e) =>
                {
                    Dispatcher.Invoke(async () =>
                    {
                        try
                        {
                            var message = JsonConvert.DeserializeObject<RemoteUpdateMessage>(e.Data);
                            await HandleRemoteUpdate(message);
                        }
                        catch (Exception ex)
                        {
                            StatusTextBlock.Text = $"消息处理错误: {ex.Message}";
                        }
                    });
                };

                webSocket.OnError += (sender, e) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        StatusTextBlock.Text = $"WebSocket错误: {e.Message}";
                    });
                };

                webSocket.OnClose += (sender, e) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        StatusTextBlock.Text = "连接状态: WebSocket已断开";
                        ConnectionStatusTextBlock.Text = "连接断开";
                    });
                };

                webSocket.Connect();
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"WebSocket连接失败: {ex.Message}";
            }
        }        private Task HandleRemoteUpdate(RemoteUpdateMessage message)
        {
            switch (message.Type)
            {
                case "navigation":
                    if (!string.IsNullOrEmpty(message.Url))
                    {
                        UrlTextBox.Text = message.Url;                        if (message.Url != RemoteWebView.CoreWebView2.Source)
                        {
                            RemoteWebView.CoreWebView2.Navigate(message.Url);
                        }
                    }
                    break;
                case "content":                    if (!string.IsNullOrEmpty(message.Html))
                    {
                        RemoteWebView.CoreWebView2.NavigateToString(message.Html);
                    }
                    break;
                case "screenshot":
                    // 可以实现截图显示
                    break;
            }
            
            return Task.CompletedTask;
        }

        private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                StatusTextBlock.Text = "连接状态: 页面加载完成";
            }
            else
            {
                StatusTextBlock.Text = "连接状态: 页面加载失败";
            }
        }

        private void OnDOMContentLoaded(object? sender, CoreWebView2DOMContentLoadedEventArgs e)
        {
            StatusTextBlock.Text = "连接状态: DOM加载完成";
        }

        private void ShowError(string message)
        {
            ConnectingPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Visible;
            ErrorTextBlock.Text = message;
            StatusTextBlock.Text = $"错误: {message}";
            ConnectionStatusTextBlock.Text = "连接失败";
        }

        private void HideConnecting()
        {
            ConnectingPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Collapsed;
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (isConnected)
            {
                try
                {
                    RemoteWebView.CoreWebView2.Reload();
                    
                    // 通知服务器刷新远程实例
                    var response = await httpClient.PostAsync(
                        $"{serverUrl}/api/browser/instance/{instanceId}/refresh", null);
                }
                catch (Exception ex)
                {
                    StatusTextBlock.Text = $"刷新失败: {ex.Message}";
                }
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (isConnected && RemoteWebView.CoreWebView2.CanGoBack)
            {
                RemoteWebView.CoreWebView2.GoBack();
            }
        }

        private void ForwardButton_Click(object sender, RoutedEventArgs e)
        {
            if (isConnected && RemoteWebView.CoreWebView2.CanGoForward)
            {
                RemoteWebView.CoreWebView2.GoForward();
            }
        }

        private async void GoButton_Click(object sender, RoutedEventArgs e)
        {
            var url = UrlTextBox.Text?.Trim();
            if (!string.IsNullOrEmpty(url) && isConnected)
            {
                try
                {
                    RemoteWebView.CoreWebView2.Navigate(url);
                    
                    // 通知服务器导航到新URL
                    var requestData = new { url = url };
                    var json = JsonConvert.SerializeObject(requestData);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    
                    await httpClient.PostAsync(
                        $"{serverUrl}/api/browser/instance/{instanceId}/navigate", content);
                }
                catch (Exception ex)
                {
                    StatusTextBlock.Text = $"导航失败: {ex.Message}";
                }
            }
        }

        private async void ScreenshotButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var response = await httpClient.GetAsync(
                    $"{serverUrl}/api/browser/instance/{instanceId}/screenshot");
                
                if (response.IsSuccessStatusCode)
                {
                    var screenshotData = await response.Content.ReadAsByteArrayAsync();
                    
                    // 保存截图到文件
                    var fileName = $"screenshot_{instanceId}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                    var filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);
                    
                    await File.WriteAllBytesAsync(filePath, screenshotData);
                    
                    StatusTextBlock.Text = $"截图已保存: {fileName}";
                    MessageBox.Show($"截图已保存到桌面: {fileName}", "截图成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    StatusTextBlock.Text = "截图失败";
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"截图失败: {ex.Message}";
            }
        }

        private async void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorPanel.Visibility = Visibility.Collapsed;
            ConnectingPanel.Visibility = Visibility.Visible;
            
            await ConnectToRemoteInstance();
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                webSocket?.Close();
                httpClient?.Dispose();
            }
            catch
            {
                // 忽略清理时的异常
            }
            
            base.OnClosed(e);
        }        private async Task<bool> StartRemoteInstance()
        {
            try
            {
                // 使用重新启动API而不是创建新实例
                var response = await httpClient.PostAsync($"{serverUrl}/api/browser/instance/{instanceId}/restart", null);
                var responseText = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<RestartInstanceResponse>(responseText);
                
                return result?.Success == true;
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"启动实例失败: {ex.Message}";
                return false;
            }
        }}

    // 数据模型
    public class RemoteContentResponse
    {
        public bool Success { get; set; }
        public RemoteContentData? Data { get; set; }
        public string? Message { get; set; }
    }

    public class RemoteContentData
    {
        public string? Url { get; set; }
        public string? Html { get; set; }
        public string? Title { get; set; }
        public byte[]? Screenshot { get; set; }
    }

    public class RemoteUpdateMessage
    {
        public string? Type { get; set; }
        public string? Url { get; set; }
        public string? Html { get; set; }        public string? Title { get; set; }
        public long Timestamp { get; set; }
    }

    public class CreateInstanceResponse
    {
        public bool success { get; set; }
        public string? message { get; set; }
        public string? url { get; set; }
        public int instanceId { get; set; }
    }

    public class RestartInstanceResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
    }
}
