const express = require('express');
const app = express();

app.use(express.json());

app.get('/api/test', (req, res) => {
    res.json({
        Success: true,
        Message: "测试成功",
        Data: [
            { id: 1, name: "测试实例1", url: "https://example.com" }
        ]
    });
});

const PORT = 3001;
app.listen(PORT, () => {
    console.log(`测试服务器运行在端口 ${PORT}`);
});
