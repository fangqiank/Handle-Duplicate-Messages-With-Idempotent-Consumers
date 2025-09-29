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

async function testRapidFix() {
    console.log('🧪 测试快速测试修复\n');

    try {
        // 1. 清理数据
        console.log('1. 清理数据...');
        const clearResult = await makeRequest('/api/clear', 'POST');
        console.log('清理结果:', clearResult.statusCode);
        await new Promise(resolve => setTimeout(resolve, 2000));

        // 2. 检查初始状态
        console.log('2. 检查初始状态...');
        const initialStats = await makeRequest('/api/dead-letter-queue');
        console.log('初始重复消息数:', initialStats.data.stats.duplicateMessagesDetected);
        await new Promise(resolve => setTimeout(resolve, 1000));

        // 3. 执行快速测试（使用固定的messageId）
        console.log('3. 执行快速测试...');
        const testData = {
            messageId: 'rapid-test-fixed-12345',
            customerName: 'Rapid Test Customer',
            amount: 99.99,
            timestamp: new Date().toISOString()
        };

        console.log('发送3个相同消息...');
        const results = [];
        for (let i = 0; i < 3; i++) {
            const result = await makeRequest('/api/orders', 'POST', testData);
            results.push(result);
            console.log(`消息 ${i + 1}: ${result.statusCode} - ${result.data.success ? '成功' : '失败'}`);
            // 等待一小段时间确保处理完成
            await new Promise(resolve => setTimeout(resolve, 100));
        }

        const successCount = results.filter(r => r.data.success).length;
        console.log(`快速测试结果: ${successCount}/3 成功`);

        // 4. 等待处理完成并检查统计
        await new Promise(resolve => setTimeout(resolve, 2000));

        console.log('4. 检查最终统计...');
        const finalStats = await makeRequest('/api/dead-letter-queue');
        console.log('最终统计:');
        console.log('- 总处理消息:', finalStats.data.stats.totalProcessedMessages);
        console.log('- 成功订单:', finalStats.data.stats.successfulOrders);
        console.log('- 重复消息:', finalStats.data.stats.duplicateMessagesDetected);
        console.log('- 死信消息:', finalStats.data.stats.deadLetterMessages);

        // 5. 检查debug信息
        console.log('5. 检查debug信息...');
        try {
            const debugResponse = await makeRequest('/api/debug-duplicates');
            console.log('Debug重复记录:', debugResponse.data);
        } catch (error) {
            console.log('无法获取debug信息');
        }

        // 6. 检查消息队列
        console.log('6. 检查消息队列...');
        const messageQueue = await makeRequest('/api/message-queue');
        console.log('消息队列记录数:', messageQueue.data.length);

        console.log('\n🎉 修复测试完成！');
        console.log('');
        console.log('✅ 预期结果：');
        console.log('- Total Processed Messages: 1 (第一个消息成功处理)');
        console.log('- Successful Orders: 1 (成功创建一个订单)');
        console.log('- Duplicate Messages Detected: 2 (后2个消息被检测为重复)');
        console.log('- Dead Letter Messages: 0 (没有死信消息)');
        console.log('');
        console.log('📋 实际结果：');
        console.log('- Total Processed Messages:', finalStats.data.stats.totalProcessedMessages);
        console.log('- Successful Orders:', finalStats.data.stats.successfulOrders);
        console.log('- Duplicate Messages Detected:', finalStats.data.stats.duplicateMessagesDetected);
        console.log('- Dead Letter Messages:', finalStats.data.stats.deadLetterMessages);
        console.log('');

        const isWorking = finalStats.data.stats.duplicateMessagesDetected === 2;
        console.log('🔧 修复状态:', isWorking ? '✅ 成功' : '❌ 仍需调试');

    } catch (error) {
        console.error('❌ 测试失败:', error.message);
    }
}

testRapidFix();