# 媒体来源

`room-whole.png` 与 `room-near.png` 是 2026-10-30 使用本仓库实际产品 room scene 离屏生成的 960 × 600 PNG。来源：`eng/tools/Dango.LocalRoomProbe/PublicPreviewCapture.cs` 的 `public-preview` 模式；使用 `BeltRecoveryVerification.cs` 中的隐藏渲染 rig、产品 WPF 场景、真实 Belt native driver 和虚构房间数据。两图没有裁剪、拼接、AI 补画或读取屏幕像素。

本次基线 HEAD 为 `9fbb828dcd37bb8c174f0c197a7953f2579d7dad`，另加开源准备分支中的生成工具；native DLL SHA-256 为 `EA18F2BFCE6D2F6C733E3F55D11E223353F5060F0D2FAB131224F810CC17B31A`。完整生成证据保留在专用 run；机器绝对路径不写入此公开说明。

原始 PNG SHA-256：

| 文件 | SHA-256 |
| --- | --- |
| `room-whole.png` | `628295777BB88D3FDA88228451471B2A0514006DCBD5DD9704BF9B2064757EB3` |
| `room-near.png` | `C0F9617F58EC003B9B2A684EAEE58F403183C6FC8B4AAE4DF2D0DD234348E0A5` |

团子外观参数为虚构示例，家具为项目产品内的平台和球，未导入外部饰品或真实用户资料。原始项目素材来源及作者再分发确认见 [../assets.md](../assets.md)，项目生成画面按仓库根目录 MIT 许可证分发。第三方组件许可仍独立适用，见 [../../../THIRD-PARTY-NOTICES.md](../../../THIRD-PARTY-NOTICES.md)。

展示时请保留“真实产品场景离屏渲染示例”的说明；不得把这些图片描述为完整应用截图、多人同步证明或桌面输入验收。复现方法和验证边界见 [../demo.md](../demo.md)。
