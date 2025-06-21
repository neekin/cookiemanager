# Cookie和Session隔离功能

## 概述
服务端现在为每个浏览器实例创建独立的用户数据目录，确保不同实例之间的Cookie和Session完全隔离。

## 技术实现

### 1. 用户数据目录结构
```
server/
├── user_data/
│   ├── instance_1/          # 实例1的数据目录
│   │   ├── Default/
│   │   └── ...
│   ├── instance_2/          # 实例2的数据目录
│   │   ├── Default/
│   │   └── ...
│   └── ...
```

### 2. 主要修改

#### a) 添加了用户数据目录管理方法
- `createUserDataDir(instanceId)`: 为指定实例创建独立的用户数据目录
- `cleanupUserDataDir(instanceId)`: 清理指定实例的用户数据目录

#### b) 修改Puppeteer启动配置
```javascript
this.browser = await puppeteer.launch({
    headless: 'new',
    defaultViewport: null,
    userDataDir: userDataDir,           // 指定用户数据目录
    args: [
        '--start-maximized',
        `--user-data-dir=${userDataDir}`, // 确保Chrome使用指定目录
        '--no-first-run',                // 跳过首次运行设置
        '--disable-default-apps'         // 禁用默认应用
    ]
});
```

### 3. 隔离效果

#### Cookie隔离
- 每个实例的Cookie独立存储在各自的用户数据目录中
- 实例A的Cookie不会影响实例B
- 支持相同域名的不同账号同时登录

#### Session隔离  
- 每个实例维护独立的会话状态
- 登录状态、表单数据、浏览历史等完全分离
- 实例重启后会话状态可以恢复

### 4. 优势

1. **完全隔离**: 不同实例之间没有数据串扰
2. **会话持久化**: 实例关闭后再次打开可恢复之前的登录状态
3. **多账号支持**: 可以同时管理同一网站的多个账号
4. **数据安全**: 每个实例的敏感数据独立存储

### 5. 使用场景

- **多账号管理**: 同时管理多个社交媒体账号
- **AB测试**: 使用不同账号测试网站功能
- **数据采集**: 独立的会话环境进行数据采集
- **Cookie保活**: 为不同账号保持登录状态

### 6. 注意事项

1. 用户数据目录会占用磁盘空间，建议定期清理不需要的实例数据
2. 实例数量增多时，系统资源占用会相应增加
3. 首次启动实例时可能需要重新登录（如果之前没有保存的会话数据）

## 配置文件

`.gitignore` 文件已更新，用户数据目录不会被提交到版本控制系统：
```
user_data/
```
