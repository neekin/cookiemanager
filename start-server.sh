#!/bin/bash

echo "Cookie管理器 - 启动服务端"
echo "========================="

# 检查Node.js是否安装
if ! command -v node &> /dev/null; then
    echo "错误: 未找到Node.js，请先安装Node.js"
    exit 1
fi

# 检查npm是否安装
if ! command -v npm &> /dev/null; then
    echo "错误: 未找到npm，请先安装npm"
    exit 1
fi

cd server

# 检查package.json是否存在
if [ ! -f "package.json" ]; then
    echo "错误: 未找到package.json文件"
    exit 1
fi

# 安装依赖
echo "正在安装依赖..."
npm install

if [ $? -eq 0 ]; then
    echo "依赖安装完成"
    echo "启动服务器..."
    npm start
else
    echo "依赖安装失败"
    exit 1
fi
