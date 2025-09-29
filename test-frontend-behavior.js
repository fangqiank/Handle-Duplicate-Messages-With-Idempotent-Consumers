const http = require('http');

// 模拟前端的apiCall函数
async function apiCall(path, method = 'GET', data = null) {
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

// 模拟前端的updateStats函数
async function updateStats() {
    try {
        console.log('=== 开始更新统计数据 ===');
        const response = await apiCall('/api/dead-letter-queue');
        console.log('API响应状态:', response.statusCode);
        console.log('API响应数据:', JSON.stringify(response.data, null, 2));

        const stats = response.data.stats;
        console.log('统计数据对象:', stats);

        // 模拟前端更新显示
        console.log('\n=== 模拟前端显示更新 ===');
        console.log('总处理消息:', stats.totalProcessedMessages || 0);
        console.log('成功订单:', stats.successfulOrders || 0);
        console.log('重复消息:', stats.duplicateMessagesDetected || 0);
        console.log('死信消息:', stats.deadLetterMessages || 0);
        console.log('========================\n');

        return stats;
    } catch (error) {
        console.error('更新统计数据失败:', error.message);
        throw error;
    }
}

// 测试函数
async function testFrontendBehavior() {
    console.log('🧪 测试前端行为模拟\n');

    try {
        // 1. 测试初始状态
        console.log('1. 测试初始统计数据...');
        const initialStats = await updateStats();

        // 2. 等待2秒
        await new Promise(resolve => setTimeout(resolve, 2000));

        // 3. 再次检查统计数据
        console.log('2. 再次检查统计数据...');
        const finalStats = await updateStats();

        // 4. 验证结果
        console.log('3. 验证结果...');
        console.log('✅ 统计数据获取成功');
        console.log('✅ 重复消息数:', finalStats.duplicateMessagesDetected);
        console.log('✅ 总处理消息数:', finalStats.totalProcessedMessages);

        if (finalStats.duplicateMessagesDetected > 0) {
            console.log('🎉 前端应该能正确显示统计数据！');
        } else {
            console.log('⚠️  重复消息数为0，可能需要先运行快速测试');
        }

    } catch (error) {
        console.error('❌ 测试失败:', error.message);
    }
}

testFrontendBehavior();