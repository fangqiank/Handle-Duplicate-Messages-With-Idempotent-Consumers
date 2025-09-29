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

async function testClearAndRapid() {
    console.log('🧪 测试：清理数据后执行Rapid Test\n');

    try {
        // 1. 清理所有数据
        console.log('1. 清理所有数据...');
        const clearResult = await makeRequest('/api/clear', 'POST');
        console.log('清理结果:', clearResult.statusCode, clearResult.data);
        console.log('');

        // 等待2秒让清理完成
        await new Promise(resolve => setTimeout(resolve, 2000));

        // 2. 检查清理后的状态
        console.log('2. 检查清理后的状态...');
        const statsAfterClear = await makeRequest('/api/dead-letter-queue');
        console.log('清理后统计:', statsAfterClear.data.stats);
        console.log('');

        // 等待2秒
        await new Promise(resolve => setTimeout(resolve, 2000));

        // 3. 执行Rapid Test
        console.log('3. 执行Rapid Test...');
        const rapidTestData = {
            messageId: 'rapid-test-fixed-12345',
            customerName: 'Rapid Test Customer',
            amount: 99.99
        };

        // 发送3个相同的请求（模拟Rapid Test）
        console.log('发送第一个请求...');
        const result1 = await makeRequest('/api/orders', 'POST', rapidTestData);
        console.log('请求1结果:', result1.statusCode, result1.data);

        console.log('发送第二个请求...');
        const result2 = await makeRequest('/api/orders', 'POST', rapidTestData);
        console.log('请求2结果:', result2.statusCode, result2.data);

        console.log('发送第三个请求...');
        const result3 = await makeRequest('/api/orders', 'POST', rapidTestData);
        console.log('请求3结果:', result3.statusCode, result3.data);

        console.log('');

        // 等待2秒让处理完成
        await new Promise(resolve => setTimeout(resolve, 2000));

        // 4. 检查最终统计
        console.log('4. 检查最终统计...');
        const finalStats = await makeRequest('/api/dead-letter-queue');
        console.log('最终统计:', finalStats.data.stats);
        console.log('');

        console.log('🎉 测试完成！');
        console.log('预期结果：');
        console.log('- Total Processed Messages: 1');
        console.log('- Duplicate Messages Detected: 2');
        console.log('- Successful Orders: 1');

    } catch (error) {
        console.error('❌ 测试过程中发生错误:', error.message);
    }
}

testClearAndRapid();