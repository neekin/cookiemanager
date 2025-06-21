# Cookie管理器

基于需求文档实现的Cookie管理系统，包含服务端和客户端两部分。

## 项目结构

```
cookiemanager/
├── server/                 # Node.js服务端
│   ├── package.json
│   ├── index.js
│   ├── database.js         # SQLite数据库管理
│   └── browser_instances.db # SQLite数据库文件
├── client/                 # C# WPF客户端
│   ├── CookieManager.csproj
│   ├── App.xaml
│   ├── App.xaml.cs
│   ├── MainWindow.xaml
│   └── MainWindow.xaml.cs
├── start-server.sh         # 服务端启动脚本
└── README.md
```

## 功能实现

### 服务端功能 ✅
1. **浏览器实例管理** - 确保只运行一个浏览器实例
2. **智能定时任务** - 在无客户端连接时，按关闭时间顺序重启已关闭的浏览器实例
3. **实例历史管理** - 记录已关闭实例的历史，支持按时间顺序循环重启
4. **Cookie保活策略** - 每个重启的实例运行3分钟保持Cookie活跃
5. **API接口** - 提供创建、关闭、状态查询等接口
6. **WebSocket** - 实时通知客户端状态变化

### 客户端功能 ✅
1. **创建浏览器实例** - 通过API创建浏览器并打开指定URL
2. **实时状态显示** - 通过WebSocket接收服务端状态更新
3. **实例列表展示** - 显示当前浏览器实例和已关闭实例历史
4. **双击重开** - 双击列表项重新打开浏览器实例
5. **后台任务监控** - 实时显示后台任务状态和处理进度

## 运行说明

### 启动服务端

```bash
cd server
npm install
npm start
```

服务端将运行在 `http://localhost:3001`

### 启动客户端

```bash
cd client
dotnet restore
dotnet run
```

或者在Visual Studio中打开项目并运行。

## API接口

- `POST /api/browser/create` - 创建浏览器实例
- `GET /api/browser/status` - 获取浏览器状态
- `POST /api/browser/close` - 关闭浏览器实例
- `GET /api/browser/cookies` - 获取当前Cookie
- `GET /api/browser/closed-instances` - 获取已关闭实例统计信息

## WebSocket事件

- `status` - 浏览器状态更新
- `keepAlive` - Cookie保活任务执行通知

## 技术栈

### 服务端
- Node.js
- Express.js
- Puppeteer (浏览器自动化)
- WebSocket

### 客户端
- C# WPF
- .NET 6.0
- WebView2
- WebSocketSharp

## 特性

1. **单实例保证** - 服务端确保同时只运行一个浏览器实例
2. **智能Cookie保活** - 后台任务在无客户端连接时自动循环重启已关闭实例
3. **时序管理** - 按关闭时间顺序处理实例，确保所有Cookie都能得到保活
4. **实时通信** - WebSocket实现客户端与服务端实时通信
5. **现代UI** - WPF客户端提供友好的用户界面，显示详细的任务状态
6. **跨平台服务端** - Node.js服务端可在多平台运行
7. **智能暂停** - 客户端连接时后台任务自动暂停，避免冲突
