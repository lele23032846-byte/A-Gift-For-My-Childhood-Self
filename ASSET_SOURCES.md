# 第三方与 AI 资产来源台账

本文件用于交接和发布前合规核对。不得在此记录账号密码、Token 或真实密钥。

## 已确认

| 资产 | 路径 | 来源/许可 | 状态 |
| --- | --- | --- | --- |
| Noto Serif SC 字体 | `Assets/Game/Art/Fonts/NotoSerifSC-Memory.ttf` | Google Noto；SIL Open Font License 1.1 | 许可文本位于同目录 `Noto-OFL.txt` |
| Liberation Sans / TMP 基础资源 | `Assets/TextMesh Pro` | Unity TMP Essential Resources；字体许可文本 `LiberationSans - OFL.txt` | 随项目保留许可文本 |

## 依据文件名可识别，但授权资料尚待负责人确认

| 资产类别 | 路径/示例 | 当前可确认信息 | 交接前需补充 |
| --- | --- | --- | --- |
| Tripo 生成模型与贴图 | `Assets/TripoAssets/**` | 目录名和部分文件名表明来自 Tripo；项目中未发现独立授权或导出记录 | 生成账号归属、生成日期、原始任务/下载链接、订阅或授权类型、商用与再分发范围 |
| Mixamo 动画 | `Assets/TripoAssets/MyBedroomDesk/Ani_Standard_Mixamo.fbx(1).fbx` | 文件名包含 Mixamo | 原始角色/动画来源、下载账号归属、使用条款、为何位于 Desk 目录 |
| 年幼角色模型 | `Assets/TripoAssets/MyYoungerSelf/tripo_convert_*.fbx` 等 | 文件名表明经过 Tripo 转换 | 原始输入资产来源、转换前文件、人物肖像/素材权利、生成与使用许可 |
| 视觉生成资源 | `Assets/Generated/MemoryLook`、`Assets/Generated/VisualPolish` | 项目内生成或加工；`MemoryLook/README.md` 描述了用途 | 生成工具/脚本、原始输入、是否可重复生成、最终权威文件列表 |

## 发布前检查

- [ ] 每个外部模型有来源链接或内部资产编号。
- [ ] 明确 Tripo/Mixamo 账号属于个人还是项目组织。
- [ ] 明确商业发布、修改和再分发权限。
- [ ] 保留原始下载文件或可恢复位置，但不提交账号凭证。
- [ ] AI 生成人物不存在未经授权的真实人物肖像或第三方受保护素材。
- [ ] 所有要求保留的许可证和署名文本均进入发布包或项目法律材料。
- [ ] 负责人签字确认授权状态后，再把“待确认”改为“已确认”。

