# Deterministic Simulation Lab

一个以整数状态驱动的 C# 固定步长模拟和回滚实验。输入晚到时，从保留的历史快照重新执行受影响的 tick，验证最终状态哈希与按时接收输入的参考时间线一致。

本项目于 **2026-10-30** 新建，由 AI 辅助实现，是独立技术演示，不是历史商业代码或生产网络同步方案。尚未进行 Unity 集成验证，也没有 Unity Physics、网络传输或图形界面。

## 运行

需要 .NET SDK 9.0，无第三方 NuGet 依赖。

```sh
dotnet run --project tests/DeterministicSimulation.Specs -c Release
dotnet run --project demo/DeterministicSimulation.Demo -c Release
```

演示先预测到 tick 12，再补交 tick 3 的输入，回滚并重放 9 个 tick，最后打印与参考运行一致的 SHA-256。

## 核心设计

| 内容 | 实现 | 验证 |
| --- | --- | --- |
| 确定性状态转换 | 整数坐标、加速度、阻力、速度上限和墙面停止 | 1000 tick 的独立 decimal 模型 |
| 输入规范化 | 玩家按 ID 排序，缺失输入表示中性输入 | 枚举顺序变化得到相同哈希 |
| 状态摘要 | 版本化格式、显式整数序列、小端编码、SHA-256 | Python 独立编码的 golden hash |
| 迟到纠正 | 输入日志、不可变快照、从最早变更 tick 重放 | 240 个输入乱序补交后逐 tick 一致 |
| 有界历史 | 回滚窗口与未来输入窗口 | 最旧可纠正边界、500 tick 空间上限 |
| 幂等修订 | 重复输入不重放，中性输入能撤销旧输入 | 重复/撤回/拒绝输入均有规格 |

**21 项可执行规格**全部使用显式预期或独立参考计算；失败返回非零退出码。

## 时间和输入契约

`WorldState.Tick = t` 表示处理 tick `t` 的输入之前的状态。`Step` 恰好前进一步，调用频率不参与计算。外层应用决定一个 tick 对应现实中的多少毫秒，本库不读取系统时钟。

`SetInput(tick, playerId, input)` 设置该位置的最终权威输入；对于同一个位置后提交的值替换先前的值。缺失输入固定预测为中性输入，而不是重复上一帧按键。如果网络消息可能乱序覆盖旧修订，传输适配层必须先按自己的权威序号筛选。本库只保证**相同最终输入日志**得到相同模拟结果。

```csharp
var initial = Simulation.Create(new SimulationConfig(), new[] { new Body(0, 0, 0) });
var session = new RollbackSession(initial, historyWindow: 120, futureWindow: 30);
session.Advance(10);
session.SetInput(3, 0, new PlayerInput(1, -1));
Console.WriteLine(Simulation.Hash(session.Current));
```

## 限制

- 玩家名册固定，使用方单线程持有 `RollbackSession`；没有实体创建/销毁、碰撞求解、锁步通讯或插值渲染。
- 过旧或过远的输入直接拒绝，真实网络客户端需要另行实现权威快照重同步。
- 坐标是每 tick 的整数单位；不包含浮点物理、时钟抖动、随机数或平台相关外部状态。
- 回滚成本为受影响 tick 数乘以玩家数，历史快照占用 O(窗口 × 玩家数) 内存。没有压缩或增量快照。
- 哈希用于比较状态；不是身份认证、反作弊或消息签名。

详细格式、边界与回滚流程见 [设计说明](docs/design.md)。MIT License。
