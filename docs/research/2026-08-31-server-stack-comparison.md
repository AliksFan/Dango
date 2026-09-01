# Dango 服务端技术栈专项调研

> 状态：已批准，决策见 [ADR 0002](../adr/0002-server-foundation-and-scaling.md)
>
> 日期：2026-08-31
>
> 所属阶段：Phase 0 — 工作区与技术基线

## 1. 调研目标

在既有[服务端设计基线](../server-design-v0.1.md)下，为 Dango 选择首版服务端候选技术，重点回答：

- 哪种语言和框架最适合个人开发者与 Coding Agent 长期维护？
- 怎样承载约 200 条持续实时连接和 50–100 个活跃房间？
- 实时通信应使用原始 WebSocket 还是更高层协议？
- PostgreSQL 应通过何种数据访问方案接入？
- 怎样保持单机部署简单，同时不给未来多实例扩容制造障碍？

本轮只做调研和推荐，不创建服务端脚手架，不部署服务器。推荐结论已于 2026-09-01 获用户批准，并由 ADR 0002 固化。

## 2. 已确认约束

- 初始目标为大陆单节点 Linux 部署，2 vCPU、4 GB 内存起步。
- 峰值约 200 人在线，每人一条持续实时连接。
- HTTP API、实时连接和后台任务先运行在同一个模块化单体应用中。
- PostgreSQL 是唯一持久数据库；单实例阶段不使用 Redis。
- 服务端是房间状态的唯一事实来源，客户端不逐帧上传动画。
- 空房间保存快照后休眠，离线睡眠团子不占用持续模拟循环。
- 关键权限、情侣唯一关系、房间额度、解散和迁移使用数据库事务。
- 公网通信必须使用 HTTPS/WSS，数据库和内部端口不得直接暴露公网。
- 当前开发资源主要是一名个人开发者和 Coding Agent，运维复杂度必须受控。

## 3. 评价标准

候选方案按以下顺序评价：

1. 与 Dango 服务端权威房间、事务和实时协议的适配程度。
2. 单人开发、调试、测试、部署和升级的综合成本。
3. 与已批准 Windows 客户端技术栈的协作效率，但不以共享代码破坏协议边界。
4. 200 条长连接下的资源余量；实际容量仍须压力测试。
5. 数据库迁移、并发约束、可观测性和安全能力。
6. Linux 容器部署、长期支持周期和许可证。
7. 未来移动端和多实例扩容的可演进性。

## 4. 关键事实与工程判断

### 4.1 200 条连接不是语言性能选型的决定因素

Dango 同步的是低频动作意图、时间基准和关键状态，不是每秒几十次的全量物理帧。按 200 人、50–100 个活跃房间估算，ASP.NET Core、Go、Node.js 和 Rust 都有足够的技术能力承载首版。

因此本轮不根据未经实测的吞吐排行榜选型。真正影响项目成败的是：连接生命周期是否可靠、事务规则是否清晰、协议能否测试、部署是否容易恢复，以及个人维护成本是否可控。

### 4.2 .NET 10 的生命周期适合当前起点，但需要升级计划

.NET 10 是 LTS，ASP.NET Core 和 Entity Framework Core 与运行时遵循同一支持政策，官方支持至 2028-11-14。处于支持期内仍需跟进最新补丁版本才能保持支持资格。

这为 2026 年开始的原型和首版提供了稳定窗口，但不是永久版本。项目应在 2028 年进入维护期前评估迁移到届时受支持的后续 LTS，不允许在公网长期运行已停止安全更新的运行时。

### 4.3 SignalR 能减少连接样板，但不能代替 Dango 协议

ASP.NET Core SignalR 是开源实时通信库，官方能力包括：

- 自动处理连接管理。
- 向指定客户端或客户端组发送消息。
- 优先使用 WebSocket，并能协商其他传输方式。
- 内置 JSON 和 MessagePack Hub Protocol。
- 多实例时可使用 Redis backplane 或托管 SignalR 服务。
- 官方客户端覆盖 .NET、Java、JavaScript 和 Swift；未来移动端不必因此被锁死在 Windows。

但 SignalR 只解决传输、连接和分发。以下内容仍必须由 Dango 明确定义并测试：

- 版本化消息外壳、房间事件序号和服务端时间。
- 快照、断线恢复、重复事件和幂等规则。
- 每次操作的身份、成员关系、权限和输入验证。
- 房间实例所有权、事件持久化边界和客户端平滑显示。

Hub 方法和服务端内部对象不能直接成为永久公开协议。首轮使用可读的 JSON；只有带宽或序列化实测表明确有必要时再评估 MessagePack。

### 4.4 PostgreSQL 18 与 .NET 10 数据访问链已经可用

PostgreSQL 主版本获得约 5 年支持。PostgreSQL 18 于 2025-09-25 发布，官方计划支持至 2030-11-14；部署时仍应使用该主版本的最新补丁，而不是固定在调研当天的 18.6。

Npgsql 是 PostgreSQL 的 .NET 数据提供程序，并提供 Entity Framework Core provider。NuGet 官方仓库在调研日提供稳定版 `Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3`，目标为 .NET 10，并依赖 EF Core 10 系列。

EF Core 适合首版的原因不是避免 SQL，而是提供迁移、变更跟踪、事务集成和统一查询边界。以下场景仍应允许使用集中封装的 SQL 或数据库特性：

- 并发额度和唯一关系需要明确锁定或原子语句时。
- 批量清理、恢复任务或性能热点经测量需要优化时。
- PostgreSQL 约束比应用层检查更可靠时。

数据库访问必须集中在持久化模块，不能让 Hub、HTTP 端点和后台任务各自散落直接 SQL。

## 5. 候选语言与框架

### 5.1 C# / .NET 10 / ASP.NET Core

#### 优点

- 与已批准 Windows 客户端使用同一语言和 SDK，个人项目可以集中工具链、调试经验和类型定义方式。
- ASP.NET Core 原生包含依赖注入、配置、日志、鉴权集成、健康检查、限流和 WebSocket 基础能力。
- SignalR 直接提供连接、组和广播抽象，适合小房间实时同步。
- EF Core、Npgsql 和 PostgreSQL 的 .NET 10 稳定版本链已经存在。
- Kestrel 可在 Linux 容器运行，适合 Caddy/Nginx 反向代理后的单体部署。
- .NET 与 ASP.NET Core 为开源项目，没有应用运行时授权费。

#### 风险

- 同时共享 C# 不等于可以直接序列化内部领域对象；协议边界仍需刻意维护。
- SignalR 的便利可能诱使业务逻辑进入 Hub，必须限制 Hub 只做会话适配、验证入口和消息转发。
- .NET 10 支持期结束前必须规划运行时升级。
- EF Core provider 必须与 EF Core 主版本匹配，不能无计划地单独升级依赖。

#### 适配判断

在性能足够的前提下，它显著降低了个人项目的认知、调试和端到端测试成本，是当前首选。

### 5.2 Go

#### 优点

- goroutine 和标准网络库适合大量长连接，静态二进制部署简单，资源开销通常易于控制。
- 语言和工具链较小，交叉编译、测试和容器化直接。
- 对显式并发和服务端房间循环的表达自然。

#### 风险

- 官方每个大版本只支持到两个更新的大版本发布，升级频率高于 .NET LTS。
- WebSocket、路由、验证、迁移和数据访问需要组合更多独立库并自行规定项目结构。
- 与 C# 客户端无法共享编译期类型；必须更早建设代码生成或双端 Schema 流程。
- 对只有 200 条连接的首版，性能余量不足以抵消额外工具链和应用框架决策成本。

#### 适配判断

是可靠的第二候选。如果未来实测证明 .NET 在目标服务器上存在不可接受的资源问题，或项目主要维护者更熟悉 Go，可重新评估；目前没有足够理由优先选择。

### 5.3 TypeScript / Node.js

#### 优点

- HTTP、WebSocket、Schema 验证和管理界面生态丰富，迭代速度快。
- TypeScript 能改善普通业务代码的类型体验。
- Node.js 24 当前处于 LTS，适合生产使用。

#### 风险

- 运行时、包管理器、Web 框架、实时库和 ORM 形成较多独立升级面。
- TypeScript 类型在运行时消失，权限和消息输入仍必须配置运行时 Schema 验证。
- CPU 密集任务或无意阻塞事件循环会影响同进程中的全部实时连接。
- 与 WPF 客户端没有直接工具链复用优势。

#### 适配判断

能力足够，但 Dango 不是以 Web 前端为核心的产品。相比 ASP.NET Core，它没有提供足以覆盖额外依赖与运行时边界的收益。

### 5.4 Rust / Tokio / Axum

#### 优点

- 资源控制、内存安全和异步并发能力强。
- 可构建体积和运行时依赖较小的 Linux 服务。
- 适合对协议、延迟和资源有极高要求的基础设施。

#### 风险

- 异步类型、生命周期、数据库和应用框架组合的学习与实现成本最高。
- 业务规则、后台任务和快速迭代会承担比首版容量需求更高的复杂度。
- 与 C# 客户端形成两套完全不同的开发与调试体系。

#### 适配判断

对当前个人作品属于过度优化，不推荐作为首版服务端。

## 6. 综合比较

下表是实现前的工程判断，不是压力测试结果：

| 方案 | 实时适配 | 事务与数据 | 单人维护 | 部署复杂度 | 200 人余量 | 综合判断 |
|---|---|---|---|---|---|---|
| ASP.NET Core 10 | 高 | 高 | 高 | 低 | 充足，待实测 | 首选 |
| Go | 高 | 中高 | 中 | 低 | 充足，待实测 | 第二候选 |
| TypeScript / Node.js 24 LTS | 高 | 中高 | 中 | 中 | 充足，待实测 | 当前收益不足 |
| Rust / Tokio / Axum | 高 | 高 | 低 | 中 | 充足，待实测 | 对首版过重 |

## 7. 推荐方案

### 7.1 应用与实时通信

建议采用：

> C# + .NET 10 LTS + ASP.NET Core 模块化单体 + SignalR。

具体边界：

- HTTP API、SignalR Hub 和低频后台任务运行在同一个应用实例。
- 正式实时连接要求可用 WSS/WebSocket；首轮使用 SignalR JSON Hub Protocol。
- Hub 不包含房间业务规则，只把已验证的请求交给应用层，并广播应用层确认的结果。
- 活跃房间保存在进程内，每个房间串行裁定关键状态变化，数据库保存持久事实和休眠快照。
- 领域事件定义独立于 SignalR Hub 方法，带协议版本、事件序号和服务端时间。
- 不为 200 人提前引入 actor framework、独立游戏服务器、消息队列或微服务。

### 7.2 数据库与数据访问

建议采用：

> PostgreSQL 18 + EF Core 10 + Npgsql 10.x。

约束：

- .NET、EF Core 和 Npgsql 按兼容主版本成套升级，依赖使用确定版本，不使用漂移的 `latest`。
- PostgreSQL 固定主版本 18，但持续安装当前受支持的小版本安全和修复更新。
- 所有 Schema 变化通过可审查迁移执行；应用启动和普通请求不得偷偷创建或修改生产 Schema。
- 唯一约束、外键、检查约束和事务是业务正确性的一部分，不能只依赖客户端或 ORM 内存检查。
- 性能热点经测量后，可以在 Repository/持久化边界内使用参数化 SQL，不强制所有查询经过 LINQ。

### 7.3 部署入口

建议首版使用：

> Caddy + 应用容器 + PostgreSQL 容器，由 Docker Compose 管理。

理由：

- Caddy 默认自动申请、续期 TLS 证书并将 HTTP 重定向至 HTTPS，个人维护成本低。
- Compose 能用一个明确配置描述应用、数据库、网络和持久卷，适合单机开发、测试和首版部署。
- PostgreSQL 数据卷、Caddy 数据目录和备份目录必须持久化，容器重建不能删除业务数据或证书状态。
- 数据库只在 Compose 内部网络暴露；公网只开放反向代理所需端口。

Nginx 保留为成熟替代。如果后续云厂商负载均衡、运维经验或特殊代理需求更适合 Nginx，应通过部署设计调整，不影响应用协议。

### 7.4 暂不引入 Redis

单应用实例内，SignalR 连接和活跃房间均由同一进程管理，不需要 Redis backplane。官方扩容文档也说明，多实例时各进程无法自动获知其他实例连接，届时才需要 Redis 或其他明确的跨实例方案。

引入条件保持为：

- 需要两个及以上应用实例；或
- 单实例连接/内存/事件延迟实测达到已批准的扩容阈值；或
- 出现明确的跨实例在线状态和消息分发需求。

## 8. 建议模块边界

以下只是职责草案，不是已经创建的目录：

```text
Server.Api             HTTP、SignalR、会话入口和输入边界
Server.Application     用例编排、事务入口、权限与幂等
Server.Domain          房间、团子、关系和迁移规则
Server.Realtime        活跃房间、事件排序、快照和连接映射
Server.Persistence     EF Core、Npgsql、Repository 和迁移
Server.Operations      健康检查、后台任务、审计和运行指标
Protocol.Contracts     版本化请求、快照、事件和错误定义
```

依赖方向应让 Domain 不引用 ASP.NET Core、SignalR、EF Core 或具体数据库类型。`Protocol.Contracts` 不直接暴露数据库实体，也不能成为服务端内部对象的无筛选镜像。

## 9. Phase 2 前必须验证的 Spike

正式批准此栈后，仍需用最小同步原型验证：

1. 两个真实 .NET 客户端通过 WSS/SignalR 进入同一房间。
2. 快照、递增事件序号、移动、拖动、贴贴和断线重连能够工作。
3. 客户端重复请求、乱序事件和过期房间版本被正确处理。
4. 200 条长连接、50 个 4 人房间和若干 12 人房间的模拟负载。
5. 分别测量空闲与事件负载下的 CPU、内存、网络、P95 延迟和 GC 行为。
6. 应用重启后客户端从 PostgreSQL 快照恢复，不生成团子副本。
7. PostgreSQL 暂时不可用时应用明确失败并可恢复，不把未持久化结果伪装成成功。

未完成这些测试前，只能称为“技术栈已选”或“原型可运行”，不能声称满足 200 人容量或完整多人验收。

## 10. 版本与升级策略

- .NET 10：使用当前 10.0.x 补丁；最迟在 2028 年上半年形成后续受支持 LTS 的迁移计划。
- EF Core / Npgsql：主版本与 .NET 兼容，升级前检查 provider 兼容范围和迁移差异。
- PostgreSQL 18：持续安装 18.x 小版本；大版本升级必须先备份并完成恢复演练。
- 容器镜像：实施时固定明确版本和可追溯来源，不在生产环境使用无版本 `latest`。
- Caddy / Compose：跟踪安全更新，配置变更先在测试环境验证 WSS、证书续期和数据卷。

## 11. 官方资料

访问日期均为 2026-08-31：

- [.NET 与 .NET Core 支持政策](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- [ASP.NET Core SignalR 概述](https://learn.microsoft.com/en-us/aspnet/core/signalr/introduction?view=aspnetcore-10.0)
- [SignalR 支持的平台](https://learn.microsoft.com/en-us/aspnet/core/signalr/supported-platforms?view=aspnetcore-10.0)
- [SignalR 生产托管与扩容](https://learn.microsoft.com/en-us/aspnet/core/signalr/scale?view=aspnetcore-10.0)
- [EF Core 数据库 Provider](https://learn.microsoft.com/en-us/ef/core/providers/)
- [Npgsql EF Core Provider 文档](https://www.npgsql.org/efcore/)
- [NuGet：Npgsql.EntityFrameworkCore.PostgreSQL](https://www.nuget.org/packages/Npgsql.EntityFrameworkCore.PostgreSQL/)
- [PostgreSQL 版本支持政策](https://www.postgresql.org/support/versioning/)
- [Go Release Policy](https://go.dev/doc/devel/release)
- [Node.js 发布与 LTS](https://nodejs.org/en/about/previous-releases)
- [Caddy Automatic HTTPS](https://caddyserver.com/docs/automatic-https)
- [Docker Compose](https://docs.docker.com/compose/)

## 12. 批准结果

1. 已批准 C# + .NET 10 LTS + ASP.NET Core 作为首版服务端应用栈。
2. 已批准 SignalR + WSS/WebSocket + JSON 作为首轮实时传输，并保持 Dango 领域协议独立。
3. 已批准 PostgreSQL 18 + EF Core 10 + Npgsql 10.x 作为首版数据栈。
4. 已批准 Caddy + Docker Compose 作为单机部署首选，并继续把 Redis 延期到多实例阶段。
5. 已确认项目希望保留做大空间；房间分区、实例所有权和分阶段扩容约束见 ADR 0002。
