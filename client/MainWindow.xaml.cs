using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Web.WebView2.Core;
using System.Net.Http;
using Newtonsoft.Json;
using WebSocketSharp;
using System.Collections.ObjectModel;
using System.Globalization;
using CookieManager.Models;

namespace CookieManager
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly HttpClient httpClient;
        private WebSocket? webSocket;
        private AppConfig appConfig;
        private string serverUrl => appConfig.ServerUrl;
        private string wsUrl => appConfig.WebSocketUrl;
        private bool isWebSocketConnected = false;
        private System.Windows.Threading.DispatcherTimer? reconnectTimer;
        
        public ObservableCollection<BrowserInstance> BrowserInstances { get; set; }
        public ObservableCollection<ClosedInstance> ClosedInstances { get; set; }
        public ObservableCollection<DatabaseInstance> AllInstances { get; set; }

        public MainWindow()
        {
            InitializeComponent();
            
            // 加载配置
            appConfig = AppConfig.Load();
            
            // 如果是首次启动或配置无效，显示配置窗口
            if (!appConfig.IsValidConfiguration() || !appConfig.AutoConnect)
            {
                ShowServerConfigDialog();
            }
            
            httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMilliseconds(appConfig.ConnectionTimeout);
            
            BrowserInstances = new ObservableCollection<BrowserInstance>();
            ClosedInstances = new ObservableCollection<ClosedInstance>();
            AllInstances = new ObservableCollection<DatabaseInstance>();
            DataContext = this;
            
            // 初始化WebSocket状态显示
            UpdateWebSocketStatus();
            
            if (appConfig.AutoConnect && appConfig.IsValidConfiguration())
            {
                InitializeWebSocket();
                LoadBrowserStatus();
                LoadClosedInstances();
                LoadAllInstances();
            }
        }

        private void InitializeWebSocket()
        {
            try
            {
                webSocket = new WebSocket(wsUrl);
                
                webSocket.OnOpen += (sender, e) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        isWebSocketConnected = true;
                        UpdateWebSocketStatus();
                        StatusTextBlock.Text = "WebSocket连接成功";
                        
                        // 停止重连定时器
                        reconnectTimer?.Stop();
                    });
                };
                
                webSocket.OnMessage += (sender, e) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            var message = JsonConvert.DeserializeObject<WebSocketMessage>(e.Data);
                            HandleWebSocketMessage(message);
                        }
                        catch (Exception ex)
                        {
                            StatusTextBlock.Text = $"WebSocket消息处理错误: {ex.Message}";
                        }
                    });
                };

                webSocket.OnError += (sender, e) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        isWebSocketConnected = false;
                        UpdateWebSocketStatus();
                        StatusTextBlock.Text = $"WebSocket错误: {e.Message}";
                        StartReconnectTimer();
                    });
                };

                webSocket.OnClose += (sender, e) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        isWebSocketConnected = false;
                        UpdateWebSocketStatus();
                        StatusTextBlock.Text = "WebSocket连接已断开";
                        StartReconnectTimer();
                    });
                };

                webSocket.Connect();
            }
            catch (Exception ex)
            {
                isWebSocketConnected = false;
                UpdateWebSocketStatus();
                StatusTextBlock.Text = $"WebSocket连接失败: {ex.Message}";
                StartReconnectTimer();
            }
        }

        private void HandleWebSocketMessage(WebSocketMessage message)
        {
            switch (message.Type)
            {
                case "status":
                    UpdateBrowserStatus(message.Data);
                    break;
                case "keepAlive":
                    StatusTextBlock.Text = $"Cookie保活任务执行 - {message.Timestamp}";
                    break;
            }
        }

        private async void LoadBrowserStatus()
        {
            try
            {
                var response = await httpClient.GetStringAsync($"{serverUrl}/api/browser/status");
                var status = JsonConvert.DeserializeObject<BrowserStatus>(response);
                UpdateBrowserStatus(status);
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"加载浏览器状态失败: {ex.Message}";
            }
        }

        private async void LoadClosedInstances()
        {
            try
            {
                var response = await httpClient.GetStringAsync($"{serverUrl}/api/browser/closed-instances");
                var result = JsonConvert.DeserializeObject<ClosedInstancesResponse>(response);
                
                if (result.Success)
                {
                    ClosedInstances.Clear();
                    foreach (var instance in result.Data.Instances)
                    {
                        ClosedInstances.Add(new ClosedInstance
                        {
                            Url = instance.Url,
                            ClosedAt = instance.ClosedAt,
                            LastAccessed = instance.LastAccessed,
                            RuntimeMinutes = instance.RuntimeMinutes,
                            CookiesCount = instance.CookiesCount
                        });
                    }
                    
                    ClosedInstancesCountTextBlock.Text = $"已关闭实例数量: {result.Data.TotalClosed}";
                    CurrentIndexTextBlock.Text = $"当前处理索引: {result.Data.CurrentIndex}";
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"加载已关闭实例失败: {ex.Message}";
            }
        }

        private async void LoadAllInstances()
        {
            try
            {
                // 获取所有实例
                var response = await httpClient.GetStringAsync($"{serverUrl}/api/instances");
                var result = JsonConvert.DeserializeObject<AllInstancesResponse>(response);
                
                if (result?.Success == true && result.Data != null)
                {
                    // 获取运行中的实例ID列表
                    var runningInstanceIds = new HashSet<int>();
                    try
                    {
                        var runningResponse = await httpClient.GetStringAsync($"{serverUrl}/api/browser/running-instances");
                        var runningResult = JsonConvert.DeserializeObject<RunningInstancesResponse>(runningResponse);
                        
                        if (runningResult?.Success == true && runningResult.Data != null)
                        {
                            foreach (var runningInstance in runningResult.Data)
                            {
                                runningInstanceIds.Add(runningInstance.InstanceId);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"获取运行中实例失败: {ex.Message}");
                    }
                    
                    AllInstances.Clear();
                    foreach (var instance in result.Data)
                    {
                        // 根据实际运行状态更新 is_active 字段
                        instance.is_active = runningInstanceIds.Contains(instance.id);
                        AllInstances.Add(instance);
                    }
                    
                    var activeCount = AllInstances.Count(i => i.is_active);
                    StatusTextBlock.Text = $"已加载 {result.Data.Count} 个实例，其中 {activeCount} 个正在运行";
                }
                else
                {
                    StatusTextBlock.Text = "未找到实例数据或服务器返回错误";
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"加载所有实例失败: {ex.Message}";
            }
        }

        private async void UpdateBrowserStatus(BrowserStatus status)
        {
            BrowserInstances.Clear();
            
            // 获取运行中的实例详细信息
            try
            {
                var response = await httpClient.GetStringAsync($"{serverUrl}/api/browser/running-instances");
                var result = JsonConvert.DeserializeObject<RunningInstancesResponse>(response);
                
                if (result?.Success == true && result.Data != null)
                {
                    foreach (var instance in result.Data)
                    {
                        BrowserInstances.Add(new BrowserInstance
                        {
                            InstanceId = instance.InstanceId,
                            Url = instance.Url,
                            Status = instance.Status,
                            CreateTime = instance.StartTime
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                // 如果获取运行实例失败，使用原有逻辑
                if (status.IsRunning)
                {
                    BrowserInstances.Add(new BrowserInstance
                    {
                        InstanceId = 0, // 未知ID
                        Url = status.Url,
                        Status = "运行中",
                        CreateTime = DateTime.Now
                    });
                }
            }

            BrowserStatusTextBlock.Text = status.IsRunning ? 
                $"运行中 ({status.RunningInstancesCount} 个实例)" : "未运行";
            CurrentUrlTextBlock.Text = status.Url ?? "无";
            ClientCountTextBlock.Text = $"客户端连接数: {status.ClientsConnected}";
            BackgroundTaskTextBlock.Text = status.BackgroundTaskActive ? "后台任务: 活跃" : "后台任务: 未活跃";
        }

        private void UpdateWebSocketStatus()
        {
            if (isWebSocketConnected)
            {
                WebSocketStatusTextBlock.Text = "服务器连接: 已连接";
                WebSocketStatusTextBlock.Foreground = Brushes.Green;
            }
            else
            {
                WebSocketStatusTextBlock.Text = "服务器连接: 未连接";
                WebSocketStatusTextBlock.Foreground = Brushes.Red;
            }
        }

        private void StartReconnectTimer()
        {
            if (reconnectTimer == null)
            {
                reconnectTimer = new System.Windows.Threading.DispatcherTimer();
                reconnectTimer.Interval = TimeSpan.FromSeconds(5); // 每5秒尝试重连
                reconnectTimer.Tick += (sender, e) =>
                {
                    if (!isWebSocketConnected)
                    {
                        try
                        {
                            webSocket?.Close();
                            InitializeWebSocket();
                        }
                        catch (Exception ex)
                        {
                            StatusTextBlock.Text = $"重连失败: {ex.Message}";
                        }
                    }
                    else
                    {
                        reconnectTimer.Stop();
                    }
                };
            }
            
            if (!reconnectTimer.IsEnabled)
            {
                reconnectTimer.Start();
            }
        }

        private async void CreateBrowserButton_Click(object sender, RoutedEventArgs e)
        {
            var url = UrlTextBox.Text?.Trim();
            
            if (string.IsNullOrEmpty(url))
            {
                MessageBox.Show("请输入URL", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                CreateBrowserButton.IsEnabled = false;
                StatusTextBlock.Text = "正在创建浏览器实例...";

                var requestData = new { 
                    url = url,
                    name = NameTextBox.Text?.Trim(),
                    description = DescriptionTextBox.Text?.Trim(),
                    groupName = GroupTextBox.Text?.Trim(),
                    tags = TagsTextBox.Text?.Trim()
                };
                var json = JsonConvert.SerializeObject(requestData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync($"{serverUrl}/api/browser/create", content);
                var responseText = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<ApiResponse>(responseText);

                if (result.Success)
                {
                    StatusTextBlock.Text = result.Message;
                    LoadBrowserStatus();
                    LoadAllInstances();
                }
                else
                {
                    MessageBox.Show(result.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusTextBlock.Text = result.Message;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"创建浏览器实例失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusTextBlock.Text = $"创建失败: {ex.Message}";
            }
            finally
            {
                CreateBrowserButton.IsEnabled = true;
            }
        }

        private async void CloseBrowserButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CloseBrowserButton.IsEnabled = false;
                StatusTextBlock.Text = "正在关闭浏览器实例...";

                var response = await httpClient.PostAsync($"{serverUrl}/api/browser/close", null);
                var responseText = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<ApiResponse>(responseText);

                StatusTextBlock.Text = result.Message;
                LoadBrowserStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"关闭浏览器实例失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusTextBlock.Text = $"关闭失败: {ex.Message}";
            }
            finally
            {
                CloseBrowserButton.IsEnabled = true;
            }
        }

        private void BrowserListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (BrowserListView.SelectedItem is BrowserInstance instance)
            {
                try
                {
                    var result = MessageBox.Show(
                        $"是否要打开远程浏览器实例？\n\n" +
                        $"实例ID: {instance.InstanceId}\n" +
                        $"URL: {instance.Url}\n" +
                        $"状态: {instance.Status}",
                        "远程浏览器实例",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        // 打开远程浏览器窗口
                        var remoteBrowserWindow = new RemoteBrowserWindow(instance.InstanceId, instance.Url);
                        remoteBrowserWindow.Title = $"远程浏览器 - {instance.Url} (ID: {instance.InstanceId})";
                        remoteBrowserWindow.Show();
                        
                        StatusTextBlock.Text = $"已打开远程实例窗口: {instance.Url}";
                    }
                }
                catch (Exception)
                {
                    MessageBox.Show("打开远程窗口失败", "错误", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadBrowserStatus();
            LoadClosedInstances();
            LoadAllInstances();
            StatusTextBlock.Text = "状态已刷新";
        }

        private async void RefreshInstancesButton_Click(object sender, RoutedEventArgs e)
        {
            LoadAllInstances();
            StatusTextBlock.Text = "实例列表已刷新";
        }

        private async void ViewStatsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var response = await httpClient.GetStringAsync($"{serverUrl}/api/statistics");
                var result = JsonConvert.DeserializeObject<StatisticsResponse>(response);
                
                if (result.Success)
                {
                    var stats = result.Data;
                    var message = $"统计信息:\n" +
                                 $"总实例数: {stats.total_instances}\n" +
                                 $"活跃实例数: {stats.active_instances}\n" +
                                 $"总会话数: {stats.total_sessions}\n" +
                                 $"总运行时间: {stats.total_runtime}分钟\n" +
                                 $"分组数: {stats.total_groups}";
                    
                    MessageBox.Show(message, "统计信息", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"获取统计信息失败: {ex.Message}";
            }
        }

        private void AllInstancesListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (AllInstancesListView.SelectedItem is DatabaseInstance instance)
            {
                try
                {
                    var result = MessageBox.Show(
                        $"是否要打开远程浏览器实例？\n\n" +
                        $"实例ID: {instance.id}\n" +
                        $"URL: {instance.url}\n" +
                        $"名称: {instance.name}\n" +
                        $"分组: {instance.group_name}",
                        "远程浏览器实例",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        // 打开远程浏览器窗口
                        var remoteBrowserWindow = new RemoteBrowserWindow(instance.id, instance.url);
                        remoteBrowserWindow.Title = $"远程浏览器 - {instance.name} (ID: {instance.id})";
                        remoteBrowserWindow.Show();
                        
                        StatusTextBlock.Text = $"已打开远程实例窗口: {instance.url}";
                    }
                }
                catch (Exception)
                {
                    MessageBox.Show("打开远程窗口失败", "错误", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ShowServerConfigDialog()
        {
            var configWindow = new ServerConfigWindow(appConfig);
            configWindow.Owner = this;
            
            var result = configWindow.ShowDialog();
            
            if (result == true && configWindow.ConfigurationSaved)
            {
                // 重新加载配置
                appConfig = configWindow.Config;
                httpClient.Timeout = TimeSpan.FromMilliseconds(appConfig.ConnectionTimeout);
                
                // 重新初始化连接
                if (appConfig.AutoConnect)
                {
                    InitializeWebSocket();
                    LoadBrowserStatus();
                    LoadClosedInstances();
                    LoadAllInstances();
                }
            }
            else if (!appConfig.IsValidConfiguration())
            {
                // 如果用户取消配置且没有有效配置，退出应用
                var exitResult = MessageBox.Show(
                    "未配置有效的服务器连接，是否退出应用程序？",
                    "配置需求",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                
                if (exitResult == MessageBoxResult.Yes)
                {
                    Application.Current.Shutdown();
                }
            }
        }
        
        private void ShowServerConfigMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ShowServerConfigDialog();
        }
        
        private void ConnectToServerMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (appConfig.IsValidConfiguration())
            {
                InitializeWebSocket();
                LoadBrowserStatus();
                LoadClosedInstances();
                LoadAllInstances();
            }
            else
            {
                ShowServerConfigDialog();
            }
        }
        
        private void DisconnectFromServerMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                reconnectTimer?.Stop();
                if (webSocket != null && webSocket.ReadyState == WebSocketState.Open)
                {
                    webSocket.Close();
                }
                isWebSocketConnected = false;
                UpdateWebSocketStatus();
                StatusTextBlock.Text = "已断开服务器连接";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"断开连接时出错: {ex.Message}";
            }
        }
        
        // 菜单事件处理方法
        private void ServerConfigMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ShowServerConfigDialog();
        }
        
        private void ReconnectMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 停止重连定时器
                reconnectTimer?.Stop();
                
                // 关闭现有连接
                if (webSocket != null && webSocket.ReadyState == WebSocketState.Open)
                {
                    webSocket.Close();
                }
                
                // 重新初始化连接
                if (appConfig.IsValidConfiguration())
                {
                    InitializeWebSocket();
                    LoadBrowserStatus();
                    LoadClosedInstances();
                    LoadAllInstances();
                    StatusTextBlock.Text = "正在重新连接到服务器...";
                }
                else
                {
                    MessageBox.Show("服务器配置无效，请先配置服务器连接。", "配置错误", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    ShowServerConfigDialog();
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"重连失败: {ex.Message}";
            }
        }
        
        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
        
        private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var aboutMessage = "Cookie管理器 v1.0\n\n" +
                              "这是一个基于WPF和Puppeteer的Cookie管理工具\n" +
                              "支持多实例管理、远程浏览器控制和Cookie保持\n\n" +
                              "功能特点:\n" +
                              "• 多浏览器实例管理\n" +
                              "• 远程浏览器画面展示\n" +
                              "• WebSocket实时状态同步\n" +
                              "• 灵活的服务器配置支持\n" +
                              "• 支持HTTP/HTTPS和WS/WSS协议\n\n" +
                              "当前服务器: " + appConfig.ServerUrl;
            
            MessageBox.Show(aboutMessage, "关于Cookie管理器", 
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        protected override void OnClosed(EventArgs e)
        {
            // 清理WebSocket连接
            try
            {
                reconnectTimer?.Stop();
                if (webSocket != null && webSocket.ReadyState == WebSocketState.Open)
                {
                    webSocket.Close();
                }
            }
            catch (Exception ex)
            {
                // 忽略关闭时的异常
            }
            
            // 清理HTTP客户端
            httpClient?.Dispose();
            
            base.OnClosed(e);
        }
    }

    // 数据模型
    public class BrowserInstance
    {
        public int InstanceId { get; set; }
        public string Url { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreateTime { get; set; }
    }

    public class BrowserStatus
    {
        public bool IsRunning { get; set; }
        public string Url { get; set; } = string.Empty;
        public bool HasInstance { get; set; }
        public int ClientsConnected { get; set; }
        public int ClosedInstancesCount { get; set; }
        public bool BackgroundTaskActive { get; set; }
        public int RunningInstancesCount { get; set; }
    }

    public class ClosedInstance
    {
        public string Url { get; set; } = string.Empty;
        public DateTime ClosedAt { get; set; }
        public DateTime LastAccessed { get; set; }
        public int RuntimeMinutes { get; set; }
        public int CookiesCount { get; set; }
    }

    public class DatabaseInstance
    {
        public int id { get; set; }
        public string url { get; set; } = string.Empty;
        public string name { get; set; } = string.Empty;
        public string description { get; set; } = string.Empty;
        public string group_name { get; set; } = string.Empty;
        public string tags { get; set; } = string.Empty;
        public DateTime created_at { get; set; }
        public DateTime? last_opened_at { get; set; }
        public DateTime? last_closed_at { get; set; }
        public int total_open_count { get; set; }
        public int total_runtime_minutes { get; set; }
        public bool is_active { get; set; }
        public int priority { get; set; }
        public int session_count { get; set; }
    }

    public class AllInstancesResponse
    {
        public bool Success { get; set; }
        public List<DatabaseInstance> Data { get; set; } = new List<DatabaseInstance>();
    }

    public class StatisticsData
    {
        public int total_instances { get; set; }
        public int active_instances { get; set; }
        public int total_sessions { get; set; }
        public int total_runtime { get; set; }
        public int total_groups { get; set; }
    }

    public class StatisticsResponse
    {
        public bool Success { get; set; }
        public StatisticsData Data { get; set; } = new StatisticsData();
    }

    public class ClosedInstanceData
    {
        public int TotalClosed { get; set; }
        public int CurrentIndex { get; set; }
        public List<ClosedInstance> Instances { get; set; } = new List<ClosedInstance>();
    }

    public class ClosedInstancesResponse
    {
        public bool Success { get; set; }
        public ClosedInstanceData Data { get; set; } = new ClosedInstanceData();
    }

    public class ApiResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class WebSocketMessage
    {
        public string Type { get; set; } = string.Empty;
        public BrowserStatus Data { get; set; } = new BrowserStatus();
        public string Timestamp { get; set; } = string.Empty;
    }

    // 转换器
    public class BoolToActiveConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (bool)value ? "活跃" : "非活跃";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (bool)value ? Brushes.Green : Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class RunningInstanceData
    {
        public int InstanceId { get; set; }
        public string Url { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
    }

    public class RunningInstancesResponse
    {
        public bool Success { get; set; }
        public List<RunningInstanceData> Data { get; set; } = new List<RunningInstanceData>();
    }
}
