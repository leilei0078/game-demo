"""Package only tested runtime assets and reusable source, excluding Unity cache."""
from pathlib import Path
import zipfile,json,hashlib
r=Path(__file__).resolve().parents[1];out=r/'output/leafy-trees';assets=r/'UnityTreeStudies/Assets/LeafyTrees';downloads=r/'public/downloads'
m=json.loads((r/'public/models/leafy-trees/manifest.json').read_text());q=json.loads((out/'qa.json').read_text());w=json.loads((out/'web/audit.json').read_text())
assert all(a['pass'] for a in q.values()) and w['passed']
rows='\n'.join('|'+s['title']+'|'+'|'.join(str(l['triangles']) for l in s['lods'])+'|' for s in m['styles'])
text='''# Leafy Trees · 丰叶森林

三款参考图三方向的实体叶簇树，保留此前所有版本。不是参考图的逐叶复刻。

## 使用

将 Assets/LeafyTrees 目录复制到 Unity 项目的 Assets 下，等待脚本和模型导入，然后执行 Tools > Leafy Trees > Build Prefabs and Materials，生成三级 LODGroup、共享材质、树干 CapsuleCollider 和预制体。FBX 包含完整三级 LOD，单位为米，根部坐标为零。当前本机 Unity Licensing Client 连接失败，未完成新版 Unity 编辑器内导入验证，未提供已验证的预制体或 unitypackage。Built-in Standard 与 URP Lit 材质配置已提供，需在目标项目执行生成；HDRP 需自行适配。

## 三角面预算

|资产|LOD0|LOD1|LOD2|
|---|---:|---:|---:|
'''+rows+'''

每个 LOD 两个网格、两个共享不透明材质；树皮 512²、叶片纯色色图 256²。所有叶片与叶簇共用同一种底色，无渐变与随机色差，明暗来自几何法线、光照和阴影。近景封闭实体小叶，中景减少叶片，远景仅保留叶簇体块。Web 采用分区 GPU 实例化、屏幕尺寸 LOD 与视锥剔除，仅近景投影。Unity 包提供实例化材质与 LODGroup，实际批次还受渲染管线、光照与放置方式影响。

不要把所有树锁到 LOD0。手机上优先缩短阴影距离与 LOD0 距离，再检查 GPU 像素开销与 CPU 提交。网页 100/300 树是桌面浏览器功能测试，不代表真机手机帧率。

## 检查与限制

9 个 Blender 几何检查通过，Web 的旋转、LOD、线框、100/300 森林与手机视口均已检查。无动画、树叶风动或烘焙光照图。UV0 故意叠放色板与树皮，不能用于光照图烘焙；分叉处独立闭合枝段保留少量位置重合的端盖顶点，避免错误合并拓扑。枝段和叶片有接触/嵌入，属于有意组装结构。
'''
(assets/'README.zh-CN.md').write_text(text)
files={}
for p in assets.rglob('*'):
 if p.is_file():files['Assets/LeafyTrees/'+str(p.relative_to(assets))]=p
for n in ['LeafyTrees.blend','qa.json','web/audit.json','web/comparison.png']:
 files['Source/'+n]=out/n
for n in ['create_leafy_trees.py','create_tree_studies.py','flatten_leafy_color.py']:
 files['Source/'+n]=r/'scripts'/n
files['Web/LeafyTrees.glb']=r/'public/models/leafy-trees/LeafyTrees.glb'
path=downloads/'LeafyTrees-Source.zip'
with zipfile.ZipFile(path,'w',zipfile.ZIP_DEFLATED) as z:
 for name,p in files.items():assert p.stat().st_size>0;z.write(p,name)
with zipfile.ZipFile(path) as z:assert z.testzip() is None
print(path,path.stat().st_size)
(out/'delivery.json').write_text(json.dumps({'zipBytes':path.stat().st_size,'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'blenderAndWebPassed':True,'unityValidated':False},indent=2))
