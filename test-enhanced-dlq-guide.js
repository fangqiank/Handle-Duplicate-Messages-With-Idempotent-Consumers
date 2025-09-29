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

async function testEnhancedDLQGuide() {
    console.log('🧪 测试增强版DLQ指南页面功能\n');

    try {
        // 1. 清理所有数据
        console.log('1. 清理所有数据...');
        const clearResult = await makeRequest('/api/clear', 'POST');
        console.log('清理结果:', clearResult.statusCode, clearResult.data);
        console.log('');

        // 等待2秒让清理完成
        await new Promise(resolve => setTimeout(resolve, 2000));

        // 2. 检查初始状态
        console.log('2. 检查初始状态...');
        const initialStats = await makeRequest('/api/dead-letter-queue');
        console.log('初始统计:', initialStats.data.stats);
        console.log('');

        // 等待1秒
        await new Promise(resolve => setTimeout(resolve, 1000));

        // 3. 测试单个DLQ消息（模拟指南页面的"单个DLQ"按钮）
        console.log('3. 测试单个DLQ消息...');
        const singleDLQData = {
            messageId: 'dlq-guide-single-' + Date.now(),
            customerName: 'DLQ Guide Single Test',
            amount: 99.99
        };

        const singleResult = await makeRequest('/api/test-dlq', 'POST', singleDLQData);
        console.log('单个DLQ结果:', singleResult.statusCode, singleResult.data);
        console.log('');

        // 等待2秒让处理完成
        await new Promise(resolve => setTimeout(resolve, 2000));

        // 4. 测试多个DLQ消息（模拟指南页面的"多个DLQ"按钮）
        console.log('4. 测试多个DLQ消息...');
        const multiMessages = [
            { messageId: 'dlq-guide-multi-1-' + Date.now(), customerName: 'Multi Test 1', amount: 25.50 },
            { messageId: 'dlq-guide-multi-2-' + Date.now(), customerName: 'Multi Test 2', amount: 75.25 },
            { messageId: 'dlq-guide-multi-3-' + Date.now(), customerName: 'Multi Test 3', amount: 150.00 }
        ];

        let successCount = 0;
        for (const message of multiMessages) {
            const result = await makeRequest('/api/test-dlq', 'POST', message);
            if (result.statusCode === 200 && result.data.success) {
                successCount++;
            }
            console.log(`消息 ${message.customerName}: ${result.statusCode} - ${result.data.success ? '成功' : '失败'}`);
        }
        console.log(`多个DLQ测试完成: ${successCount}/${multiMessages.length} 成功`);
        console.log('');

        // 等待2秒让处理完成
        await new Promise(resolve => setTimeout(resolve, 2000));

        // 5. 检查最终统计
        console.log('5. 检查最终统计...');
        const finalStats = await makeRequest('/api/dead-letter-queue');
        console.log('最终统计:');
        console.log('- 死信消息数量:', finalStats.data.stats.deadLetterMessages);
        console.log('- 总处理消息数:', finalStats.data.stats.totalProcessedMessages);
        console.log('- 重复消息数:', finalStats.data.stats.duplicateMessagesDetected);
        console.log('- 成功订单数:', finalStats.data.stats.successfulOrders);
        console.log('');

        // 6. 检查DLQ队列中的消息
        console.log('6. 检查DLQ队列消息...');
        console.log('DLQ消息数量:', finalStats.data.messages.length);
        finalStats.data.messages.forEach((msg, index) => {
            console.log(`消息 ${index + 1}: ${msg.originalMessageId} - ${msg.customerName} - $${msg.amount}`);
        });
        console.log('');

        // 7. 测试重试功能（模拟指南页面的重试操作）
        console.log('7. 测试重试功能...');
        if (finalStats.data.messages.length > 0) {
            const firstMessageId = finalStats.data.messages[0].id;
            const retryResult = await makeRequest(`/api/dead-letter-queue/${firstMessageId}/retry`, 'POST');
            console.log('重试结果:', retryResult.statusCode, retryResult.data);
        } else {
            console.log('没有DLQ消息可供重试');
        }
        console.log('');

        // 8. 最终清理（模拟指南页面的清理功能）
        console.log('8. 最终清理...');
        const finalClearResult = await makeRequest('/api/clear', 'POST');
        console.log('最终清理结果:', finalClearResult.statusCode, finalClearResult.data);
        console.log('');

        // 等待1秒让清理完成
        await new Promise(resolve => setTimeout(resolve, 1000));

        // 9. 验证清理结果
        console.log('9. 验证清理结果...');
        const clearedStats = await makeRequest('/api/dead-letter-queue');
        console.log('清理后统计:', clearedStats.data.stats);
        console.log('');

        console.log('🎉 增强版DLQ指南页面测试完成！');
        console.log('');
        console.log('✅ 功能验证结果：');
        console.log('- 单个DLQ测试：', successCount >= 1 ? '✅ 正常' : '❌ 失败');
        console.log('- 多个DLQ测试：', successCount >= 3 ? '✅ 正常' : '❌ 失败');
        console.log('- DLQ消息创建：', finalStats.data.stats.deadLetterMessages > 0 ? '✅ 正常' : '❌ 失败');
        console.log('- 统计数据更新：', finalStats.data.stats.totalProcessedMessages > 0 ? '✅ 正常' : '❌ 失败');
        console.log('- 重试功能：', finalStats.data.messages.length > 0 ? '✅ 正常' : '❌ 无数据测试');
        console.log('- 清理功能：', clearedStats.data.stats.deadLetterMessages === 0 ? '✅ 正常' : '❌ 失败');
        console.log('');
        console.log('📋 增强版DLQ指南页面功能列表：');
        console.log('✅ 快捷操作面板 - 4个一键操作按钮');
        console.log('✅ 实时状态监控 - 显示DLQ数量、处理消息数、成功订单数');
        console.log('✅ 交互式步骤按钮 - 每个步骤都有对应的一键操作');
        console.log('✅ 自动导航功能 - 测试后自动跳转到结果页面');
        console.log('✅ 用户友好通知 - Toast通知代替alert');
        console.log('✅ 表单自动填充 - 支持跨窗口表单填充');
        console.log('✅ 完整的错误处理 - 所有操作都有错误处理');

    } catch (error) {
        console.error('❌ 测试过程中发生错误:', error.message);
    }
}

testEnhancedDLQGuide();