# Cookie管理器 - Linux服务器部署指南

## 系统要求

支持的Linux发行版：
- Ubuntu 18.04+ / Debian 9+
- CentOS 7+ / RHEL 7+
- Amazon Linux 2
- 其他基于Debian/RedHat的发行版

## 依赖安装

### Ubuntu/Debian 系统

```bash
# 更新包管理器
sudo apt update

# 安装Node.js和npm
curl -fsSL https://deb.nodesource.com/setup_18.x | sudo -E bash -
sudo apt-get install -y nodejs

# 安装Chromium浏览器
sudo apt-get install -y chromium-browser

# 安装其他依赖
sudo apt-get install -y \
    fonts-liberation \
    libappindicator3-1 \
    libasound2 \
    libatk-bridge2.0-0 \
    libdrm2 \
    libgtk-3-0 \
    libnspr4 \
    libnss3 \
    libxcomposite1 \
    libxdamage1 \
    libxrandr2 \
    xdg-utils \
    libgbm1 \
    libxss1
```

### CentOS/RHEL 系统

```bash
# 安装Node.js和npm
curl -fsSL https://rpm.nodesource.com/setup_18.x | sudo bash -
sudo yum install -y nodejs

# 安装Chromium
sudo yum install -y chromium

# 安装其他依赖
sudo yum install -y \
    libX11 \
    libXcomposite \
    libXcursor \
    libXdamage \
    libXext \
    libXi \
    libXtst \
    cups-libs \
    libXScrnSaver \
    libXrandr \
    GConf2 \
    alsa-lib \
    atk \
    gtk3 \
    ipa-gothic-fonts \
    xorg-x11-fonts-100dpi \
    xorg-x11-fonts-75dpi \
    xorg-x11-utils \
    xorg-x11-fonts-cyrillic \
    xorg-x11-fonts-Type1 \
    xorg-x11-fonts-misc
```

## 项目部署

### 1. 上传项目文件

```bash
# 创建项目目录
sudo mkdir -p /opt/cookiemanager
sudo chown $USER:$USER /opt/cookiemanager

# 上传server目录到服务器
scp -r ./server/* user@your-server:/opt/cookiemanager/
```

### 2. 安装项目依赖

```bash
cd /opt/cookiemanager
npm install
```

### 3. 创建systemd服务

创建服务文件：
```bash
sudo nano /etc/systemd/system/cookiemanager.service
```

服务文件内容：
```ini
[Unit]
Description=Cookie Manager Server
After=network.target

[Service]
Type=simple
User=www-data
WorkingDirectory=/opt/cookiemanager
ExecStart=/usr/bin/node index.js
Restart=always
RestartSec=5
Environment=NODE_ENV=production
Environment=PORT=3001

# 安全设置
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ProtectHome=true
ReadWritePaths=/opt/cookiemanager

[Install]
WantedBy=multi-user.target
```

### 4. 启动服务

```bash
# 重新加载systemd配置
sudo systemctl daemon-reload

# 启用服务（开机自启）
sudo systemctl enable cookiemanager

# 启动服务
sudo systemctl start cookiemanager

# 检查服务状态
sudo systemctl status cookiemanager
```

## Chromium 路径配置

服务器会自动检测以下Chromium路径：
- `/usr/bin/chromium-browser` (Ubuntu/Debian)
- `/usr/bin/chromium` (通用)
- `/usr/bin/google-chrome` (如果安装了Chrome)
- `/usr/bin/google-chrome-stable`
- `/snap/bin/chromium` (Snap包管理器)

如果您的Chromium安装在其他位置，可以通过环境变量指定：
```bash
# 在服务文件中添加
Environment=CHROMIUM_PATH=/path/to/your/chromium
```

## 防火墙配置

### UFW (Ubuntu)
```bash
sudo ufw allow 3001/tcp
sudo ufw reload
```

### firewalld (CentOS/RHEL)
```bash
sudo firewall-cmd --permanent --add-port=3001/tcp
sudo firewall-cmd --reload
```

### iptables
```bash
sudo iptables -A INPUT -p tcp --dport 3001 -j ACCEPT
sudo iptables-save > /etc/iptables/rules.v4
```

## Nginx 反向代理配置（可选）

如果您想使用Nginx作为反向代理：

```nginx
server {
    listen 80;
    server_name your-domain.com;

    location / {
        proxy_pass http://localhost:3001;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection 'upgrade';
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_cache_bypass $http_upgrade;
        
        # WebSocket支持
        proxy_read_timeout 86400;
    }
}
```

## 性能优化

### 1. 调整系统限制

```bash
# 编辑limits.conf
sudo nano /etc/security/limits.conf

# 添加以下行
www-data soft nofile 65536
www-data hard nofile 65536
www-data soft nproc 32768
www-data hard nproc 32768
```

### 2. 内存优化

对于内存有限的服务器，可以调整Chromium参数：

```javascript
// 在getPuppeteerLaunchOptions函数中添加
args: [
    // ...existing args...
    '--max_old_space_size=4096',
    '--memory-pressure-off',
    '--max-heap-size=2048'
]
```

## 故障排除

### 1. 检查服务日志
```bash
sudo journalctl -u cookiemanager -f
```

### 2. 检查Chromium是否可用
```bash
chromium-browser --version
# 或
/usr/bin/chromium-browser --version
```

### 3. 测试连接
```bash
curl http://localhost:3001/api/browser/status
```

### 4. 常见问题

**Chromium启动失败**：
- 确保安装了所有必要的依赖包
- 检查Chromium路径是否正确
- 查看是否有权限问题

**内存不足**：
- 增加系统swap空间
- 调整Chromium启动参数
- 限制同时运行的实例数量

**权限错误**：
- 确保服务用户有权限访问项目目录
- 检查用户数据目录的权限设置

## 安全建议

1. **防火墙**：只开放必要的端口
2. **用户权限**：使用专用的非特权用户运行服务
3. **定期更新**：保持系统和依赖包的更新
4. **日志监控**：定期检查系统和应用日志
5. **备份**：定期备份用户数据和配置

## 监控和维护

### 1. 设置日志轮转

```bash
sudo nano /etc/logrotate.d/cookiemanager
```

```
/opt/cookiemanager/logs/*.log {
    daily
    missingok
    rotate 7
    compress
    delaycompress
    copytruncate
}
```

### 2. 磁盘空间监控

```bash
# 添加到crontab
0 */6 * * * /usr/bin/du -sh /opt/cookiemanager/user_data | logger -t cookiemanager-disk
```

这样就完成了Linux服务器上的Cookie管理器部署。服务器现在可以在各种Linux发行版上稳定运行，自动检测和使用系统的Chromium浏览器。
