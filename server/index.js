const express = require('express');
const cors = require('cors');
const puppeteer = require('puppeteer');
const WebSocket = require('ws');
const path = require('path');
const fs = require('fs');
const DatabaseManager = require('./database');

class BrowserManager {
    constructor() {
        this.browsers = new Map(); // 存储多个浏览器实例
        this.wsClients = new Set();
        this.backgroundTaskTimer = null;
        this.currentInstanceIndex = 0;
        this.instanceActiveTime = 3 * 60 * 1000; // 每个实例活跃3分钟
        this.taskInterval = 10 * 60 * 1000; // 每10分钟检查一次
        
        // 保留兼容性字段
        this.browser = null;
        this.page = null;
        this.isRunning = false;
        this.timer = null;
        this.currentUrl = '';
        this.currentInstanceId = null;
        this.sessionStartTime = null;
        this.closedInstances = []; // 保留内存缓存用于兼容
        
        // 初始化数据库
        this.db = new DatabaseManager();
        
        // 清理未运行的活跃实例状态
        this.cleanupStaleActiveInstances();
        
        // 启动后台任务
        this.startBackgroundTask();
    }

    async createBrowserInstance(url, instanceData = {}) {
        try {
            // 记录到数据库
            const instanceId = await this.db.createOrUpdateInstance({
                url: url,
                name: instanceData.name,
                description: instanceData.description,
                groupName: instanceData.groupName,
                tags: instanceData.tags
            });

            // 创建用户数据目录
            const userDataDir = this.createUserDataDir(instanceId);

            const browser = await puppeteer.launch(this.getPuppeteerLaunchOptions(userDataDir));

            const page = await browser.newPage();
            const sessionStartTime = new Date();
            await page.goto(url);

            // 存储浏览器实例信息
            const instanceInfo = {
                browser,
                page,
                url,
                instanceId,
                sessionStartTime,
                timer: null
            };

            this.browsers.set(instanceId, instanceInfo);

            // 更新兼容性字段（指向最新创建的实例）
            this.browser = browser;
            this.page = page;
            this.isRunning = true;
            this.currentUrl = url;
            this.currentInstanceId = instanceId;
            this.sessionStartTime = sessionStartTime;

            // 设置定时任务保持Cookie活跃
            this.startKeepAliveTaskForInstance(instanceId);

            console.log(`浏览器实例创建成功: ${url} (ID: ${instanceId})`);

            return {
                success: true,
                message: '浏览器实例创建成功',
                url: url,
                instanceId: instanceId
            };
        } catch (error) {
            console.error('创建浏览器实例失败:', error);
            return {
                success: false,
                message: '创建浏览器实例失败: ' + error.message
            };
        }
    }

    async closeBrowser(sessionType = 'manual') {
        try {
            if (this.timer) {
                clearInterval(this.timer);
                this.timer = null;
            }

            if (this.browser) {
                // 计算运行时间
                const runtimeMinutes = this.sessionStartTime ? 
                    Math.round((new Date() - this.sessionStartTime) / (1000 * 60)) : 0;

                // 获取Cookie数量
                let cookiesCount = 0;
                try {
                    if (this.page) {
                        const cookies = await this.page.cookies();
                        cookiesCount = cookies.length;
                    }
                } catch (error) {
                    console.warn('获取Cookie失败:', error.message);
                }

                // 记录到数据库
                if (this.currentUrl) {
                    await this.db.closeInstance(this.currentUrl, runtimeMinutes, cookiesCount, sessionType);
                    
                    // 更新实例为非活跃状态
                    if (this.currentInstanceId) {
                        await this.db.setInstanceInactive(this.currentInstanceId);
                    }
                    
                    // 保留内存缓存用于兼容
                    this.closedInstances.push({
                        url: this.currentUrl,
                        closedAt: new Date(),
                        lastAccessed: new Date(),
                        runtimeMinutes: runtimeMinutes,
                        cookiesCount: cookiesCount
                    });
                    
                    // 只保留最近20个关闭的实例
                    if (this.closedInstances.length > 20) {
                        this.closedInstances = this.closedInstances.slice(-20);
                    }
                    
                    console.log(`浏览器实例已关闭: ${this.currentUrl}, 运行时间: ${runtimeMinutes}分钟, Cookie数量: ${cookiesCount}`);
                }

                await this.browser.close();
                this.browser = null;
                this.page = null;
                this.isRunning = false;
                this.currentUrl = '';
                this.currentInstanceId = null;
                this.sessionStartTime = null;
            }
        } catch (error) {
            console.error('关闭浏览器失败:', error);
        }
    }

    startKeepAliveTask() {
        // 每5分钟执行一次保活任务
        this.timer = setInterval(async () => {
            try {
                if (this.page && this.isRunning) {
                    console.log('执行Cookie保活任务');
                    // 刷新页面保持Cookie活跃
                    await this.page.reload();
                    
                    // 获取当前Cookie
                    const cookies = await this.page.cookies();
                    console.log('当前Cookie数量:', cookies.length);
                    
                    // 通知客户端
                    this.broadcastToClients({
                        type: 'keepAlive',
                        timestamp: new Date().toISOString(),
                        cookieCount: cookies.length
                    });
                }
            } catch (error) {
                console.error('保活任务执行失败:', error);
            }
        }, 5 * 60 * 1000); // 5分钟
    }

    startKeepAliveTaskForInstance(instanceId) {
        const instanceInfo = this.browsers.get(instanceId);
        if (!instanceInfo) return;

        // 每5分钟执行一次保活任务
        instanceInfo.timer = setInterval(async () => {
            try {
                if (instanceInfo.page && this.browsers.has(instanceId)) {
                    console.log(`执行Cookie保活任务 (实例ID: ${instanceId})`);
                    // 刷新页面保持Cookie活跃
                    await instanceInfo.page.reload();
                    
                    // 获取当前Cookie
                    const cookies = await instanceInfo.page.cookies();
                    console.log(`实例${instanceId}当前Cookie数量:`, cookies.length);
                    
                    // 通知客户端
                    this.broadcastToClients({
                        type: 'keepAlive',
                        timestamp: new Date().toISOString(),
                        instanceId: instanceId,
                        cookieCount: cookies.length
                    });
                }
            } catch (error) {
                console.error(`实例${instanceId}保活任务执行失败:`, error);
            }
        }, 5 * 60 * 1000); // 5分钟
    }

    // 启动后台任务 - 在无客户端连接时运行
    startBackgroundTask() {
        this.backgroundTaskTimer = setInterval(async () => {
            try {
                // 只在无客户端连接且无当前运行实例时执行
                if (this.wsClients.size === 0 && !this.isRunning && this.closedInstances.length > 0) {
                    console.log('开始执行后台Cookie保活任务...');
                    await this.executeBackgroundTask();
                }
            } catch (error) {
                console.error('后台任务执行失败:', error);
            }
        }, this.taskInterval);
    }

    async executeBackgroundTask() {
        try {
            // 从数据库获取需要保活的实例
            const instances = await this.db.getInstancesForKeepAlive(20);
            
            if (instances.length === 0) {
                console.log('没有已关闭的实例需要处理');
                return;
            }

            // 获取当前要处理的实例
            const instance = instances[this.currentInstanceIndex % instances.length];
            
            console.log(`后台任务: 重新启动实例 ${instance.url} (关闭时间: ${instance.last_closed_at})`);
            
            // 创建浏览器实例
            this.currentInstanceId = instance.id;
            
            // 为此实例创建独立的用户数据目录
            const userDataDir = this.createUserDataDir(this.currentInstanceId);
            
            this.browser = await puppeteer.launch(this.getPuppeteerLaunchOptions(userDataDir));

            this.page = await this.browser.newPage();
            this.currentUrl = instance.url;
            this.sessionStartTime = new Date();
            await this.page.goto(instance.url);
            this.isRunning = true;

            console.log(`后台任务: 实例 ${instance.url} 已启动，将运行 ${this.instanceActiveTime / 1000} 秒`);

            // 让实例运行指定时间后自动关闭
            setTimeout(async () => {
                try {
                    if (this.wsClients.size === 0) { // 确保仍然没有客户端连接
                        console.log(`后台任务: 关闭实例 ${instance.url}`);
                        await this.closeBrowser('background');
                        
                        // 移动到下一个实例
                        this.currentInstanceIndex++;
                        console.log(`后台任务: 准备处理下一个实例 (索引: ${this.currentInstanceIndex})`);
                    }
                } catch (error) {
                    console.error('后台任务关闭浏览器失败:', error);
                }
            }, this.instanceActiveTime);

        } catch (error) {
            console.error('后台任务执行失败:', error);
        }
    }

    // 获取已关闭实例的统计信息
    getClosedInstancesStats() {
        return {
            totalClosed: this.closedInstances.length,
            currentIndex: this.currentInstanceIndex,
            instances: this.closedInstances.map(inst => ({
                url: inst.url,
                closedAt: inst.closedAt,
                lastAccessed: inst.lastAccessed
            }))
        };
    }

    getBrowserStatus() {
        return {
            isRunning: this.browsers.size > 0,
            url: this.currentUrl,
            hasInstance: this.browsers.size > 0,
            clientsConnected: this.wsClients.size,
            closedInstancesCount: this.closedInstances.length,
            backgroundTaskActive: this.backgroundTaskTimer !== null,
            runningInstancesCount: this.browsers.size
        };
    }

    async getCookies() {
        try {
            if (this.page) {
                const cookies = await this.page.cookies();
                return cookies;
            }
            return [];
        } catch (error) {
            console.error('获取Cookie失败:', error);
            return [];
        }
    }

    // 创建用户数据目录
    createUserDataDir(instanceId) {
        const userDataDir = path.join(__dirname, 'user_data', `instance_${instanceId}`);
        
        // 确保目录存在
        if (!fs.existsSync(userDataDir)) {
            fs.mkdirSync(userDataDir, { recursive: true });
        }
        
        return userDataDir;
    }

    // 清理用户数据目录（可选，用于完全删除实例数据）
    cleanupUserDataDir(instanceId) {
        const userDataDir = path.join(__dirname, 'user_data', `instance_${instanceId}`);
        
        try {
            if (fs.existsSync(userDataDir)) {
                fs.rmSync(userDataDir, { recursive: true, force: true });
                console.log(`已清理用户数据目录: ${userDataDir}`);
                return true;
            }
        } catch (error) {
            console.error(`清理用户数据目录失败: ${error.message}`);
            return false;
        }
        
        return false;
   }

    // WebSocket相关方法
    addClient(ws) {
        this.wsClients.add(ws);
        ws.on('close', () => {
            this.wsClients.delete(ws);
        });
    }

    broadcastToClients(message) {
        this.wsClients.forEach(client => {
            if (client.readyState === WebSocket.OPEN) {
                client.send(JSON.stringify(message));
            }
        });
    }

    // 获取所有运行中的实例
    getRunningInstances() {
        const instances = [];
        for (const [instanceId, instanceInfo] of this.browsers) {
            instances.push({
                instanceId: instanceId,
                url: instanceInfo.url,
                startTime: instanceInfo.sessionStartTime,
                status: '运行中'
            });
        }
        return instances;
    }

    // 获取实例内容
    async getInstanceContent(instanceId) {
        const instanceInfo = this.browsers.get(instanceId);
        if (!instanceInfo) {
            return null;
        }

        try {
            const url = await instanceInfo.page.url();
            const title = await instanceInfo.page.title();
            const html = await instanceInfo.page.content();
            
            return {
                url: url,
                title: title,
                html: html,
                instanceId: instanceId,
                isActive: true
            };
        } catch (error) {
            console.error(`获取实例${instanceId}内容失败:`, error);
            return null;
        }
    }

    // 远程导航
    async navigateInstance(instanceId, url) {
        const instanceInfo = this.browsers.get(instanceId);
        if (!instanceInfo) {
            return { success: false, message: '实例不存在' };
        }

        try {
            await instanceInfo.page.goto(url);
            instanceInfo.url = url;
            
            // 更新数据库
            await this.db.updateInstanceUrl(instanceId, url);
            
            return { success: true, message: '导航成功' };
        } catch (error) {
            console.error(`实例${instanceId}导航失败:`, error);
            return { success: false, message: error.message };
        }
    }

    // 刷新实例
    async refreshInstance(instanceId) {
        const instanceInfo = this.browsers.get(instanceId);
        if (!instanceInfo) {
            return { success: false, message: '实例不存在' };
        }

        try {
            await instanceInfo.page.reload();
            return { success: true, message: '刷新成功' };
        } catch (error) {
            console.error(`实例${instanceId}刷新失败:`, error);
            return { success: false, message: error.message };
        }
    }

    // 获取实例截图
    async getInstanceScreenshot(instanceId) {
        const instanceInfo = this.browsers.get(instanceId);
        if (!instanceInfo) {
            return null;
        }

        try {
            const screenshot = await instanceInfo.page.screenshot({
                type: 'png',
                fullPage: true
            });
            return screenshot;
        } catch (error) {
            console.error(`实例${instanceId}截图失败:`, error);
            return null;
        }
    }

    // 重新启动指定实例
    async restartInstance(instanceId, url, instanceData = {}) {
        try {
            // 确保实例没有在运行
            if (this.browsers.has(instanceId)) {
                return {
                    success: true,
                    message: '实例已在运行中'
                };
            }
            
            // 创建用户数据目录
            const userDataDir = this.createUserDataDir(instanceId);

            const browser = await puppeteer.launch(this.getPuppeteerLaunchOptions(userDataDir));

            const page = await browser.newPage();
            const sessionStartTime = new Date();
            await page.goto(url);

            // 存储浏览器实例信息
            const instanceInfo = {
                browser,
                page,
                url,
                instanceId,
                sessionStartTime,
                timer: null
            };

            this.browsers.set(instanceId, instanceInfo);

            // 更新兼容性字段（指向最新启动的实例）
            this.browser = browser;
            this.page = page;
            this.isRunning = true;
            this.currentUrl = url;
            this.currentInstanceId = instanceId;
            this.sessionStartTime = sessionStartTime;

            // 设置定时任务保持Cookie活跃
            this.startKeepAliveTaskForInstance(instanceId);
            
            // 更新数据库中的最后打开时间
            await this.db.updateInstanceLastOpened(instanceId);

            console.log(`浏览器实例重新启动成功: ${url} (ID: ${instanceId})`);

            return {
                success: true,
                message: '实例重新启动成功',
                url: url,
                instanceId: instanceId
            };
        } catch (error) {
            console.error('重新启动浏览器实例失败:', error);
            return {
                success: false,
                message: '重新启动浏览器实例失败: ' + error.message
            };
        }
    }

    // 清理未运行的活跃实例状态
    async cleanupStaleActiveInstances() {
        try {
            // 获取所有标记为活跃的实例
            const activeInstances = await this.db.getActiveInstances();
            
            for (const instance of activeInstances) {
                // 检查实例是否真的在运行
                if (!this.browsers.has(instance.id)) {
                    // 实例实际上没有运行，更新状态
                    await this.db.setInstanceInactive(instance.id);
                    console.log(`清理未运行的活跃实例: ${instance.id} - ${instance.url}`);
                }
            }
        } catch (error) {
            console.error('清理活跃实例状态失败:', error);
        }
    }

    // Puppeteer启动配置 - 兼容Linux/Debian服务器
    getPuppeteerLaunchOptions(userDataDir) {
        const options = {
            headless: 'new',
            defaultViewport: null,
            userDataDir: userDataDir,
            args: [
                '--start-maximized',
                `--user-data-dir=${userDataDir}`,
                '--no-first-run',
                '--disable-default-apps',
                '--disable-dev-shm-usage',
                '--disable-setuid-sandbox',
                '--no-sandbox',
                '--disable-background-timer-throttling',
                '--disable-backgrounding-occluded-windows',
                '--disable-renderer-backgrounding'
            ]
        };

        // 在Linux系统上尝试使用系统安装的Chromium
        if (process.platform === 'linux') {
            // 尝试多个可能的Chromium路径
            const possiblePaths = [
                '/usr/bin/chromium-browser',
                '/usr/bin/chromium',
                '/usr/bin/google-chrome',
                '/usr/bin/google-chrome-stable',
                '/snap/bin/chromium'
            ];

            for (const path of possiblePaths) {
                try {
                    const fs = require('fs');
                    if (fs.existsSync(path)) {
                        options.executablePath = path;
                        console.log(`使用系统Chromium: ${path}`);
                        break;
                    }
                } catch (error) {
                    // 继续尝试下一个路径
                }
            }

            // 如果没有找到系统Chromium，让Puppeteer使用默认配置
            if (!options.executablePath) {
                console.log('未找到系统Chromium，使用Puppeteer默认配置');
            }
        }

        return options;
    }
}

const app = express();
const browserManager = new BrowserManager();

app.use(cors());
app.use(express.json());

// API路由
app.post('/api/browser/create', async (req, res) => {
    const { url, name, description, groupName, tags } = req.body;
    
    if (!url) {
        return res.status(400).json({
            success: false,
            message: '请提供URL'
        });
    }

    const result = await browserManager.createBrowserInstance(url, {
        name, description, groupName, tags
    });
    res.json(result);
});

app.get('/api/browser/status', (req, res) => {
    const status = browserManager.getBrowserStatus();
    res.json(status);
});

app.post('/api/browser/close', async (req, res) => {
    await browserManager.closeBrowser();
    res.json({
        success: true,
        message: '浏览器实例已关闭'
    });
});

app.get('/api/browser/cookies', async (req, res) => {
    const cookies = await browserManager.getCookies();
    res.json({
        success: true,
        cookies: cookies
    });
});

app.get('/api/browser/closed-instances', (req, res) => {
    const stats = browserManager.getClosedInstancesStats();
    res.json({
        success: true,
        data: stats
    });
});

// 新增API接口
app.get('/api/instances', async (req, res) => {
    try {
        const instances = await browserManager.db.getAllInstances();
        res.json({
            Success: true,
            Data: instances
        });
    } catch (error) {
        res.status(500).json({
            Success: false,
            Message: error.message
        });
    }
});

app.get('/api/instances/groups', async (req, res) => {
    try {
        const groups = await browserManager.db.getInstancesByGroup();
        res.json({
            success: true,
            data: groups
        });
    } catch (error) {
        res.status(500).json({
            success: false,
            message: error.message
        });
    }
});

app.put('/api/instances/:id', async (req, res) => {
    try {
        const { id } = req.params;
        const { name, description, groupName, tags, priority } = req.body;
        
        const changes = await browserManager.db.updateInstance(id, {
            name, description, groupName, tags, priority
        });
        
        res.json({
            success: true,
            message: '实例更新成功',
            changes: changes
        });
    } catch (error) {
        res.status(500).json({
            success: false,
            message: error.message
        });
    }
});

app.delete('/api/instances/:id', async (req, res) => {
    try {
        const { id } = req.params;
        const changes = await browserManager.db.deleteInstance(id);
        
        res.json({
            success: true,
            message: '实例删除成功',
            changes: changes
        });
    } catch (error) {
        res.status(500).json({
            success: false,
            message: error.message
        });
    }
});

app.get('/api/statistics', async (req, res) => {
    try {
        const stats = await browserManager.db.getStatistics();
        res.json({
            success: true,
            data: stats
        });
    } catch (error) {
        res.status(500).json({
            success: false,
            message: error.message
        });
    }
});

app.get('/api/browser/running-instances', (req, res) => {
    const instances = browserManager.getRunningInstances();
    res.json({
        Success: true,
        Data: instances
    });
});

// 远程控制API
app.get('/api/browser/instance/:id/content', async (req, res) => {
    const instanceId = parseInt(req.params.id);
    
    try {
        const content = await browserManager.getInstanceContent(instanceId);
        
        if (content) {
            res.json({
                Success: true,
                Data: content
            });
        } else {
            res.status(404).json({
                Success: false,
                Message: '实例不存在或无法访问'
            });
        }
    } catch (error) {
        res.status(500).json({
            Success: false,
            Message: error.message
        });
    }
});

app.post('/api/browser/instance/:id/navigate', async (req, res) => {
    const instanceId = parseInt(req.params.id);
    const { url } = req.body;
    
    if (!url) {
        return res.status(400).json({
            Success: false,
            Message: '请提供URL'
        });
    }

    try {
        const result = await browserManager.navigateInstance(instanceId, url);
        res.json({
            Success: result.success,
            Message: result.message
        });
    } catch (error) {
        res.status(500).json({
            Success: false,
            Message: error.message
        });
    }
});

app.post('/api/browser/instance/:id/refresh', async (req, res) => {
    const instanceId = parseInt(req.params.id);
    
    try {
        const result = await browserManager.refreshInstance(instanceId);
        res.json({
            Success: result.success,
            Message: result.message
        });
    } catch (error) {
        res.status(500).json({
            Success: false,
            Message: error.message
        });
    }
});

app.get('/api/browser/instance/:id/screenshot', async (req, res) => {
    const instanceId = parseInt(req.params.id);
    
    try {
        const screenshot = await browserManager.getInstanceScreenshot(instanceId);
        
        if (screenshot) {
            res.setHeader('Content-Type', 'image/png');
            res.send(screenshot);
        } else {
            res.status(404).json({
                Success: false,
                Message: '无法获取截图'
            });
        }
    } catch (error) {
        res.status(500).json({
            Success: false,
            Message: error.message
        });
    }
});

// 重新启动指定实例的API
app.post('/api/browser/instance/:id/restart', async (req, res) => {
    const instanceId = parseInt(req.params.id);
    
    try {
        // 检查实例是否已经在运行
        const existingInstance = browserManager.browsers.get(instanceId);
        if (existingInstance) {
            return res.json({
                Success: true,
                Message: '实例已在运行中'
            });
        }
        
        // 从数据库获取实例信息
        const instanceData = await browserManager.db.getInstanceById(instanceId);
        if (!instanceData) {
            return res.status(404).json({
                Success: false,
                Message: '实例不存在'
            });
        }
        
        // 重新启动实例
        const result = await browserManager.restartInstance(instanceId, instanceData.url, {
            name: instanceData.name,
            description: instanceData.description,
            groupName: instanceData.group_name,
            tags: instanceData.tags
        });
        
        res.json({
            Success: result.success,
            Message: result.message
        });
    } catch (error) {
        res.status(500).json({
            Success: false,
            Message: error.message
        });
    }
});

// 启动HTTP服务器
const PORT = process.env.PORT || 3000;
const server = app.listen(PORT, () => {
    console.log(`服务器运行在端口 ${PORT}`);
});

// 创建WebSocket服务器
const wss = new WebSocket.Server({ server });

wss.on('connection', (ws) => {
    console.log('客户端连接到WebSocket');
    browserManager.addClient(ws);
    
    // 发送当前状态
    ws.send(JSON.stringify({
        type: 'status',
        data: browserManager.getBrowserStatus()
    }));
});

// 优雅关闭
process.on('SIGINT', async () => {
    console.log('正在关闭服务器...');
    
    // 清理定时器
    if (browserManager.backgroundTaskTimer) {
        clearInterval(browserManager.backgroundTaskTimer);
        browserManager.backgroundTaskTimer = null;
    }
    
    // 关闭浏览器
    await browserManager.closeBrowser();
    
    // 关闭数据库
    browserManager.db.close();
    
    process.exit(0);
});

module.exports = app;
