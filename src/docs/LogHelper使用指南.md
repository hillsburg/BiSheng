# LogHelper 使用指南

## 概述

`LogHelper` 是基于 NLog 封装的静态日志工具类，为 BiSheng.Latte 客户端提供统一的日志记录能力。

## 日志文件位置

日志文件保存在 **exe 目录下的 log 文件夹**：
- `log/app-{日期}.log` —— 所有级别的日志（Trace ~ Fatal）
- `log/error-{日期}.log` —— 仅 Error 和 Fatal 级别的日志

日志格式：
```
2025-01-15 14:30:22.123 | INFO  | 应用启动
2025-01-15 14:30:23.456 | ERROR | 同步失败 [启动同步] System.Net.Http.HttpRequestException: ...
```

## 日志级别

| 级别 | 用途 | 示例 |
|------|------|------|
| **Trace** | 最详细的调试信息，通常只在开发时启用 | 变量值、循环迭代 |
| **Debug** | 调试信息 | 方法调用、状态变化 |
| **Info** | 一般信息，记录重要事件 | 应用启动、同步完成 |
| **Warn** | 警告信息，可能的问题 | 配置缺失、降级处理 |
| **Error** | 错误信息，需要关注的问题 | 网络失败、数据异常 |
| **Fatal** | 致命错误，应用无法继续运行 | 未处理异常、关键组件失败 |

## 使用方法

### 1. 简单消息

```csharp
LogHelper.Info("应用启动");
LogHelper.Debug("加载笔记完成");
LogHelper.Warn("配置文件缺失，使用默认设置");
```

### 2. 带格式化参数

```csharp
LogHelper.Info("同步完成，共 {0} 条变更", count);
LogHelper.Debug("加载笔记: {0} (ID: {1})", note.Title, note.Id);
LogHelper.Warn("重试第 {0} 次", retryCount);
```

### 3. 记录异常

```csharp
try
{
    await UploadImageAsync(image);
}
catch (Exception ex)
{
    LogHelper.Error("图片上传失败", ex);
}
```

### 4. 带上下文信息的异常

```csharp
try
{
    await syncService.StartAsync();
}
catch (Exception ex)
{
    LogHelper.Error($"同步失败 [触发器: {trigger}]", ex);
}
```

## 初始化与关闭

日志系统已在 `App.xaml.cs` 中自动初始化和关闭：

```csharp
protected override void OnStartup(StartupEventArgs e)
{
    LogHelper.Initialize();  // 初始化（创建 log 目录、配置 NLog）
    LogHelper.Info("应用启动");
    
    // ... 其他初始化代码
}

protected override void OnExit(ExitEventArgs e)
{
    LogHelper.Info("应用退出");
    LogHelper.Shutdown();  // 关闭（刷新缓冲区）
    base.OnExit(e);
}
```

## 日志归档策略

- **按天归档**：每天生成新的日志文件
- **保留 30 天**：自动删除 30 天前的旧日志
- **UTF-8 编码**：支持中文和其他特殊字符

## 最佳实践

### ✅ 推荐

1. **在关键路径添加日志**
   ```csharp
   public async Task StartSyncEngineAsync()
   {
       LogHelper.Info("同步引擎启动");
       // ... 业务逻辑
       LogHelper.Debug("同步引擎已就绪");
   }
   ```

2. **异常必须记录**
   ```csharp
   catch (Exception ex)
   {
       LogHelper.Error("操作失败", ex);
       // 处理异常
   }
   ```

3. **使用格式化参数而非字符串拼接**
   ```csharp
   // ✅ 好
   LogHelper.Info("处理 {0} 个文件", count);
   
   // ❌ 差（性能差）
   LogHelper.Info("处理 " + count + " 个文件");
   ```

4. **选择合适的日志级别**
   ```csharp
   LogHelper.Trace("变量 x = {0}", x);        // 开发调试
   LogHelper.Debug("方法调用");                // 调试信息
   LogHelper.Info("用户登录");                 // 重要事件
   LogHelper.Warn("磁盘空间不足");             // 潜在问题
   LogHelper.Error("数据库连接失败", ex);      // 需要关注
   LogHelper.Fatal("应用崩溃", ex);            // 致命错误
   ```

### ❌ 避免

1. **不要在循环中记录大量日志**
   ```csharp
   // ❌ 不好
   foreach (var item in items)
   {
       LogHelper.Debug("处理项目: {0}", item.Id);  // 可能产生大量日志
   }
   
   // ✅ 好
   LogHelper.Debug("开始处理 {0} 个项目", items.Count);
   foreach (var item in items)
   {
       // 处理逻辑
   }
   LogHelper.Debug("处理完成");
   ```

2. **不要记录敏感信息**
   ```csharp
   // ❌ 不好
   LogHelper.Info("用户密码: {0}", password);
   
   // ✅ 好
   LogHelper.Info("用户登录成功");
   ```

## 查看日志

日志文件位于应用目录下的 `log` 文件夹：

```
Bisheng.Latte.exe
├── log/
│   ├── app-2025-01-15.log      # 当天的完整日志
│   ├── app-2025-01-14.log      # 昨天的日志
│   ├── error-2025-01-15.log    # 当天的错误日志
│   └── error-2025-01-14.log    # 昨天的错误日志
├── Bisheng.Latte.exe
└── ...
```

可以使用任何文本编辑器（如 Notepad++、VS Code）打开查看。

## 故障排查

### 日志文件没有生成

1. 检查 `LogHelper.Initialize()` 是否被调用
2. 检查 exe 目录是否有写入权限
3. 查看应用启动时是否有异常

### 日志内容不完整

1. 确保调用 `LogHelper.Shutdown()` 刷新缓冲区
2. 检查日志级别是否被过滤（默认记录所有级别）

### 性能问题

1. 减少 Trace/Debug 级别的日志输出
2. 考虑在发布版本中提高日志级别阈值
3. 避免在高频调用路径记录日志
