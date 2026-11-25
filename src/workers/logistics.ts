import type {Kind,Job} from './sim.ts';

export type Logistics={maxItems:number;maxWeight:number;walkSpeed:number;gatherRate:number;unloadPerItem:number;weights:Record<Kind,number>};
export const DEFAULT_LOGISTICS:Logistics={maxItems:6,maxWeight:18,walkSpeed:2.9,gatherRate:1,unloadPerItem:.1,weights:{wood:2,ore:3,food:.5}};
export const SECONDS_PER_ITEM:Record<Job,number>={chop:2.2/6,mine:.55,gather:.25,pickup:.65/6};
export function validateLogistics(p:Logistics){
 const values=[p.maxItems,p.maxWeight,p.walkSpeed,p.gatherRate,p.unloadPerItem,...Object.values(p.weights)];
 if(!values.every(Number.isFinite))return '参数必须是有效数字';
 if(!Number.isInteger(p.maxItems)||p.maxItems<1||p.maxItems>24)return '每趟件数须为 1–24 的整数';
 if(p.maxWeight<1||p.maxWeight>120)return '载重上限须为 1–120 kg';
 if(p.walkSpeed<.5||p.walkSpeed>6)return '空载速度须为 0.5–6 m/s';
 if(p.gatherRate<.25||p.gatherRate>4)return '采集效率须为 0.25–4 倍';
 if(p.unloadPerItem<.02||p.unloadPerItem>2)return '每件卸货耗时须为 0.02–2 秒';
 if(Object.values(p.weights).some(v=>v<.1||v>60))return '单件重量须为 0.1–60 kg';
 if(Object.values(p.weights).some(v=>v>p.maxWeight))return '载重上限必须至少容纳一件最重的资源';
 return '';
}
export function batchSize(p:Logistics,kind:Kind,remaining:number){return Math.max(0,Math.min(Math.floor(remaining),p.maxItems,Math.floor((p.maxWeight+1e-8)/p.weights[kind])));}
export function movementSpeed(p:Logistics,weight:number){return p.walkSpeed*(1-.3*Math.min(1,Math.max(0,weight)/p.maxWeight));}
export function gatherSeconds(p:Logistics,job:Job,amount:number){return SECONDS_PER_ITEM[job]*amount/p.gatherRate;}
export function unloadSeconds(p:Logistics,amount:number){return .3+p.unloadPerItem*amount;}
