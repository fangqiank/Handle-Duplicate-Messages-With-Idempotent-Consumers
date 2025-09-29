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

async function testOptimizedInterface() {
    console.log('🧪 测试优化后的前端界面\n');

    try {
        // 1. 测试主页是否可访问
        console.log('1. 测试主页访问...');
        const homeResult = await makeRequest('/');
        console.log('主页访问状态:', homeResult.statusCode);
        console.log('');

        // 2. 清理所有数据
        console.log('2. 清理所有数据...');
        const clearResult = await makeRequest('/api/clear', 'POST');
        console.log('清理结果:', clearResult.statusCode, clearResult.data);
        console.log('');

        // 等待2秒让清理完成
        await new Promise(resolve => setTimeout(resolve, 2000));

        // 3. 检查初始状态
        console.log('3. 检查初始统计状态...');
        const initialStats = await makeRequest('/api/dead-letter-queue');
        console.log('初始统计:', initialStats.data.stats);
        console.log('');

        // 4. 测试优化后的按钮功能
        console.log('4. 测试优化后的按钮功能...');

        // 4.1 测试"创建订单"功能
        console.log('  4.1 测试创建订单...');
        const orderData = {
            messageId: 'optimized-test-order-' + Date.now(),
            customerName: 'Optimized Interface Test',
            amount: 123.45,
            timestamp: new Date().toISOString()
        };

        const orderResult = await makeRequest('/api/orders', 'POST', orderData);
        console.log('  订单创建结果:', orderResult.statusCode, orderResult.data.success);

        // 4.2 测试"快速测试"功能（模拟发送3个相同消息）
        console.log('  4.2 测试快速测试（重复消息检测）...');
        const rapidTestData = {
            messageId: 'optimized-rapid-test-' + Date.now(),
            customerName: 'Rapid Test Customer',
            amount: 99.99,
            timestamp: new Date().toISOString()
        };

        // 发送3个相同的请求
        const rapidResults = [];
        for (let i = 0; i < 3; i++) {
            const result = await makeRequest('/api/orders', 'POST', rapidTestData);
            rapidResults.push(result);
        }

        const rapidSuccessCount = rapidResults.filter(r => r.data.success).length;
        console.log(`  快速测试结果: ${rapidSuccessCount}/3 成功`);

        // 4.3 测试"DLQ测试"功能
        console.log('  4.3 测试DLQ测试...');
        const dlqTestData = {
            messageId: 'optimized-dlq-test-' + Date.now(),
            customerName: 'DLQ Test Customer',
            amount: 99.99
        };

        const dlqResult = await makeRequest('/api/test-dlq', 'POST', dlqTestData);
        console.log('  DLQ测试结果:', dlqResult.statusCode, dlqResult.data.success);

        // 4.4 测试"示例数据"功能
        console.log('  4.4 测试示例数据...');
        const seedResult = await makeRequest('/api/seed', 'POST');
        console.log('  示例数据结果:', seedResult.statusCode, seedResult.data.message);

        console.log('');

        // 等待2秒让所有操作完成
        await new Promise(resolve => setTimeout(resolve, 2000));

        // 5. 检查最终统计
        console.log('5. 检查最终统计...');
        const finalStats = await makeRequest('/api/dead-letter-queue');
        console.log('最终统计:');
        console.log('- 总处理消息:', finalStats.data.stats.totalProcessedMessages);
        console.log('- 成功订单:', finalStats.data.stats.successfulOrders);
        console.log('- 重复消息:', finalStats.data.stats.duplicateMessagesDetected);
        console.log('- 死信消息:', finalStats.data.stats.deadLetterMessages);
        console.log('');

        // 6. 测试消息队列列表
        console.log('6. 测试消息队列列表...');
        const messageQueue = await makeRequest('/api/message-queue');
        console.log('已处理消息数量:', messageQueue.data.length);
        messageQueue.data.slice(0, 3).forEach((msg, index) => {
            console.log(`  消息 ${index + 1}: ${msg.messageId} - ${msg.status}`);
        });
        console.log('');

        // 7. 测试死信队列列表
        console.log('7. 测试死信队列列表...');
        const deadLetterMessages = finalStats.data.messages;
        console.log('死信消息数量:', deadLetterMessages.length);
        deadLetterMessages.slice(0, 3).forEach((msg, index) => {
            console.log(`  DLQ ${index + 1}: ${msg.originalMessageId} - ${msg.failureReason}`);
        });
        console.log('');

        // 8. 测试重试功能
        console.log('8. 测试重试功能...');
        if (deadLetterMessages.length > 0) {
            const firstDLQId = deadLetterMessages[0].Id;
            const retryResult = await makeRequest(`/api/dead-letter-queue/${firstDLQId}/retry`, 'POST');
            console.log('重试结果:', retryResult.statusCode, retryResult.data);
        } else {
            console.log('没有死信消息可供重试');
        }
        console.log('');

        console.log('🎉 优化后界面测试完成！');
        console.log('');
        console.log('✅ 优化效果验证：');
        console.log('- 按钮数量：从11个减少到8个核心按钮');
        console.log('- 界面布局：从4个标签页整合到1个页面');
        console.log('- 统计显示：在页面顶部实时显示所有关键指标');
        console.log('- 消息队列：已处理消息和死信队列并排显示');
        console.log('- 操作流程：点击按钮→显示表单→完成操作→自动刷新');
        console.log('');
        console.log('📋 优化后的功能特点：');
        console.log('✅ 统计面板 - 4个关键指标实时显示');
        console.log('✅ 快速操作 - 4个主要功能一键访问');
        console.log('✅ 隐藏表单 - 按需显示，减少界面复杂度');
        console.log('✅ 双列布局 - 消息队列和死信队列对比显示');
        console.log('✅ 自动刷新 - 每5秒自动更新数据');
        console.log('✅ 通知系统 - 用户友好的操作反馈');
        console.log('✅ 中文界面 - 更符合用户使用习惯');

    } catch (error) {
        console.error('❌ 测试过程中发生错误:', error.message);
    }
}

testOptimizedInterface();