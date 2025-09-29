// DLQ观察测试
const http = require('http');

const BASE_URL = 'http://localhost:5272';

async function makeRequest(path, method = 'GET', data = null) {
    return new Promise((resolve, reject) => {
        const options = {
            hostname: 'localhost',
            port: 5272,
            path: path,
            method: method,
            headers: {
                'Content-Type': 'application/json'
            }
        };

        const req = http.request(options, (res) => {
            let body = '';
            res.on('data', (chunk) => {
                body += chunk;
            });
            res.on('end', () => {
                try {
                    const result = JSON.parse(body);
                    resolve({
                        statusCode: res.statusCode,
                        data: result
                    });
                } catch (error) {
                    reject(error);
                }
            });
        });

        req.on('error', (error) => {
            reject(error);
        });

        if (data) {
            req.write(JSON.stringify(data));
        }

        req.end();
    });
}

async function observeDLQProcess() {
    console.log('🧪 开始DLQ过程观察测试...\n');

    try {
        // 1. 首先检查初始状态
        console.log('1. 检查初始DLQ状态...');
        const initialResult = await makeRequest('/api/dead-letter-queue');
        console.log('初始DLQ消息数量:', initialResult.data.messages.length);
        console.log('');

        // 2. 创建DLQ消息
        console.log('2. 创建DLQ消息...');
        const dlqMessageId = 'dlq-observe-' + Date.now();
        const dlqData = {
            messageId: dlqMessageId,
            customerName: 'DLQ Observe Customer',
            amount: 123.45,
            timestamp: new Date().toISOString()
        };

        console.log('发送DLQ测试数据:', dlqData);
        const dlqResult = await makeRequest('/api/test-dlq', 'POST', dlqData);
        console.log('DLQ创建结果:', dlqResult.statusCode, dlqResult.data);
        console.log('');

        // 3. 等待处理并观察DLQ队列
        console.log('3. 观察DLQ队列...');
        await new Promise(resolve => setTimeout(resolve, 2000));

        const afterDLQResult = await makeRequest('/api/dead-letter-queue');
        console.log('处理后DLQ消息数量:', afterDLQResult.data.messages.length);

        if (afterDLQResult.data.messages.length > 0) {
            console.log('DLQ消息详情:');
            afterDLQResult.data.messages.forEach((msg, index) => {
                console.log(`  消息${index + 1}:`, {
                    id: msg.id,
                    originalMessageId: msg.originalMessageId,
                    customerName: msg.customerName,
                    amount: msg.amount,
                    failureReason: msg.failureReason,
                    attemptNumber: msg.attemptNumber
                });
            });
        }
        console.log('');

        // 4. 检查消息队列
        console.log('4. 检查消息队列...');
        const messageQueueResult = await makeRequest('/api/message-queue');
        console.log('消息队列数量:', messageQueueResult.data.length);

        if (messageQueueResult.data.length > 0) {
            console.log('最新的消息记录:');
            messageQueueResult.data.slice(0, 3).forEach((msg, index) => {
                console.log(`  记录${index + 1}:`, {
                    messageId: msg.messageId,
                    consumerName: msg.consumerName,
                    status: msg.status,
                    result: msg.result,
                    processedAt: msg.processedAt
                });
            });
        }
        console.log('');

        // 5. 检查统计信息
        console.log('5. 检查统计信息...');
        const statsResult = await makeRequest('/api/dead-letter-queue');
        console.log('当前统计:', statsResult.data.stats);
        console.log('');

        console.log('🎉 DLQ过程观察完成！');

    } catch (error) {
        console.error('❌ 观察过程中发生错误:', error);
    }
}

// 运行观察测试
observeDLQProcess();