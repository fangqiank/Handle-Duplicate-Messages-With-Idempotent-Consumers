// DLQ API测试
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

async function testDLQAPI() {
    console.log('🧪 开始DLQ API测试...\n');

    try {
        // 1. 测试单个DLQ消息
        console.log('1. 测试单个DLQ消息...');
        const dlqMessageId = 'dlq-test-' + Date.now();
        const dlqData = {
            messageId: dlqMessageId,
            customerName: 'DLQ Test Customer (Single)',
            amount: 99.99,
            timestamp: new Date().toISOString()
        };

        const dlqResult = await makeRequest('/api/test-dlq', 'POST', dlqData);
        console.log('DLQ测试结果:', dlqResult.statusCode, dlqResult.data);

        if (dlqResult.statusCode === 200 && dlqResult.data.success) {
            console.log('✅ 单个DLQ消息测试成功\n');
        } else {
            console.log('❌ 单个DLQ消息测试失败\n');
        }

        // 等待处理完成
        await new Promise(resolve => setTimeout(resolve, 1000));

        // 2. 检查DLQ队列
        console.log('2. 检查DLQ队列...');
        const dlqQueueResult = await makeRequest('/api/dead-letter-queue');
        console.log('DLQ队列状态:', dlqQueueResult.statusCode);

        if (dlqQueueResult.statusCode === 200) {
            const messages = dlqQueueResult.data.messages || [];
            console.log(`发现 ${messages.length} 个DLQ消息`);

            if (messages.length > 0) {
                console.log('最新的DLQ消息:', {
                    messageId: messages[0].originalMessageId,
                    customerName: messages[0].customerName,
                    amount: messages[0].amount,
                    failureReason: messages[0].failureReason
                });
            }
        }
        console.log('');

        // 3. 测试重试功能
        if (dlqQueueResult.statusCode === 200 && dlqQueueResult.data.messages && dlqQueueResult.data.messages.length > 0) {
            console.log('3. 测试重试功能...');
            const firstMessage = dlqQueueResult.data.messages[0];
            const retryResult = await makeRequest(`/api/dead-letter-queue/${firstMessage.id}/retry`, 'POST');
            console.log('重试结果:', retryResult.statusCode, retryResult.data);

            if (retryResult.statusCode === 200) {
                console.log('✅ 重试功能测试成功\n');
            } else {
                console.log('❌ 重试功能测试失败\n');
            }
        } else {
            console.log('ℹ️ 没有DLQ消息可重试，跳过重试测试\n');
        }

        // 4. 测试清理功能
        console.log('4. 测试清理功能...');
        const clearResult = await makeRequest('/api/clear', 'POST');
        console.log('清理结果:', clearResult.statusCode, clearResult.data);

        if (clearResult.statusCode === 200) {
            console.log('✅ 清理功能测试成功\n');
        } else {
            console.log('❌ 清理功能测试失败\n');
        }

        // 5. 最终检查
        console.log('5. 最终检查...');
        const finalCheck = await makeRequest('/api/dead-letter-queue');
        console.log('最终DLQ队列状态:', finalCheck.statusCode);

        if (finalCheck.statusCode === 200) {
            const finalMessages = finalCheck.data.messages || [];
            console.log(`清理后剩余 ${finalMessages.length} 个DLQ消息`);
        }

        console.log('🎉 DLQ API测试完成！');

    } catch (error) {
        console.error('❌ 测试过程中发生错误:', error);
        console.log(`请确保应用程序正在运行在 ${BASE_URL}`);
    }
}

// 运行测试
testDLQAPI();