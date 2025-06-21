const sqlite3 = require('sqlite3').verbose();
const path = require('path');

class DatabaseManager {
    constructor() {
        this.dbPath = path.join(__dirname, 'browser_instances.db');
        this.db = null;
        this.init();
    }

    init() {
        this.db = new sqlite3.Database(this.dbPath, (err) => {
            if (err) {
                console.error('数据库连接失败:', err.message);
            } else {
                console.log('SQLite数据库连接成功');
                this.createTables();
            }
        });
    }

    createTables() {
        const createInstancesTable = `
            CREATE TABLE IF NOT EXISTS browser_instances (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                url TEXT NOT NULL,
                name TEXT,
                description TEXT,
                group_name TEXT,
                created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                last_opened_at DATETIME,
                last_closed_at DATETIME,
                total_open_count INTEGER DEFAULT 0,
                total_runtime_minutes INTEGER DEFAULT 0,
                is_active BOOLEAN DEFAULT 0,
                tags TEXT,
                priority INTEGER DEFAULT 1
            )
        `;

        const createSessionsTable = `
            CREATE TABLE IF NOT EXISTS browser_sessions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                instance_id INTEGER,
                opened_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                closed_at DATETIME,
                runtime_minutes INTEGER,
                cookies_count INTEGER,
                session_type TEXT DEFAULT 'manual',
                FOREIGN KEY (instance_id) REFERENCES browser_instances (id)
            )
        `;

        this.db.run(createInstancesTable, (err) => {
            if (err) {
                console.error('创建browser_instances表失败:', err.message);
            } else {
                console.log('browser_instances表创建成功');
            }
        });

        this.db.run(createSessionsTable, (err) => {
            if (err) {
                console.error('创建browser_sessions表失败:', err.message);
            } else {
                console.log('browser_sessions表创建成功');
            }
        });
    }

    // 创建浏览器实例（允许相同URL的多个实例）
    async createOrUpdateInstance(instanceData) {
        return new Promise((resolve, reject) => {
            const { url, name, description, groupName, tags } = instanceData;
            
            // 直接创建新实例，不检查URL是否重复
            const insertSql = `
                INSERT INTO browser_instances 
                (url, name, description, group_name, tags, last_opened_at, total_open_count, is_active)
                VALUES (?, ?, ?, ?, ?, CURRENT_TIMESTAMP, 1, 1)
            `;
            this.db.run(insertSql, [url, name, description, groupName, tags], function(err) {
                if (err) {
                    reject(err);
                } else {
                    resolve(this.lastID);
                }
            });
        });
    }

    // 记录实例关闭
    async closeInstance(url, runtimeMinutes, cookiesCount, sessionType = 'manual') {
        return new Promise((resolve, reject) => {
            // 更新实例状态
            const updateInstanceSql = `
                UPDATE browser_instances 
                SET last_closed_at = CURRENT_TIMESTAMP,
                    total_runtime_minutes = total_runtime_minutes + ?,
                    is_active = 0
                WHERE url = ?
            `;
            
            this.db.run(updateInstanceSql, [runtimeMinutes, url], (err) => {
                if (err) {
                    reject(err);
                    return;
                }

                // 获取实例ID并记录会话
                const getInstanceSql = 'SELECT id FROM browser_instances WHERE url = ?';
                this.db.get(getInstanceSql, [url], (err, row) => {
                    if (err) {
                        reject(err);
                        return;
                    }

                    if (row) {
                        // 记录会话结束
                        const insertSessionSql = `
                            INSERT INTO browser_sessions 
                            (instance_id, closed_at, runtime_minutes, cookies_count, session_type)
                            VALUES (?, CURRENT_TIMESTAMP, ?, ?, ?)
                        `;
                        this.db.run(insertSessionSql, [row.id, runtimeMinutes, cookiesCount, sessionType], (err) => {
                            if (err) {
                                reject(err);
                            } else {
                                resolve();
                            }
                        });
                    } else {
                        resolve();
                    }
                });
            });
        });
    }

    // 获取所有实例
    async getAllInstances() {
        return new Promise((resolve, reject) => {
            const sql = `
                SELECT 
                    *,
                    (SELECT COUNT(*) FROM browser_sessions WHERE instance_id = browser_instances.id) as session_count,
                    (SELECT MAX(closed_at) FROM browser_sessions WHERE instance_id = browser_instances.id) as last_session_end
                FROM browser_instances 
                ORDER BY last_closed_at ASC
            `;
            this.db.all(sql, [], (err, rows) => {
                if (err) {
                    reject(err);
                } else {
                    resolve(rows);
                }
            });
        });
    }

    // 获取按分组的实例
    async getInstancesByGroup() {
        return new Promise((resolve, reject) => {
            const sql = `
                SELECT 
                    group_name,
                    COUNT(*) as instance_count,
                    SUM(total_runtime_minutes) as total_runtime,
                    MAX(last_closed_at) as last_activity
                FROM browser_instances 
                WHERE group_name IS NOT NULL
                GROUP BY group_name
                ORDER BY last_activity DESC
            `;
            this.db.all(sql, [], (err, rows) => {
                if (err) {
                    reject(err);
                } else {
                    resolve(rows);
                }
            });
        });
    }

    // 获取需要保活的实例（按关闭时间排序）
    async getInstancesForKeepAlive(limit = 20) {
        return new Promise((resolve, reject) => {
            const sql = `
                SELECT * FROM browser_instances 
                WHERE last_closed_at IS NOT NULL 
                AND is_active = 0
                ORDER BY last_closed_at ASC
                LIMIT ?
            `;
            this.db.all(sql, [limit], (err, rows) => {
                if (err) {
                    reject(err);
                } else {
                    resolve(rows);
                }
            });
        });
    }

    // 更新实例信息
    async updateInstance(id, updateData) {
        return new Promise((resolve, reject) => {
            const { name, description, groupName, tags, priority } = updateData;
            const sql = `
                UPDATE browser_instances 
                SET name = COALESCE(?, name),
                    description = COALESCE(?, description),
                    group_name = COALESCE(?, group_name),
                    tags = COALESCE(?, tags),
                    priority = COALESCE(?, priority)
                WHERE id = ?
            `;
            this.db.run(sql, [name, description, groupName, tags, priority, id], function(err) {
                if (err) {
                    reject(err);
                } else {
                    resolve(this.changes);
                }
            });
        });
    }

    // 更新实例URL
    async updateInstanceUrl(instanceId, url) {
        return new Promise((resolve, reject) => {
            const sql = 'UPDATE browser_instances SET url = ?, last_opened_at = CURRENT_TIMESTAMP WHERE id = ?';
            this.db.run(sql, [url, instanceId], function(err) {
                if (err) {
                    reject(err);
                } else {
                    resolve(this.changes);
                }
            });
        });
    }

    // 删除实例
    async deleteInstance(id) {
        return new Promise((resolve, reject) => {
            // 先删除相关会话
            const deleteSessionsSql = 'DELETE FROM browser_sessions WHERE instance_id = ?';
            this.db.run(deleteSessionsSql, [id], (err) => {
                if (err) {
                    reject(err);
                    return;
                }

                // 再删除实例
                const deleteInstanceSql = 'DELETE FROM browser_instances WHERE id = ?';
                this.db.run(deleteInstanceSql, [id], function(err) {
                    if (err) {
                        reject(err);
                    } else {
                        resolve(this.changes);
                    }
                });
            });
        });
    }

    // 获取统计信息
    async getStatistics() {
        return new Promise((resolve, reject) => {
            const sql = `
                SELECT 
                    COUNT(*) as total_instances,
                    COUNT(CASE WHEN is_active = 1 THEN 1 END) as active_instances,
                    SUM(total_open_count) as total_sessions,
                    SUM(total_runtime_minutes) as total_runtime,
                    COUNT(DISTINCT group_name) as total_groups
                FROM browser_instances
            `;
            this.db.get(sql, [], (err, row) => {
                if (err) {
                    reject(err);
                } else {
                    resolve(row);
                }
            });
        });
    }

    // 根据ID获取实例
    async getInstanceById(instanceId) {
        return new Promise((resolve, reject) => {
            this.db.get(
                'SELECT * FROM browser_instances WHERE id = ?',
                [instanceId],
                (err, row) => {
                    if (err) {
                        reject(err);
                    } else {
                        resolve(row);
                    }
                }
            );
        });
    }
    
    // 更新实例的最后打开时间
    async updateInstanceLastOpened(instanceId) {
        return new Promise((resolve, reject) => {
            const now = new Date().toISOString();
            this.db.run(
                `UPDATE browser_instances 
                 SET last_opened_at = ?, total_open_count = total_open_count + 1, is_active = 1 
                 WHERE id = ?`,
                [now, instanceId],
                function(err) {
                    if (err) {
                        reject(err);
                    } else {
                        resolve(this.changes);
                    }
                }
            );
        });
   }

    // 获取所有活跃实例
    async getActiveInstances() {
        return new Promise((resolve, reject) => {
            this.db.all(
                'SELECT * FROM browser_instances WHERE is_active = 1',
                [],
                (err, rows) => {
                    if (err) {
                        reject(err);
                    } else {
                        resolve(rows || []);
                    }
                }
            );
        });
    }
    
    // 设置实例为非活跃状态
    async setInstanceInactive(instanceId) {
        return new Promise((resolve, reject) => {
            this.db.run(
                'UPDATE browser_instances SET is_active = 0 WHERE id = ?',
                [instanceId],
                function(err) {
                    if (err) {
                        reject(err);
                    } else {
                        resolve(this.changes);
                    }
                }
            );
        });
    }

    close() {
        if (this.db) {
            this.db.close((err) => {
                if (err) {
                    console.error('关闭数据库失败:', err.message);
                } else {
                    console.log('数据库连接已关闭');
                }
            });
        }
    }
}

module.exports = DatabaseManager;
