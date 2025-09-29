const http = require('http');

const options = {
    hostname: 'localhost',
    port: 5272,
    path: '/api/dead-letter-queue',
    method: 'GET',
    timeout: 5000
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
    console.error('请求错误:', error.message);
});

req.on('timeout', () => {
    console.error('请求超时');
    req.destroy();
});

req.end();