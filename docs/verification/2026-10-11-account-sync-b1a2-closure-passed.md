# B1a2 单次只读核对通过

日期：2026-10-11。用户批准后，从本地提交 `a2608cd11b6c67f142dda38e41389502865905b9` 按固定计划执行一次，运行器退出0。执行前工作区干净，清单、34源码／计划输入和3份实际PostgreSQL工具全部匹配；正式目录此前不存在。清单SHA256 `06960DA1241C18CA437AA3244230834600FB908D59706AEA7C6F6DD43909F372`。

## 实际结果

仅启动原B0隔离实例，读取原state与DPAPI凭据；固定1条只读查询、5个工具，业务写入0、迁移0，未重复注册、登录或客户端旅程。没有启动HTTPS、AppHost、证书、邮件、系统信任、WinCred或worker，也没有读取keys内容。

完整两流EOF与显式关闭后，先CreateNew保存原始stdout／stderr，再验证。实际PostgreSQL为18.6；数据库 `dango_backend_b0`、角色 `dango_b0_owner`、本机监听和原数据目录匹配，历史恰好为原七项。stderr为0字节，修复后的ASCII Unicode昵称条件已真实执行成功。

| 业务检查 | 实际值 |
| --- | ---: |
| users | 1 |
| validUsers | 1 |
| namedUsers | 1 |
| completeGraphs | 1 |
| pendingUnsentEmails | 1 |
| sessions | 4 |
| validSessions | 4 |
| revokedSessions | 4 |

上述检查严格绑定原B1a2 User、Pet、DefaultRoom、SourceOwner与RecoveryEpoch，证明昵称“新同步团子”／profile_version3、configuration_version6／eyeSize128、注册图和默认桌宠房完整、唯一邮件Pending且attempts0、四个会话均为正确代际且全部撤销。没有只看总表行数或借客户端Passed推断最终数据库结果。

固定工具顺序 version／status／start／scoped／stop；退出码分别0／3／0／0／0，status3表示执行前实例未运行。五工具身份、退出、Dispose及输出资源均确认；PG、CMD桥分别确认退出，pidfile和63237端口均消失。根实查记录的七个PID均不存在、固定端口无监听、pidfile不存在。数据、原凭据、密钥、代际及所有新旧证据保留。

证据目录：`C:\Users\fanh\AppData\Local\Dango\BackendDevelopment\run20261010accountsyncb1a2closure`。`resource-receipt.json` SHA256 `A6DC37056F013764BCB591B4F6D7B41ED56D1F3C2C3984E2EC9AA57926BE90FC`；`target-facts.json` SHA256 `5728F695F821F655C919F1E8BA151A75AD31DDCE546ABE093F74453E83EE8309`；空 `query-stderr.txt` SHA256 `E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855`。ClosurePassed=true，所有独立cleanup真值为true，RetryAllowed=false。

## 证据与后续边界

原B1a2旅程Passed、整个原运行Failed及其回执保留。本次独立closure补齐缺失的最终只读数据库证据，不把原Failed回执改写为Passed。结合原旅程、22表旧行保全、archive及收尾，登录／昵称／正常连续捏脸同步的技术主流程现已验证。

独立保存证据审计30/30通过，blockers为空。14项目标事实、七项历史、八项业务计数、固定五工具参数及输出／退出／Dispose／收尾规则均匹配；九份授权证据文件审计前后未变，34份源码与3份PG工具仍匹配执行时冻结。投影 `artifacts/closure-result-audit/bbeac030e8c04cbda0f76f69cd96f96e/independent-result-audit.json`，根已核对SHA256 `9E413C98387F78DE9D4DC537F1274BABD9B3F9AC82A840CC4D6FBE54FADABC1C`。该审计只读保存证据，实时资源检查由根另行完成。

此次单次额度已消耗，不能重跑、补SQL或扩为新的写入。下一步继续准备B1b异常恢复／多设备测试，门廊真实技术验证及用户主观验收仍未完成。未合并main、push、部署、安装、替换生产DLL或删除数据。
