const http = require('http');

function makeRequest(path, method = 'GET', data = null) {
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
                    resolve({
                        statusCode: res.statusCode,
                        data: body
                    });
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

async function testFinalFix() {
    console.log('🧪 测试最终修复\n');

    try {
        // 1. 执行快速测试
        console.log('1. 执行快速测试...');
        const testData = {
            messageId: 'final-test-' + Date.now(),
            customerName: 'Final Test Customer',
            amount: 123.45
        };

        // 发送3个相同消息
        const results = [];
        for (let i = 0; i < 3; i++) {
            const result = await makeRequest('/api/orders', 'POST', testData);
            results.push(result);
        }

        const successCount = results.filter(r => r.data.success).length;
        console.log(`快速测试结果: ${successCount}/3 成功`);

        // 等待2秒让处理完成
        await new Promise(resolve => setTimeout(resolve, 2000));

        // 2. 检查统计
        console.log('2. 检查统计...');
        const statsResponse = await makeRequest('/api/dead-letter-queue');
        const stats = statsResponse.data.stats;

        console.log('最终统计:');
        console.log('- 总处理消息:', stats.totalProcessedMessages);
        console.log('- 成功订单:', stats.successfulOrders);
        console.log('- 重复消息:', stats.duplicateMessagesDetected);
        console.log('- 死信消息:', stats.deadLetterMessages);

        // 3. 验证结果
        console.log('\n3. 验证结果...');
        const expectedStats = {
            totalProcessedMessages: 1,
            successfulOrders: 1,
            duplicateMessagesDetected: 2,
            deadLetterMessages: 0
        };

        let allCorrect = true;
        for (const [key, expectedValue] of Object.entries(expectedStats)) {
            const actualValue = stats[key];
            const isCorrect = actualValue === expectedValue;
            allCorrect = allCorrect && isCorrect;
            console.log(`${key}: ${actualValue} ${isCorrect ? '✅' : '❌'}`);
        }

        console.log('\n4. 总结...');
        if (allCorrect) {
            console.log('🎉 所有统计数据都正确！');
            console.log('✅ 前端现在应该能正确显示:');
            console.log('   - 总处理消息: 1');
            console.log('   - 成功订单: 1');
            console.log('   - 重复消息: 2');
            console.log('   - 死信消息: 0');
        } else {
            console.log('❌ 统计数据不正确，需要进一步调试');
        }

    } catch (error) {
        console.error('❌ 测试过程中发生错误:', error.message);
    }
}

testFinalFix();