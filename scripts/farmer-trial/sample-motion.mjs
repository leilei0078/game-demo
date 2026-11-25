import fs from 'node:fs';
import {Object3D,Vector3,Quaternion,Matrix4,VectorKeyframeTrack,QuaternionKeyframeTrack,InterpolateDiscrete,InterpolateLinear} from 'three';

const [source,blob,output]=process.argv.slice(2);
const chopping=blob==='--tree-chop';
const input=fs.readFileSync(source),jsonEnd=chopping?20+input.readUInt32LE(12):0;
const asset=JSON.parse(chopping?input.subarray(20,jsonEnd):input), binary=chopping?input.subarray(jsonEnd+8):fs.readFileSync(blob);
const objects=asset.nodes.map(node=>{
  const object=new Object3D(); object.name=node.name;
  if(node.translation)object.position.fromArray(node.translation);
  if(node.rotation)object.quaternion.fromArray(node.rotation);
  if(node.scale)object.scale.fromArray(node.scale);
  return object;
});
asset.nodes.forEach((node,index)=>node.children?.forEach(child=>objects[index].add(objects[child])));
const roots=asset.scenes[0].nodes.map(index=>objects[index]);
const original=objects.map(object=>[object.position.clone(),object.quaternion.clone(),object.scale.clone()]);
const mapping=[['Pelvis','hips'],['Spine','spine.001'],['Spine2','spine.002'],['Spine3','spine.003'],['Neck','neck'],['Head','head']];
for(const side of ['L','R'])for(const [target,src] of [['Clavicle','shoulder'],['UpperArm','upper_arm'],['Forearm','forearm'],['Hand','hand'],['Thigh','thigh'],['Calf','shin'],['Foot','foot'],['Toe0','toe']])mapping.push([side+' '+target,src+'.'+side]);
const chopNames=['pelvis','spine_01','spine_02','spine_03','neck_01','Head'];
if(chopping){
  for(const side of ['l','r'])for(const joint of ['clavicle','upperarm','lowerarm','hand','thigh','calf','foot','ball'])chopNames.push(joint+'_'+side);
  for(const side of ['L','R'])for(const [finger,alias] of [['Index','1'],['Middle','2'],['Ring','3'],['Pinky','4'],['Thumb','0']])for(let joint=1;joint<=3;joint++){
    const prefix=side==='L'?'Left':'Right';
    mapping.push([joint===1&&['1','2','4'].includes(alias)?side+' Finger'+alias:'mixamorig:'+prefix+'Hand'+finger+joint]);
    chopNames.push(finger.toLowerCase()+'_0'+joint+'_'+side.toLowerCase());
  }
}
const bones=mapping.map(([,name],index)=>objects.find(object=>object.name===(chopping?chopNames[index]:'DEF-'+name)));
const pos=index=>bones[index].getWorldPosition(new Vector3());
const right=pos(18).sub(pos(10)).setY(0).normalize();
const front=pos(13).sub(pos(12)).setY(0).normalize();
const basis=new Matrix4().makeBasis(right,new Vector3(0,1,0),front.addScaledVector(right,-front.dot(right)).normalize()).invert();
const inv=basis.clone().invert();
function pose(){
  roots.forEach(root=>root.updateMatrixWorld(true));
  const values=[];
  for(const bone of bones){
    const point=bone.getWorldPosition(new Vector3()).applyMatrix4(basis);
    const rot=new Matrix4().makeRotationFromQuaternion(bone.getWorldQuaternion(new Quaternion()));
    const orient=new Quaternion().setFromRotationMatrix(basis.clone().multiply(rot).multiply(inv));
    values.push(...point.toArray(),...orient.toArray());
  }return values.map(value=>+value.toFixed(6));
}
function floats(index){const access=asset.accessors[index],view=asset.bufferViews[access.bufferView];if(access.componentType!==5126||view.byteStride)throw Error('Unsupported accessor');return new Float32Array(binary.buffer,binary.byteOffset+(view.byteOffset||0)+(access.byteOffset||0),access.count*({SCALAR:1,VEC3:3,VEC4:4}[access.type]));}
function clip(name){
  objects.forEach((object,index)=>{object.position.copy(original[index][0]);object.quaternion.copy(original[index][1]);object.scale.copy(original[index][2]);});
  const animation=asset.animations.find(item=>item.name===name);
  const tracks=animation.channels.map(channel=>{const sampler=animation.samplers[channel.sampler];if(sampler.interpolation && !['LINEAR','STEP'].includes(sampler.interpolation))throw Error(sampler.interpolation);
    const dest={rotation:'quaternion',translation:'position',scale:'scale'}[channel.target.path],type=dest==='quaternion'?QuaternionKeyframeTrack:VectorKeyframeTrack;
    const times=floats(sampler.input);return [objects[channel.target.node][dest],new type(dest,times,floats(sampler.output),sampler.interpolation==='STEP'?InterpolateDiscrete:InterpolateLinear).createInterpolant(),times.at(-1)];});
  const duration=Math.max(...tracks.map(track=>track[2])),count=Math.round(duration*36),data=[];
  for(let frame=0;frame<count;frame++){for(const [dest,interpolant] of tracks)dest.fromArray(interpolant.evaluate(frame*duration/count));data.push(...pose());}
  return {name,duration,count,data};
}
fs.writeFileSync(output,JSON.stringify({names:mapping.map(pair=>pair[0]),rest:pose(),...(chopping?{chop:clip('TreeChopping_Loop')}:{walk:clip('Walk_Loop'),idle:clip('Idle_Loop')})}));
