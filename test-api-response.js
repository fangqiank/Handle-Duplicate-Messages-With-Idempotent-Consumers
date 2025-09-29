// Test API response structure
async function testAPI() {
    try {
        console.log('=== 测试API响应结构 ===');

        // 测试主要统计端点
        const response = await fetch('/api/dead-letter-queue');
        const data = await response.json();

        console.log('完整API响应:', JSON.stringify(data, null, 2));
        console.log('统计对象:', data.stats);
        console.log('消息数组:', data.messages);

        // 测试debug端点
        const debugResponse = await fetch('/api/debug-duplicates');
        const debugData = await debugResponse.json();
        console.log('Debug重复数据:', JSON.stringify(debugData, null, 2));

        // 测试重复检测
        console.log('=== 测试重复检测 ===');
        const testResponse = await fetch('/api/test-duplicate-stats', {
            method: 'POST'
        });
        const testData = await testResponse.json();
        console.log('重复检测测试结果:', JSON.stringify(testData, null, 2));

    } catch (error) {
        console.error('API测试失败:', error);
    }
}

// 运行测试
testAPI();