"""Package only deliverables, never Unity Library/Temp/Logs."""
import json, zipfile, hashlib
from pathlib import Path

root=Path(__file__).resolve().parents[1]
package=root/'UnityTreeStudies'
output=root/'output/tree-studies'
downloads=root/'public/downloads'
downloads.mkdir(parents=True,exist_ok=True)
manifest=json.loads((root/'public/models/trees/manifest.json').read_text())
unity=json.loads((output/'unity-audit.json').read_text())
assert unity['passed']
for asset,style in zip(unity['assets'],manifest['styles']):
    assert asset['name']==style['id']
    assert [l['triangles'] for l in asset['lods']]==[l['triangles'] for l in style['lods']]
qa=json.loads((output/'qa.json').read_text())
assert len(qa)==len(manifest['styles'])*3 and all(r['pass'] for r in qa.values())
web=json.loads((output/'web/audit.json').read_text())
assert web['passed']
files={}
for path in (package/'Assets/TreeStudies').rglob('*'):
    if path.is_file():files['UnityTreeStudies/'+str(path.relative_to(package))]=path
files['UnityTreeStudies/README.zh-CN.md']=package/'README.zh-CN.md'
files['UnityTreeStudies/Source/TreeStudies.blend']=output/'TreeStudies.blend'
files['UnityTreeStudies/Source/create_tree_studies.py']=root/'scripts/create_tree_studies.py'
files['UnityTreeStudies/Source/create_soft_pine.py']=root/'scripts/create_soft_pine.py'
files['UnityTreeStudies/Source/SoftPine.blend']=output/'SoftPine.blend'
files['UnityTreeStudies/Web/TreeStudies.glb']=root/'public/models/trees/TreeStudies.glb'
for name in ['qa.json','unity-audit.json','unity-preview.png','web/audit.json','web/comparison.png']:
    files['UnityTreeStudies/Validation/'+name]=output/name
path=downloads/'UnityTreeStudies.zip'
with zipfile.ZipFile(path,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=6) as archive:
    for name,source in sorted(files.items()):
        assert source.stat().st_size>0
        archive.write(source,name)
with zipfile.ZipFile(path) as archive:assert archive.testzip() is None
result={'zip':{'bytes':path.stat().st_size,'files':len(files),'sha256':hashlib.sha256(path.read_bytes()).hexdigest()},'unitypackage':{'bytes':(downloads/'TreeStudies.unitypackage').stat().st_size},'webBundleBytes':manifest['bundle']['bytes'],'allChecksPassed':True}
(output/'delivery.json').write_text(json.dumps(result,indent=2))
print(json.dumps(result,indent=2))
