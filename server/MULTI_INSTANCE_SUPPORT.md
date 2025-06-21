# 多实例支持功能说明

## 概述
系统现在支持同时创建和运行多个浏览器实例，即使是相同的URL也可以创建多个独立的实例。

## 主要改进

### 1. 数据库层面
- **移除URL唯一性限制**: 修改了`createOrUpdateInstance`方法，不再检查URL是否已存在
- **支持重复URL**: 每次创建请求都会生成新的实例记录

### 2. 服务器架构改进
- **多实例管理**: 使用`Map`数据结构存储多个浏览器实例
- **独立会话**: 每个实例拥有独立的浏览器、页面和用户数据目录
- **并行保活**: 每个实例都有独立的Cookie保活定时器

### 3. 新增功能

#### a) 多实例存储结构
```javascript
this.browsers = new Map(); // instanceId -> instanceInfo
// instanceInfo包含：
// - browser: Puppeteer浏览器实例
// - page: 页面对象  
// - url: 访问的URL
// - instanceId: 实例ID
// - sessionStartTime: 会话开始时间
// - timer: 保活定时器
```

#### b) 新增API接口
- `GET /api/browser/running-instances`: 获取所有运行中的实例列表

#### c) 独立Cookie保活
- 每个实例拥有独立的保活定时器
- 避免实例间相互干扰

### 4. 客户端更新
- **运行状态显示**: 显示运行中的实例数量
- **BrowserStatus扩展**: 添加`RunningInstancesCount`字段

## 技术实现

### 实例隔离机制
1. **用户数据目录**: 每个实例使用独立的`user_data/instance_${instanceId}`目录
2. **会话隔离**: Cookie、localStorage、sessionStorage完全独立
3. **进程隔离**: 每个实例运行在独立的浏览器进程中

### 兼容性保持
- 保留了原有的单实例相关字段和方法
- 向后兼容现有的API接口
- 客户端无需大幅改动

## 使用场景

### 1. 多账号管理
```
相同网站 + 不同账号 = 多个并行实例
例如：
- 实例1: https://twitter.com (账号A)
- 实例2: https://twitter.com (账号B)  
- 实例3: https://twitter.com (账号C)
```

### 2. AB测试
```
相同页面 + 不同测试条件 = 并行对比
例如：
- 实例1: https://example.com (测试版本A)
- 实例2: https://example.com (测试版本B)
```

### 3. 数据采集
```
相同网站 + 不同数据源 = 并行采集
例如：
- 实例1: https://news.com/category1
- 实例2: https://news.com/category2
```

## 资源管理

### 内存消耗
- 每个实例约消耗50-100MB内存
- 建议根据系统配置合理控制实例数量

### 磁盘空间
- 每个实例的用户数据目录约10-50MB
- 定期清理不需要的实例数据

### 网络连接
- 每个实例独立的网络连接
- 支持不同的代理设置（如需要）

## 限制和注意事项

1. **系统资源**: 过多实例会消耗大量内存和CPU
2. **网站限制**: 某些网站可能检测并限制多实例访问
3. **数据同步**: 不同实例间的数据不会自动同步

## 未来扩展

- 实例分组管理
- 批量操作支持
- 实例间数据共享机制
- 负载均衡和资源调度
