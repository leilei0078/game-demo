# 树木风格研究资产

面向 PC 与中端手机的五款树形研究。新增 SoftPine 柔冠童话松，参考用户提供图片的厚实、圆润、黄绿色层叠树冠方向，采用原创网格。前四款及原有密林场景保留。所有模型为真实网格、UV 和 PNG 贴图，没有依赖网页专用程序材质。

## 导入 Unity

推荐直接导入 `TreeStudies.unitypackage`，也可以将此压缩包中的 `Assets/TreeStudies` 复制到已有项目的 `Assets/`。

1. Built-in 项目可直接拖入 `Assets/TreeStudies/Prefabs/` 中的五个 Prefab；示例场景为 `Scenes/TreeComparison.unity`。
2. **URP 项目必须先执行** `Tools > Tree Studies > Build Prefabs and Materials`。工具会用 URP/Lit 重建材质，设置双面、Alpha Clip、贴图及实例化。示例场景的地面和灯光按 Built-in 制作，URP 请优先把树木 Prefab 放到自己的场景。
3. 执行 `Tools > Tree Studies > Validate Prefabs` 检查 LOD、比例、原点、材质和碰撞体。
4. 本机已在 Unity 6000.3.27f1 Built-in 实际导入、运行生成工具并渲染验证；URP 路径已提供，未在 URP 项目里运行。HDRP 需自行转换材质。

资产根节点单位为米，比例为 1，最低点为 y=0。Blender 源文件为 Z 向上，FBX/GLB 导出为引擎使用的 Y 向上。FBX 含三级 LOD，生成工具创建一个统一的 LODGroup，避免多个层级同时显示。

| 模型 | LOD0 三角面 | LOD1 | LOD2 | 约高 |
|---|---:|---:|---:|---:|
| FacetedFir 块面冷杉 | 2,148 | 1,464 | 636 | 8.44 m |
| PaintedFir 团簇冷杉 | 4,572 | 2,676 | 2,012 | 8.60 m |
| NaturalPine 贴片自然松 | 2,560 | 1,776 | 1,142 | 8.10 m |
| OldOak 团簇老橡树 | 3,132 | 1,836 | 814 | 7.85 m |
| SoftPine 柔冠童话松 | 3,720 | 1,932 | 852 | 8.42 m |

SoftPine 使用闭合的圆润树冠，每层具有不规则的五瓣垂边。每级为 2 个网格、2 个材质，独立共享 2 张 512² 贴图（暖色树皮与黄绿树冠），全资产共 5 张 512² 贴图。新款 100 棵树在全 LOD0 时为 37.2 万三角面，全 LOD2 时为 8.52 万，未计阴影或其他场景内容；自动 LOD 按屏幕占比混用不同级别。

每树每级 LOD 为 **2 个网格 / 2 个材质**，其中包含根系与枝条，三角面统计来自重新读取的导出文件，并在 Unity 再次核对。每树的原点为树干底部。树干使用一个简化 CapsuleCollider，树冠不参与碰撞；倾斜大枝条如需精确碰撞，应按玩法补充简化碰撞体。

LOD 阈值按屏幕占比 .30 / .13 / .025，低于 .025 剔除。默认硬切换，避免移动端 LOD 交叠绘制；可在自己的 shader 支持 CrossFade 后启用渐变。仅 LOD0 投射阴影，LOD1/2 仍接收阴影。物体变大或 FOV 变化时，LOD 触发距离也会变化。

## 性能使用建议

- **大面积森林优先块面冷杉**，或以团簇冷杉作近景、低细节版本作远景。贴片自然松面数较低，但需要计入 Alpha Test、双面和透明区域重叠带来的像素成本。
- 五种共享材质均启用 GPU Instancing。Built-in 用同网格同材质实例；URP 的 SRP Batcher 与传统实例化并不保证同时生效，请用 Frame Debugger 验证。大量树建议用 Terrain、GPU Resident Drawer / BatchRendererGroup 或分区的 RenderMeshInstanced，依项目管线选择。
- 实例化主要减少 CPU 提交和 draw call，不会消除三角面处理、阴影和叶片填充开销。按空间分区合批可保留有效剔除，避免整片森林一个巨大的不可细分批次。
- 三张共享 **512×512 PNG**：树皮、四分区树冠色图、带 Alpha 的针叶。工具打开 mipmaps、Trilinear、4× AF；Android 默认 ASTC 6×6。较旧 Android 设备可改用 ETC2；不支持 ASTC 的设备需按目标构建配置回退。
- 理论上三张完整 mip 链的 RGBA32 贴图共约 **4 MiB**，ASTC 6×6 共约 **0.46 MiB**；实际常驻内存取决于 Unity/平台格式、CPU 可读副本和驱动，不能将 PNG 文件大小视作显存大小。Read/Write 默认关闭。
- 控制阴影距离、近景投影树数量、相机可见树数。手机先从 20–35 m 阴影距离、1–2 cascades 开始测，再依据设备调优。远处可继续采用烘焙 impostor，本包暂未提供 billboard/impostor。
- 不包含风动画、季节变化或交互破坏系统。FBX 保持两个共享材质可扩展风动 shader；新增 Shader Graph 前应重新测量 draw call 和移动端填充成本。

## 验证与接受的建模提示

Blender 的 15 个 LOD 全部通过几何检查，另有材质和线框的四角度检查图。没有悬空、反向面、退化面或共面闪烁错误。

接受的警告仅为：**UV0 有意重叠复用树皮与叶片图集**；**少量独立枝条的封口中心/连接顶点同位，但拓扑仍分开**，保留封闭枝条可避免错误焊接为非流形接头。没有重复共面表面。若要烘焙光照，需生成独立 UV1；本包不带 lightmap，也未开启自动 UV1。

`output/tree-studies/qa.json`、`unity-audit.json`、`web/audit.json` 为工作区完整验证记录，压缩包中附副本。

网页 `/trees.html` 提供五树对比、单树旋转、线框、自动/手动 LOD 和 25/100/300 棵分区实例化森林。页面统计为**主相机渲染**的 tris / draws，阴影渲染另计；FPS 是本机浏览器值，**未进行手机真机性能测量**。

Blender 文件为 `Source/TreeStudies.blend`（原四款）及 `Source/SoftPine.blend`（参考图新款），纹理均已打包；生成脚本为 `Source/create_tree_studies.py` 与 `Source/create_soft_pine.py`。源脚本的 QA 工具路径指向作者本机技能目录，异机重跑需替换路径或安装对应 QA 工具；现成 FBX、GLB 和 .blend 无此依赖。
