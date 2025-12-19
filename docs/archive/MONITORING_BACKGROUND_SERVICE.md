# Monitoring Background Service

## 概述

`MonitoringBackgroundService` 是一个后台托管服务，负责定期从数据库获取监控数据并通过 SignalR 广播给所有连接的客户端。

## 架构设计

### 职责分离

1. **MonitoringHub** (`Infrastructure.Hubs.MonitoringHub`)
   - 仅负责管理 SignalR 连接和分组
   - 不包含业务逻辑
   - 客户端连接时自动加入 "monitoring" 组

2. **MonitoringService** (`Infrastructure.Services.MonitoringService`)
   - 实现 `IMonitoringService` 接口
   - 负责从数据库查询监控数据
   - 提供三个广播方法：
     - `BroadcastActivityStatsUpdateAsync()` - 广播活动统计
     - `BroadcastSecurityAlertsUpdateAsync()` - 广播安全警报
     - `BroadcastSystemMetricsUpdateAsync()` - 广播系统指标

3. **MonitoringBackgroundService** (`Infrastructure.BackgroundServices.MonitoringBackgroundService`)
   - 后台定时任务服务
   - 按不同频率调用 MonitoringService 的广播方法
   - 使用 DI Scope 解析服务

## 更新频率

| 数据类型 | 更新间隔 | 方法 |
|---------|---------|------|
| 活动统计 (Activity Stats) | 5 秒 | `BroadcastActivityStatsUpdateAsync()` |
| 安全警报 (Security Alerts) | 10 秒 | `BroadcastSecurityAlertsUpdateAsync()` |
| 系统指标 (System Metrics) | 15 秒 | `BroadcastSystemMetricsUpdateAsync()` |

## 数据源

### 1. Activity Stats (活动统计)
- **数据源**: `LoginHistories` 表
- **统计范围**: 最近 24 小时
- **包含内容**:
  - `ActiveSessions`: 活跃会话数
  - `TotalLogins`: 总登录次数
  - `FailedLogins`: 失败登录次数
  - `RiskScore`: 平均风险分数

### 2. Security Alerts (安全警报)
- **数据源**: `LoginHistories` 和 `AuditEvents` 表
- **统计范围**: 最近 1 小时
- **检测内容**:
  - 异常登录行为 (标记为可疑的登录)
  - 暴力破解攻击 (同一 IP 多次失败登录)
  - 安全相关的审计事件
- **返回**: 最多 10 条最新警报

### 3. System Metrics (系统指标)
- **数据源**: Prometheus `/metrics` 端点
- **内容**: 
  - `Gauges`: 仪表盘指标 (当前值)
  - `Counters`: 计数器指标 (累计值)
  - `Histograms`: 直方图指标 (分布统计)

## SignalR 客户端事件

客户端连接到 `/monitoringHub` 后，会收到以下事件：

```javascript
// 连接到 Hub
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/monitoringHub")
    .build();

// 监听活动统计更新 (每 5 秒)
connection.on("ActivityStatsUpdated", (stats) => {
    console.log("Activity Stats:", stats);
    // stats = { activeSessions, totalLogins, failedLogins, riskScore }
});

// 监听安全警报更新 (每 10 秒)
connection.on("SecurityAlertsUpdated", (alerts) => {
    console.log("Security Alerts:", alerts);
    // alerts = [{ id, type, message, timestamp, severity }, ...]
});

// 监听系统指标更新 (每 15 秒)
connection.on("SystemMetricsUpdated", (metrics) => {
    console.log("System Metrics:", metrics);
    // metrics = { gauges: {}, counters: {}, histograms: {} }
});

await connection.start();
```

## 配置

### 注册服务

在 `Program.cs` 中已注册：

```csharp
// Add SignalR services
builder.Services.AddSignalR();

// Add Monitoring Background Service
builder.Services.AddHostedService<Infrastructure.BackgroundServices.MonitoringBackgroundService>();

// Map SignalR Hub
app.MapHub<Infrastructure.Hubs.MonitoringHub>("/monitoringHub");
```

### 系统指标配置 (可选)

在 `appsettings.json` 中配置 Prometheus 端点：

```json
{
  "Monitoring": {
    "MetricsBaseUrl": "https://localhost:7035"
  },
  // 或
  "Observability": {
    "MetricsBaseUrl": "https://localhost:7035"
  }
}
```

如果未配置，默认使用 `https://localhost:7035`。

## 错误处理

- **独立错误处理**: 每个广播方法都有独立的 try-catch，单个广播失败不影响其他广播
- **服务继续运行**: 即使发生错误，后台服务也会继续运行
- **日志记录**: 所有错误都会记录到日志系统
- **优雅关闭**: 接收到停止信号时会优雅关闭

## 性能考虑

1. **使用 Scoped 服务**: 每次循环创建新的 DI Scope，避免长时间持有 DbContext
2. **不同更新频率**: 根据数据重要性和查询成本设置不同的更新间隔
3. **异步操作**: 所有数据库和网络操作都是异步的
4. **SignalR 分组**: 使用 "monitoring" 组进行高效广播

## 日志级别

- **Information**: 服务启动/停止事件
- **Debug**: 每次广播操作 (可在生产环境关闭)
- **Error**: 广播失败或其他错误

## 测试

### 手动测试

1. 启动应用程序
2. 打开浏览器开发者工具
3. 在控制台中连接到 SignalR Hub (参考上面的客户端代码)
4. 观察定期收到的更新事件

### 监控日志

查看应用程序日志，应该看到：
```
[Information] Monitoring Background Service started
[Debug] Broadcasting activity stats update
[Debug] Broadcasting security alerts update
[Debug] Broadcasting system metrics update
```

## 扩展

### 修改更新频率

在 `MonitoringBackgroundService.cs` 中调整间隔：

```csharp
private readonly TimeSpan activityStatsInterval = TimeSpan.FromSeconds(5);
private readonly TimeSpan securityAlertsInterval = TimeSpan.FromSeconds(10);
private readonly TimeSpan systemMetricsInterval = TimeSpan.FromSeconds(15);
```

### 添加新的监控数据类型

1. 在 `IMonitoringService` 中添加新方法
2. 在 `MonitoringService` 中实现查询和广播逻辑
3. 在 `MonitoringBackgroundService` 中添加定时调用
4. 在客户端监听新的 SignalR 事件

## 相关文件

- `Infrastructure/BackgroundServices/MonitoringBackgroundService.cs` - 后台服务实现
- `Infrastructure/Hubs/MonitoringHub.cs` - SignalR Hub
- `Infrastructure/Services/MonitoringService.cs` - 监控服务实现
- `Core.Application/IMonitoringService.cs` - 监控服务接口
- `Core.Application/DTOs/MonitoringDtos.cs` - 数据传输对象
- `Web.IdP/Program.cs` - 服务注册

## 故障排除

### 客户端没有收到更新

1. 检查 SignalR 连接是否成功建立
2. 确认客户端正确监听事件名称 (区分大小写)
3. 查看服务器日志是否有错误

### 广播频率异常

1. 检查后台服务是否正常运行
2. 查看日志中的时间戳
3. 确认数据库连接正常

### 系统指标为空

1. 确认 Prometheus 端点已启用 (`Observability:PrometheusEnabled = true`)
2. 检查 Metrics URL 配置是否正确
3. 确认 IP 白名单配置允许本地访问

