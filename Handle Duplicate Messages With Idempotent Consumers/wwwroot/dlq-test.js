// DLQ测试功能演示
// 运行方式：在浏览器控制台中执行此脚本

console.log('🧪 开始DLQ功能测试...');

// 测试数据
const testCustomerName = 'DLQ Test Customer';
const testAmount = 99.99;

async function testDLQFunctionality() {
    try {
        console.log('1. 测试单个DLQ消息...');
        await testSingleDLQ();

        console.log('2. 测试多个DLQ消息...');
        await testMultipleDLQ();

        console.log('3. 测试DLQ重试功能...');
        await testDLQRetry();

        console.log('4. 测试DLQ清理功能...');
        await testDLQCleanup();

        console.log('✅ 所有DLQ测试完成！');
    } catch (error) {
        console.error('❌ DLQ测试失败:', error);
    }
}

// 测试单个DLQ消息
async function testSingleDLQ() {
    const dlqMessageId = 'dlq-test-' + Date.now();

    const failData = {
        messageId: dlqMessageId,
        customerName: testCustomerName + ' (Single)',
        amount: testAmount,
        timestamp: new Date().toISOString()
    };

    const response = await fetch('/api/test-dlq', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(failData)
    });

    const result = await response.json();
    console.log('单个DLQ测试结果:', result);

    if (result.success) {
        console.log('✅ 单个DLQ消息测试成功');
    } else {
        console.log('❌ 单个DLQ消息测试失败');
    }

    // 等待处理完成
    await new Promise(resolve => setTimeout(resolve, 200));
}

// 测试多个DLQ消息
async function testMultipleDLQ() {
    const scenarios = [
        { messageId: 'dlq-multi-1-' + Date.now(), customerName: testCustomerName + ' (Multi 1)', amount: testAmount, scenario: 'Basic' },
        { messageId: 'dlq-multi-2-' + Date.now(), customerName: testCustomerName + ' (Multi 2)', amount: testAmount * 2, scenario: 'High Amount' },
        { messageId: 'dlq-multi-3-' + Date.now(), customerName: testCustomerName + ' (Multi 3)', amount: testAmount * 0.5, scenario: 'Low Amount' }
    ];

    const results = [];
    for (let i = 0; i < scenarios.length; i++) {
        const scenario = scenarios[i];

        const failData = {
            messageId: scenario.messageId,
            customerName: scenario.customerName,
            amount: scenario.amount,
            timestamp: new Date().toISOString()
        };

        try {
            const response = await fetch('/api/test-dlq', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(failData)
            });

            const result = await response.json();
            results.push({ scenario: scenario.scenario, success: result.success });
            console.log(`${scenario.scenario} DLQ测试结果:`, result);

            // 小延迟
            await new Promise(resolve => setTimeout(resolve, 100));
        } catch (error) {
            results.push({ scenario: scenario.scenario, success: false, error: error.message });
            console.error(`${scenario.scenario} DLQ测试失败:`, error);
        }
    }

    const successCount = results.filter(r => r.success).length;
    console.log(`✅ 多个DLQ消息测试完成: ${successCount}/${scenarios.length} 成功`);
}

// 测试DLQ重试功能
async function testDLQRetry() {
    try {
        // 获取DLQ列表
        const dlqResponse = await fetch('/api/dead-letter-queue');
        const dlqData = await dlqResponse.json();

        if (dlqData.messages && dlqData.messages.length > 0) {
            console.log(`发现 ${dlqData.messages.length} 个DLQ消息，测试重试功能...`);

            // 重试第一个消息
            const firstMessage = dlqData.messages[0];
            const retryResponse = await fetch(`/api/dead-letter-queue/${firstMessage.id}/retry`, {
                method: 'POST'
            });

            const retryResult = await retryResponse.json();
            console.log('DLQ重试测试结果:', retryResult);

            if (retryResponse.ok) {
                console.log('✅ DLQ重试功能测试成功');
            } else {
                console.log('❌ DLQ重试功能测试失败');
            }
        } else {
            console.log('ℹ️ 没有DLQ消息可重试，跳过重试测试');
        }
    } catch (error) {
        console.error('❌ DLQ重试测试失败:', error);
    }
}

// 测试DLQ清理功能
async function testDLQCleanup() {
    try {
        // 获取DLQ列表
        const dlqResponse = await fetch('/api/dead-letter-queue');
        const dlqData = await dlqResponse.json();

        if (dlqData.messages && dlqData.messages.length > 0) {
            console.log(`发现 ${dlqData.messages.length} 个DLQ消息，测试清理功能...`);

            // 清理所有数据
            const clearResponse = await fetch('/api/clear', {
                method: 'POST'
            });

            const clearResult = await clearResponse.json();
            console.log('DLQ清理测试结果:', clearResult);

            if (clearResponse.ok) {
                console.log('✅ DLQ清理功能测试成功');
            } else {
                console.log('❌ DLQ清理功能测试失败');
            }
        } else {
            console.log('ℹ️ 没有DLQ消息可清理，跳过清理测试');
        }
    } catch (error) {
        console.error('❌ DLQ清理测试失败:', error);
    }
}

// 运行完整的DLQ功能测试
testDLQFunctionality().then(() => {
    console.log('🎉 DLQ功能测试完成！');

    // 显示最终统计
    setTimeout(async () => {
        try {
            const statsResponse = await fetch('/api/dead-letter-queue');
            const statsData = await statsResponse.json();
            console.log('📊 最终统计:', statsData.stats);
        } catch (error) {
            console.error('获取最终统计失败:', error);
        }
    }, 1000);
});

// 手动测试功能
window.testDLQ = {
    single: testSingleDLQ,
    multiple: testMultipleDLQ,
    retry: testDLQRetry,
    cleanup: testDLQCleanup,
    all: testDLQFunctionality
};

console.log('🔧 DLQ测试功能已加载，可以使用以下命令:');
console.log('  testDLQ.single() - 测试单个DLQ消息');
console.log('  testDLQ.multiple() - 测试多个DLQ消息');
console.log('  testDLQ.retry() - 测试DLQ重试功能');
console.log('  testDLQ.cleanup() - 测试DLQ清理功能');
console.log('  testDLQ.all() - 运行所有测试');