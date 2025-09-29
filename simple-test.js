// 简单的连接测试
const http = require('http');

const BASE_URL = 'http://localhost:5272';

async function testConnection() {
    try {
        console.log('测试连接到:', BASE_URL);

        const options = {
            hostname: 'localhost',
            port: 5272,
            path: '/api/dead-letter-queue',
            method: 'GET'
        };

        const req = http.request(options, (res) => {
            console.log('状态码:', res.statusCode);
            console.log('响应头:', res.headers);

            let body = '';
            res.on('data', (chunk) => {
                body += chunk;
            });
            res.on('end', () => {
                console.log('响应体:', body);
            });
        });

        req.on('error', (error) => {
            console.error('请求错误:', error);
        });

        req.end();

    } catch (error) {
        console.error('测试失败:', error);
    }
}

testConnection();