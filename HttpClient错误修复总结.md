# Cookie管理器 - HttpClient 错误修复总结

## 问题描述

用户修改连接服务器地址后出现以下错误：
```
应用程序发生未处理的异常: This instance has already started one or more requests. Properties can only be modified before sending the first request.
```

## 问题原因

在 MainWindow.xaml.cs 中，当用户修改服务器配置后，代码尝试修改已经使用过的 HttpClient 实例的 Timeout 属性：

```csharp
// 错误的做法
httpClient.Timeout = TimeSpan.FromMilliseconds(appConfig.ConnectionTimeout);
```

HttpClient 一旦发送了第一个请求，就不能再修改其属性（如 Timeout）。

## 解决方案

### 1. 修改字段声明
将 `httpClient` 从只读字段改为可写字段：

```csharp
// 修改前
private readonly HttpClient httpClient;

// 修改后
private HttpClient httpClient;
```

### 2. 重新创建 HttpClient 实例
在配置更改时，释放旧的 HttpClient 实例并创建新的：

```csharp
// 重新加载配置
appConfig = configWindow.Config;

// 重新创建HttpClient以避免属性修改错误
httpClient?.Dispose();
httpClient = new HttpClient();
httpClient.Timeout = TimeSpan.FromMilliseconds(appConfig.ConnectionTimeout);
```

### 3. 确保资源正确释放
在窗口关闭时释放 HttpClient：

```csharp
protected override void OnClosed(EventArgs e)
{
    // ...existing code...
    httpClient?.Dispose();
    base.OnClosed(e);
}
```

## 已修复的文件

- `MainWindow.xaml.cs`: 修改了 httpClient 字段声明和配置更新逻辑

## 测试建议

1. **配置修改测试**：
   - 启动应用程序
   - 通过菜单进入服务器配置
   - 修改服务器地址
   - 保存配置
   - 验证不再出现 HttpClient 错误

2. **功能验证**：
   - 确保配置保存后能正常连接新服务器
   - 验证远程实例功能正常工作
   - 检查WebSocket连接正常

3. **资源管理验证**：
   - 多次修改配置，确保没有内存泄漏
   - 关闭应用程序，确保资源正确释放

## 相关的最佳实践

### 1. HttpClient 使用建议
- HttpClient 实例应该重用，但需要修改配置时应该重新创建
- 始终在适当的时候释放 HttpClient 实例
- 避免在 HttpClient 使用后修改其属性

### 2. 配置更新模式
```csharp
public void UpdateConfiguration(AppConfig newConfig)
{
    // 释放旧资源
    httpClient?.Dispose();
    webSocket?.Close();
    
    // 更新配置
    this.config = newConfig;
    
    // 创建新资源
    httpClient = new HttpClient();
    httpClient.Timeout = TimeSpan.FromMilliseconds(config.ConnectionTimeout);
    
    // 重新初始化连接
    InitializeConnections();
}
```

### 3. 错误处理
```csharp
try
{
    // HttpClient 操作
}
catch (HttpRequestException ex)
{
    // 网络错误处理
}
catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
{
    // 超时错误处理
}
catch (Exception ex)
{
    // 其他错误处理
}
```

## 验证清单

- [x] HttpClient 字段改为可写
- [x] 配置更新时重新创建 HttpClient
- [x] 确保 HttpClient 正确释放
- [x] 编译通过，无警告
- [x] 测试配置修改功能

修复完成后，用户现在可以正常修改服务器配置而不会遇到 HttpClient 属性修改错误。
