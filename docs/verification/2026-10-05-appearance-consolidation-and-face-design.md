# 色板整理与独立外观设计记录

日期：2026-10-05。状态：独立颜色模块整理完成，捏脸设计候选待用户评审。帽子按用户纠正移出捏脸，另行设计。当前没有生产捏脸界面、眼型／大小实现或首页保存接线。

## 1. 范围与来源保护

从 `main@e2f4037` 建立独立 `codex/appearance-face-design`；本轮仅复制旧色板四个生产模块、三个测试文件，新增整理说明、自身捏脸设计与交互示意、独立帽子选择设计。未修改既有 Home、Host、Runtime、native、解决方案、项目引用、设置或安装脚本。

四个生产模块、颜色测试和会话测试与旧源逐字节相同；存储测试增加六个非对象根／重复字段负例。旧 `codex/basic-appearance-palette@89bacb5` 的 HEAD、工作树状态、tracked diff 与七个源文件 SHA256 前后完全一致，旧未提交成果仍保留。[来源保护清单](../../artifacts/appearance-consolidation/preservation-and-results.json)与[原始清单](../../artifacts/appearance-consolidation/source-before.json)保存在本地隔离产物中；清单没有进入 Git。

## 2. 独立颜色模块实际验证

| 本轮检查 | 实际结果 | 证据与边界 |
|---|---|---|
| 新基线 Release 构建与三类聚焦测试 | 33/33，通过，0 失败，0 跳过 | [首次 TRX](../../artifacts/appearance-consolidation/results/appearance-focused.trx)；颜色值、存储和会话，不是全量桌面验收 |
| 错误实现试验 | 31 通过、2 失败 | 临时允许未来 Schema 3，严格读取／保护反例实际抓住错误；[失败 TRX](../../artifacts/appearance-consolidation/results/mutation-future-schema.trx) |
| 恢复代码后重建复跑 | 33/33，通过，0 失败，0 跳过 | Store 原字节／SHA 已恢复；[最终 TRX](../../artifacts/appearance-consolidation/results/appearance-restored.trx)与[恢复记录](../../artifacts/appearance-consolidation/mutation.json) |

旧外观小样的 72/72 及两项颜色人工反馈没有被当作本轮新验证。此次未运行全量 Desktop／Physics，不关闭旧 DLL 缺导出、历史比较库缺失或其他旧门禁。新类尚未在产品入口实例化，不把测试的临时文件当作用户已能保存／重启恢复。

## 3. 捏脸示意实际检查

[方案](../planning/2026-10-05-dango-face-design.md)与[示意源](../prototypes/dango-face-design.html)仅设计团子自身：身体／眼睛 RGB、三种候选基础眼型与三档候选大小。没有帽子控制、绘制上传、嘴巴、眼距或物理 DIY。

浏览器检查 **62 项通过、0 失败、0 JavaScript 页面错误**，见[检查结果](../../artifacts/face-design/verification.json)。实际检查了黑色与同色、页签保留草稿、九组眼型／大小、眨眼恢复、睡眠／醒来、整套取消、恢复默认、模拟应用失败／重试、聊天示意状态恢复及键盘切换。1024／736／320 像素视口的控件无横向溢出或截断，截图已读取检查。

检查脚本首次把“恢复默认已等于已应用值”误当成仍需应用，实际按钮正确禁用；修正脚本为验证无 dirty 状态后重新检查通过，没有修改产品行为或降低条件。

这些是页面示意检查，不是三种 Windows DPI、真实 WPF/native、安全脸部求解、磁盘外观保存、重启、多人或用户人工验收。示意只用局部页面状态比较草稿和已应用值，不写真实外观文件；设计面板中的失败情形只作流程比较。

## 4. 独立帽子设计

[帽子选择设计](../planning/2026-10-05-hat-selection-design.md)作为单独候选，沿用三种已有预设和单帽位，不加入捏脸示意。只读复核现有预设、帧绑定、主设置帽字段及自动保存路径；当前 `TryApplyHatPreset` 发变更事件后会触发 Host 延迟保存，未来帽子草稿不能直接调用这一提交 API。

本轮没有帽子实现、帽子原型或帽子新增自动／实时验收。独立设计明确候选草稿、取消、显式应用、自动保存隔离、部分写盘成功与帧绑定恢复；具体界面与实现合同仍待评审。

## 5. Git 与环境交付

本轮代码整理提交为 `b01909f`，设计文档另存本地提交，实际哈希以 `git log` 为准；没有合入 main、推送、部署或更新日常安装。捏脸与帽子候选尚未获人工确认，未标阶段已验收。

没有启动产品、服务或数据库，没有本轮监听端口、账号或网络数据。聚焦测试使用隔离 artifacts 目录；测试进程和浏览器检查均已结束，产物保留。旧颜色源树、原产品进程、日常设置、native 产物与安装目录不变。

下一步是评审自身捏脸交互及候选眼型／大小；真实实现需单独计划，在普通房接口稳定后由单一负责人接线。帽子属于另一部分，不自动由本轮设计扩展。
