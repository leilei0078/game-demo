import {DEFAULT_LOGISTICS,batchSize,movementSpeed,gatherSeconds,unloadSeconds,validateLogistics,type Logistics} from './logistics.ts';
export type Point = { x: number; z: number };
export type Kind = 'wood' | 'ore' | 'food';
export type Phase = 'idle' | 'move' | 'approach' | 'waitWork' | 'work' | 'deliver' | 'waitDepot' | 'unload' | 'buildApproach' | 'build' | 'blocked';
export type Source = Point & { id: number; name: string; kind: Kind; activity: 'chop' | 'mine' | 'gather' | 'pickup'; stock: number; initial: number; radius: number };
export type Order = { type: 'move'; point: Point } | { type: 'work'; source: number } | { type: 'transport' };
export type Worker = Point & {
  id: number; phase: Phase; source: number | null; goal: Point | null; path: Point[];
  cargo: { kind: Kind; amount: number; unitWeight?:number } | null; progress: number; face: number; moving: boolean;
  slot: number | null; bay: number | null; queuedAt: number; queue: Order[];
  vx: number; vz: number; stuck: number;
  continueJob: boolean;
  finishAfterDelivery: boolean; lastSource: number | null;
  blockedPhase?: Phase; routeRetries?: number; retryIn?: number; noProgress?: number; bestDistance?: number;
  routeIssue?: 'unreachable'|'congestion'; waitSince?: number;
  batch?: {amount:number;seconds:number;unitWeight:number}; unloadDuration?:number;
};
export type Message = { worker: number; text: string };
export type Job = Source['activity'];
export type WorkerState = 'idle' | 'move' | 'work' | 'haul' | 'wait';
export function workerState(w: Worker): WorkerState {
  if(w.phase==='blocked')return 'wait';
  if (w.phase === 'idle') return 'idle';
  if ((w.phase === 'waitWork' || w.phase === 'waitDepot') && !w.path.length) return 'wait';
  if (w.cargo || w.phase === 'unload') return 'haul';
  return w.phase === 'work' || w.phase === 'build' ? 'work' : 'move';
}
export const DEPOT = { x: -6, z: -2, halfX: 1.5, halfZ: 1.05 };
export const LIMIT = { x: 10.5, z: 6.5 };
export const RADIUS = .28;
export const distance = (a: Point, b: Point) => Math.hypot(a.x - b.x, a.z - b.z);
export const CAPACITY = 6;
export const ACTIVITY = { chop: '砍伐', mine: '采矿', gather: '采集', pickup: '装货' };
export const KIND = { wood: '木材', ore: '矿石', food: '果实' };
export const home = (id: number): Point => ({ x: -8.7 + (id - 1) % 4 * 1.3, z: 2.5 + Math.floor((id - 1) / 4) * 1.3 });
export const BAYS: Point[] = [-.95, 0, .95].map(dx => ({ x: DEPOT.x + dx, z: DEPOT.z + DEPOT.halfZ + .8 }));
export const workSpot = (s: Source, slot: number): Point => {
  const angle = slot * Math.PI / 2 + .35, r = s.radius + .8;
  return { x: s.x + Math.sin(angle) * r, z: s.z + Math.cos(angle) * r };
};
const WALKING: Phase[] = ['move', 'approach', 'deliver', 'waitWork', 'waitDepot'];
export class Sandbox {
  time = 0;
  paused = false;
  inventory: Record<Kind, number> = { wood: 0, ore: 0, food: 0 };
  messages: Message[] = [];
  collectionStats={orders:0,delivered:0,stopped:0,finished:0,resumed:0};
  private serial = 0;
  private logistics:Logistics=structuredClone(DEFAULT_LOGISTICS);
  get tuning():Logistics{return structuredClone(this.logistics);}
  configureLogistics(next:Logistics){
    const error=validateLogistics(next);if(error)return error;
    if(this.paused)return '先继续游戏再调整参数';
    this.logistics=structuredClone(next);return '';
  }
  cargoWeight(w:Worker){return w.cargo?w.cargo.amount*(w.cargo.unitWeight??this.logistics.weights[w.cargo.kind]):0;}
  travelSpeed(w:Worker){return movementSpeed(this.logistics,this.cargoWeight(w));}
  batchPlan(s:Source){const amount=batchSize(this.logistics,s.kind,s.stock);return{amount,weight:amount*this.logistics.weights[s.kind],seconds:gatherSeconds(this.logistics,s.activity,amount),speed:movementSpeed(this.logistics,amount*this.logistics.weights[s.kind])};}
  get deliveredPerMinute(){return this.time?this.collectionStats.delivered/this.time*60:0;}

  workers: Worker[] = Array.from({ length: 12 }, (_, i) => ({ id: i + 1, ...home(i + 1), phase: 'idle', source: null, goal: null, path: [], cargo: null, progress: 0, face: 0, moving: false, slot: null, bay: null, queuedAt: 0, queue: [], vx: 0, vz: 0, stuck: 0, continueJob: false, finishAfterDelivery:false,lastSource:null }));
  sources: Source[] = [
    { id: 101, x: 3.2, z: -3, radius: .8, name: '树木 A', kind: 'wood', activity: 'chop', stock: 144, initial: 144 },
    { id: 102, x: 7, z: -1.5, radius: .8, name: '树木 B', kind: 'wood', activity: 'chop', stock: 108, initial: 108 },
    { id: 103, x: 6.2, z: 3.7, radius: 1.0, name: '矿石', kind: 'ore', activity: 'mine', stock: 144, initial: 144 },
    { id: 104, x: -.8, z: -3.8, radius: .65, name: '果丛', kind: 'food', activity: 'gather', stock: 90, initial: 90 },
    { id: 105, x: .7, z: 3.5, radius: .65, name: '待运木箱', kind: 'wood', activity: 'pickup', stock: 72, initial: 72 },
  ];
  say(w: Worker, text: string) { this.messages.push({ worker: w.id, text }); }
  blocked(p: Point, padding = RADIUS + .04) {
    if (Math.abs(p.x) > LIMIT.x || Math.abs(p.z) > LIMIT.z) return true;
    if (Math.abs(p.x - DEPOT.x) <= DEPOT.halfX + padding && Math.abs(p.z - DEPOT.z) <= DEPOT.halfZ + padding) return true;
    return this.sources.some(s => s.stock > 0 && distance(p, s) < s.radius + padding);
  }
  private segment(a: Point, b: Point, padding = .34) {
    const n = Math.max(1, Math.ceil(distance(a, b) / .15));
    for (let i = 1; i <= n; i++) if (this.blocked({ x: a.x + (b.x - a.x) * i / n, z: a.z + (b.z - a.z) * i / n }, padding)) return false;
    return true;
  }
  free(p: Point): Point | null {
    if (!this.blocked(p, .38)) return { ...p };
    for (let r = .4; r <= 4; r += .3) for (let a = 0; a < 32; a++) {
      const q = { x: p.x + Math.cos(a * Math.PI / 16) * r, z: p.z + Math.sin(a * Math.PI / 16) * r };
      if (!this.blocked(q, .38)) return q;
    }
    return null;
  }
  // Reserve separated destinations as well as work/depot positions.
  private vacant(p: Point, w: Worker, reserved: Point[] = []): Point {
    const occupied = [...reserved, ...this.workers.filter(o => o !== w).map(o => o.goal ?? o)];
    for (let r = 0; r <= 5; r += .35) for (let a = 0; a < (r === 0 ? 1 : 32); a++) {
      const q = { x: p.x + Math.cos(a * Math.PI / 16) * r, z: p.z + Math.sin(a * Math.PI / 16) * r };
      if (!this.blocked(q, .4) && occupied.every(o => distance(o, q) > .78)) return q;
    }
    return this.free(p) ?? { x: w.x, z: w.z };
  }
  route(from: Point, to: Point, avoid: Point[] = []): Point[] {
    const n = 45, m = 29, step = .5;
    const point = (i: number) => ({ x: (i % n - 22) * step, z: (Math.floor(i / n) - 14) * step });
    const cell = (p: Point) => (Math.round(p.z / step) + 14) * n + Math.round(p.x / step) + 22;
    const blocked = (p: Point) => this.blocked(p, .37) || avoid.some(o => distance(o, p) < .61 && distance(p, from) > .5 && distance(p, to) > .7);
    const start = cell(from);
    let end = -1, score = Infinity;
    for (let z = 0; z < m; z++) for (let x = 0; x < n; x++) {
      const i = z * n + x, p = point(i), d = distance(to, p);
      if (d < score && !blocked(p)) { end = i; score = d; }
    }
    if (end < 0) return [];
    if (end === start) return this.segment(from, to) ? [{ ...to }] : [point(end)];
    const open = new Set([start]), closed = new Set<number>();
    const costs = new Map([[start, 0]]), previous = new Map<number, number>();
    while (open.size) {
      let current = -1, best = Infinity;
      for (const i of open) { const f = costs.get(i)! + distance(point(i), point(end)); if (f < best) { best = f; current = i; } }
      if (current === end) {
        const raw: Point[] = [];
        for (let i = current; i !== start; i = previous.get(i)!) raw.unshift(point(i));
        if (this.segment(point(end), to)) raw.push({ ...to });
        const path: Point[] = [];
        let origin = from;
        while (raw.length) {
          let last = 0;
          for (let j = 1; j < raw.length; j++) {
            if (!this.segment(origin, raw[j]) || avoid.some(o => segmentDistance(o, origin, raw[j]) < .61)) break;
            last = j;
          }
          origin = raw[last]; path.push(origin); raw.splice(0, last + 1);
        }
        return path;
      }
      open.delete(current); closed.add(current);
      const p = point(current);
      for (const [dx, dz] of DIRECTIONS) {
        const q = { x: p.x + dx * step, z: p.z + dz * step }, i = cell(q);
        if (closed.has(i) || blocked(q)) continue;
        if (dx && dz && (blocked({ x: q.x, z: p.z }) || blocked({ x: p.x, z: q.z }))) continue;
        const cost = costs.get(current)! + step * Math.hypot(dx, dz);
        if (cost < (costs.get(i) ?? Infinity)) { costs.set(i, cost); previous.set(i, current); open.add(i); }
      }
    }
    return [];
  }
  protected go(w: Worker, target: Point, phase: Phase) {
    const destination = this.free(target);
    w.batch=undefined;w.unloadDuration=undefined;
    w.phase = phase; w.progress = 0; w.goal = destination??{...target}; w.path = destination ? this.route(w, destination) : []; w.stuck = 0;
    w.noProgress=0;w.bestDistance=destination?distance(w,destination):Infinity;
  }
  protected release(w: Worker) { w.slot = null; w.bay = null; w.goal = null; w.path = []; w.vx = 0; w.vz = 0;w.blockedPhase=undefined;w.routeRetries=0;w.retryIn=0;w.noProgress=0;w.bestDistance=undefined;w.routeIssue=undefined;w.waitSince=undefined;w.batch=undefined;w.unloadDuration=undefined; }
  protected finish(w: Worker, vacate = false) {
    if(w.finishAfterDelivery){w.finishAfterDelivery=false;this.collectionStats.finished++;this.say(w,'本轮采集已结束，等待新任务');}
    this.release(w); w.phase = 'idle'; w.source = null; w.progress = 0;
    const next = w.queue.shift(); if (next) this.execute(w, next);
    else if (vacate && (BAYS.some(p=>distance(w,p)<1) || this.sources.some(s=>distance(w,s)<s.radius+1.1))) this.go(w,this.vacant(home(w.id),w,BAYS),'move');
  }
  private approach(w: Worker) {
    w.slot = null; w.queuedAt = ++this.serial;w.waitSince=this.time;
    const s = this.sources.find(s => s.id === w.source && s.stock > 0);
    if (!s) { this.depleted(w); return; }
    const spot = this.vacant({ x: s.x, z: s.z + s.radius + 2.1 }, w, this.sources.flatMap(s => Array.from({ length: 4 }, (_, i) => workSpot(s, i))));
    this.go(w, spot, 'waitWork');
  }
  protected deliveryBays(): Point[] { return BAYS; }
  protected deliver(w: Worker) {
    w.slot = null; w.bay = null; w.queuedAt = ++this.serial;w.waitSince=this.time;
    const spot = this.vacant({ x: -6 + ((w.id - 1) % 4 - 1.5) * .95, z: 1.1 + Math.floor((w.id - 1) / 4) * .85 }, w, this.deliveryBays());
    this.go(w, spot, 'waitDepot');
  }
  private execute(w: Worker, order: Order) {
    this.release(w); w.source = null;w.finishAfterDelivery=false;
    if (order.type === 'move') this.go(w, order.point, 'move');
    else if (order.type === 'work') {
      w.source = order.source;
      w.lastSource=order.source;this.collectionStats.orders++;
      if (w.cargo) this.deliver(w); else this.approach(w);
    } else if (w.cargo) this.deliver(w);
    else this.go(w, this.vacant(home(w.id), w, BAYS), 'move');
  }
  private command(w: Worker, order: Order, append: boolean) {
    const same=(a:Order,b:Order)=>a.type===b.type&&(a.type==='work'&&b.type==='work'?a.source===b.source:a.type==='move'&&b.type==='move'?distance(a.point,b.point)<.2:true);
    // Repeated clicks must not restart a swing, release a slot or duplicate the last queued order.
    if(append&&w.phase!=='idle'){
      if(w.queue.length&&same(w.queue.at(-1)!,order))return;
      if(w.queue.length>=16){this.say(w,'后续任务已达 16 条，请先清理队列');return;}
      w.queue.push(order);
    }
    else if(!w.finishAfterDelivery&&w.phase!=='blocked'&&w.phase!=='idle'&&(
      order.type==='work'&&w.source===order.source||
      order.type==='move'&&w.phase==='move'&&w.goal&&distance(w.goal,order.point)<.2||
      order.type==='transport'&&w.source===null&&!!w.cargo&&['waitDepot','deliver','unload'].includes(w.phase)
    )){w.queue=[];return;}
    else { w.queue = []; this.execute(w, order); }
  }
  move(ids: number[], destination: Point, append = false) {
    if (this.paused) return;
    const ws = this.workers.filter(w => ids.includes(w.id));
    const cols = Math.ceil(Math.sqrt(ws.length)), rows = Math.ceil(ws.length / cols);
    // Pair nearby workers to nearby formation positions to reduce crossing.
    const points = ws.map((_, i) => ({ x: destination.x + (i % cols - (cols - 1) / 2) * 1.2, z: destination.z + (Math.floor(i / cols) - (rows - 1) / 2) * 1.2 }));
    const reserved: Point[] = [];
    for (const w of [...ws].sort((a, b) => distance(a, destination) - distance(b, destination))) {
      points.sort((a, b) => distance(w, a) - distance(w, b));
      const target = this.vacant(points.shift()!, w, reserved); reserved.push(target);
      this.command(w, { type: 'move', point: target }, append);
    }
  }
  work(ids: number[], source: number, append = false) {
    if (this.paused || !this.sources.some(s => s.id === source && s.stock > 0)) return false;
    for (const w of this.workers.filter(w => ids.includes(w.id))) this.command(w, { type: 'work', source }, append);
    return true;
  }
  assign(ids: number[], job: Job, append = false) {
    if (this.paused) return [];
    const sources = this.sources.filter(s => s.activity === job && s.stock > 0);
    if (!sources.length) return [];
    // Count current jobs and queued orders; replacing an order releases that worker's old assignment.
    const loads = new Map(sources.map(s => [s.id, this.workers.reduce((n,w) => {
      if (!append && ids.includes(w.id)) return n;
      return n + Number(w.source === s.id) + w.queue.filter(o=>o.type==='work'&&o.source===s.id).length;
    },0)]));
    const assigned: {worker:number;source:number}[] = [];
    for (const w of this.workers.filter(w=>ids.includes(w.id))) {
      const origin = append ? w.queue.filter(o=>o.type==='move').at(-1)?.point ?? w.goal ?? w : w;
      const s = [...sources].sort((a,b) => (loads.get(a.id)! * 6 + distance(origin,a)) - (loads.get(b.id)! * 6 + distance(origin,b)))[0];
      this.command(w,{type:'work',source:s.id},append);
      loads.set(s.id,loads.get(s.id)!+1);assigned.push({worker:w.id,source:s.id});
    }
    return assigned;
  }
  distribute(ids: number[], append = false) {
    if (this.paused) return [];
    // Desired balance is 4 wood / 3 ore / 3 food / 2 hauling for twelve workers.
    const weights: Record<Job,number> = {chop:4,mine:3,gather:3,pickup:2};
    const jobs=(Object.keys(weights) as Job[]).filter(job=>this.sources.some(s=>s.activity===job&&s.stock>0));
    const load=new Map(jobs.map(job=>[job,this.workers.reduce((n,w)=>{
      if(!append&&ids.includes(w.id))return n;
      return n+Number(this.sources.find(s=>s.id===w.source)?.activity===job)+w.queue.filter(o=>o.type==='work'&&this.sources.find(s=>s.id===o.source)?.activity===job).length;
    },0)]));
    const assigned:{worker:number;source:number}[]=[];
    for(const w of this.workers.filter(w=>ids.includes(w.id))){
      const job=[...jobs].sort((a,b)=>load.get(a)!/weights[a]-load.get(b)!/weights[b])[0];
      if(!job)break;
      assigned.push(...this.assign([w.id],job,append));load.set(job,load.get(job)!+1);
    }
    return assigned;
  }
  setContinuation(ids: number[], enabled: boolean) {
    if(!this.paused)for(const w of this.workers.filter(w=>ids.includes(w.id)))w.continueJob=enabled;
  }
  addWorker(source: number) {
    if(this.paused || !this.sources.some(s=>s.id===source&&s.stock>0))return null;
    const target=this.sources.find(s=>s.id===source)!;
    const w=this.workers.filter(w=>w.phase==='idle'&&!w.cargo&&!w.queue.length).sort((a,b)=>distance(a,target)-distance(b,target))[0];
    if(!w)return null;
    this.work([w.id],source);return w.id;
  }
  removeWorker(source:number){
    if(this.paused)return null;
    // Withdraw a waiting/unladen worker first; any carried goods still go to the depot.
    const candidates=this.workers.filter(w=>w.source===source).sort((a,b)=>{
      const priority=(w:Worker)=>(w.cargo?10:0)+(w.phase==='waitWork'?0:w.phase==='approach'?1:2);
      return priority(a)-priority(b)||b.queuedAt-a.queuedAt;
    });
    const w=candidates[0];if(!w)return null;
    this.transport([w.id]);return w.id;
  }
  private depleted(w:Worker){
    const previous=this.sources.find(s=>s.id===w.source);
    if(previous)this.say(w,`${previous.name}已采空${w.queue.length?'，执行后续任务':w.continueJob&&!w.finishAfterDelivery?'，寻找同类资源':'，结束采集并待命'}`);
    // Explicit queued orders have priority over continuing an exhausted job.
    if(w.continueJob&&!w.finishAfterDelivery&&!w.queue.length&&previous){
      const assigned=this.assign([w.id],previous.activity);
      if(assigned.length){const next=this.sources.find(s=>s.id===assigned[0].source)!;this.say(w,`${previous.name}已采空，转往${next.name}`);return;}
    }
    this.finish(w,true);
  }
  clearQueue(ids: number[]) {
    if (!this.paused) for (const w of this.workers.filter(w=>ids.includes(w.id))) w.queue=[];
  }
  transport(ids: number[], append = false) {
    if (!this.paused) for (const w of this.workers.filter(w => ids.includes(w.id))) this.command(w, { type: 'transport' }, append);
  }
  finishWork(ids:number[]){
    if(this.paused)return;
    for(const w of this.workers.filter(w=>ids.includes(w.id))){
      if(!w.source&&!w.cargo)continue;
      w.queue=[];w.finishAfterDelivery=true;
      if(w.cargo){w.source=null;if(!['deliver','waitDepot','unload'].includes(w.phase)){w.routeRetries=0;this.deliver(w);}}
      this.say(w,w.cargo?'送完现有货物后结束':'完成本次采集并入库后结束');
    }
  }
  resumeWork(ids:number[]){
    if(this.paused)return [];
    const resumed:number[]=[];
    for(const w of this.workers.filter(w=>ids.includes(w.id)&&w.phase==='idle'&&w.lastSource!==null))if(this.work([w.id],w.lastSource!)){resumed.push(w.id);this.collectionStats.resumed++;}
    return resumed;
  }
  private blockRoute(w:Worker,issue:'unreachable'|'congestion'='unreachable'){
    w.blockedPhase=w.phase;w.phase='blocked';w.path=[];w.vx=0;w.vz=0;w.moving=false;w.retryIn=3;
    w.slot=null;w.bay=null;w.routeIssue=issue;
    this.say(w,(w.routeRetries??0)>=3?'路线仍受阻，任务和货物保留；请重试、改派或停止':'路线受阻，任务和货物保留，3 秒后尝试重新寻路');
  }
  retryRoute(ids:number[],manual=true){
    if(this.paused)return;
    for(const w of this.workers.filter(w=>ids.includes(w.id)&&w.phase==='blocked')){
      w.routeRetries=manual?0:(w.routeRetries??0)+1;
      if(w.source&&this.sources.find(s=>s.id===w.source)?.stock===0&&!w.cargo){this.depleted(w);continue;}
      const phase=w.blockedPhase??(w.cargo?'deliver':'move');
      w.routeIssue=undefined;
      if(['deliver','waitDepot'].includes(phase)&&w.cargo)this.deliver(w);
      else if(['approach','waitWork'].includes(phase)&&w.source)this.approach(w);
      else if(w.goal){const p={...w.goal};this.go(w,p,phase);}else if(w.cargo)this.deliver(w);else this.finish(w);
    }
  }
  stop(ids: number[]) {
    if (this.paused) return;
    for (const w of this.workers.filter(w => ids.includes(w.id))) { if(w.phase!=='idle')this.collectionStats.stopped++;this.release(w); w.queue = []; w.source = null; w.phase = 'idle'; w.progress = 0; w.moving = false;w.finishAfterDelivery=false; }
  }
  status(w: Worker) {
    if(w.phase==='blocked')return `${w.routeIssue==='congestion'?'通行拥堵':'路线受阻'} · ${(w.routeRetries??0)>=3?'请重试或改派':Math.ceil(w.retryIn??0)+'秒后重试'}`;
    const s = this.sources.find(s => s.id === w.source);
    const text = w.phase === 'idle' ? w.cargo ? '待命 · 有货物' : '待命' : w.phase === 'move' ? '移动' : w.phase === 'approach' ? `前往${s?.name ?? '资源点'}` : w.phase === 'waitWork' ? '等待工作位' : w.phase === 'waitDepot' ? '等待卸货位' : w.phase === 'work' ? `${s ? ACTIVITY[s.activity] : '作业'} ${Math.floor(w.progress * 100)}%` : w.phase === 'deliver' ? `运送${w.cargo ? KIND[w.cargo.kind] : '货物'}` : '卸货';
    return text + (w.finishAfterDelivery?' · 本轮后结束':'') + (w.queue.length ? ` · 排队 ${w.queue.length} 条` : '');
  }
  workerAdvice(w:Worker){
    if(w.phase==='blocked')return `${w.routeIssue==='congestion'?'长时间未能推进，可将附近待命工人移开。':'无法到达目标。'}已释放预约位置，重试时重新选择。${w.cargo?'携货保留。':''}${(w.routeRetries??0)>=3?'自动重试已用完，请重试、改派或停止。':'等待自动重试，也可手动重试。'}`;
    if(w.phase==='waitWork'||w.phase==='waitDepot'){
      const work=w.phase==='waitWork';
      const queue=this.workers.filter(o=>o.phase===w.phase&&(!work||o.source===w.source)).sort((a,b)=>a.queuedAt-b.queuedAt);
      return `${work?'工作位':'卸货位'}排队第 ${queue.findIndex(o=>o===w)+1} 位 · 已等待 ${Math.floor(this.time-(w.waitSince??this.time))} 秒。${work?'每处资源有四个位置，可改派到其他资源。':'会自动使用空闲卸货位，携货不会丢失。'}`;
    }
    if(w.finishAfterDelivery)return '已取消后续任务；本次采集入库后结束。仍可立即停止或改派。';
    if(w.phase==='idle'&&w.cargo)return '货物仍由工人携带；送回仓库或改派采集会先卸货。';
    if(w.phase==='idle'&&w.lastSource!==null&&!this.sources.some(s=>s.id===w.lastSource&&s.stock>0))return '原资源已采空，请选择其他资源。';
    if(w.phase==='work')return '采完当前这份后自动运送、入库并返回；本轮后结束可取消往返。';
    return w.cargo?'正在运输入库；完成后继续原任务。':'点击资源可派工；新任务替换当前任务，Shift 可追加。';
  }
  private reachable(w:Worker,p:Point){
    const route=this.route(w,p);
    return distance(w,p)<.22||(route.length>0&&distance(route.at(-1)!,p)<.22);
  }
  protected admit() {
    for (const s of this.sources) {
      const waiting = this.workers.filter(w => w.source === s.id && w.phase === 'waitWork').sort((a,b) => a.queuedAt - b.queuedAt);
      for (const w of waiting) {
        if (!s.stock) { this.depleted(w); continue; }
        const slots = [0,1,2,3].filter(i => !this.workers.some(o => o.source === s.id && o.slot === i));
        slots.sort((a,b) => distance(w, workSpot(s,a)) - distance(w, workSpot(s,b)));
        const available=slots.filter(i=>!this.blocked(workSpot(s,i),.37)&&this.workers.every(o => o === w || distance(o, workSpot(s,i)) > .7));
        const slot=available.find(i=>this.reachable(w,workSpot(s,i)));
        if (slot === undefined) {if(available.length||[0,1,2,3].every(i=>this.blocked(workSpot(s,i),.37)))this.blockRoute(w);continue;}
        w.slot = slot; this.go(w, workSpot(s,slot), 'approach');
      }
    }
    for (const w of this.workers.filter(w => w.phase === 'waitDepot').sort((a,b) => a.queuedAt - b.queuedAt)) {
      const available=this.deliveryBays().map((p,i)=>({p,i})).filter(({p,i}) => !this.blocked(p,.37)&&!this.workers.some(o => o.bay === i) && this.workers.every(o => o === w || distance(o,p) > .75)).sort((a,b)=>distance(w,a.p)-distance(w,b.p));
      const bay = available.find(({p})=>this.reachable(w,p))?.i;
      if (bay === undefined) {if(available.length||this.deliveryBays().every(p=>this.blocked(p,.37)))this.blockRoute(w);continue;}
      w.bay = bay; this.go(w, this.deliveryBays()[bay], 'deliver');
    }
  }
  // Sample velocities with a short prediction horizon, then enforce a hard separation.
  // Right-biased passing makes head-on traffic choose consistent sides.
  protected walk(w: Worker, dt: number) {
    if (!w.path.length) { w.vx = 0; w.vz = 0; return; }
    while (w.path.length && distance(w, w.path[0]) < .10) w.path.shift();
    if (!w.path.length) { w.vx = 0; w.vz = 0; return; }
    const p = w.path[0], d = distance(w,p), speed = Math.min(this.travelSpeed(w), d / dt), dx = (p.x-w.x)/d, dz = (p.z-w.z)/d;
    const near = this.workers.filter(o => o !== w && distance(w,o) < 3.2);
    let best = Infinity, vx = 0, vz = 0;
    for (const factor of [1,.55,.22]) for (const angle of [0,-.35,.35,-.7,.7,-1.1,1.1,-1.6,1.6,-2.2,2.2,Math.PI]) {
      const ux = (dx*Math.cos(angle)-dz*Math.sin(angle))*speed*factor, uz = (dx*Math.sin(angle)+dz*Math.cos(angle))*speed*factor;
      const q = { x: w.x+ux*dt, z: w.z+uz*dt };
      if (this.blocked(q,.31) || !this.segment(w,q,.31)) continue;
      let cost = (1-factor)*1.3 + (1-Math.cos(angle))*1.7 + (angle>0 ? .025 : 0);
      if (!this.segment(w,{x:w.x+ux*.25,z:w.z+uz*.25},.32)) cost += 5;
      let invalid = false;
      for (const o of near) {
        if (distance(q,o) < RADIUS*2+.025) { invalid = true; break; }
        const rx=o.x-w.x, rz=o.z-w.z, rvx=ux-o.vx, rvz=uz-o.vz;
        const t=Math.max(0,Math.min(.85,(rx*rvx+rz*rvz)/(rvx*rvx+rvz*rvz||1)));
        const separation=Math.hypot(rx-rvx*t,rz-rvz*t);
        const comfort=Math.hypot(o.vx,o.vz)<.1?.62:.85;
        if(separation<comfort) cost += (comfort-separation)*7/(.4+t);
      }
      if(!invalid && cost < best){best=cost;vx=ux;vz=uz;}
    }
    w.vx=vx;w.vz=vz;w.x+=vx*dt;w.z+=vz*dt;
    w.moving=Math.hypot(vx,vz)>.08;
    if(w.moving)w.face=Math.atan2(vx,vz);
    const toward=vx*dx+vz*dz;
    w.stuck=(toward<.35||near.some(o=>!o.path.length&&segmentDistance(o,w,p)<.7))?w.stuck+dt:Math.max(0,w.stuck-dt*.3);
    if(w.stuck>1.3 && w.goal){
      const avoid=this.workers.filter(o=>o!==w&&distance(w,o)<3.2).map(o=>({x:o.x,z:o.z}));
      const route=this.route(w,w.goal,avoid);if(route.length&&distance(route.at(-1)!,w.goal)<.18)w.path=route;w.stuck=0;
    }
  }
  tick(dt: number) {
    if (this.paused) return;
    this.time += dt; this.admit();
    // Rotate priority so one worker never owns all contested space.
    const first = Math.floor(this.time * 2) % this.workers.length;
    const ordered = [...this.workers.slice(first), ...this.workers.slice(0,first)];
    for (const w of ordered) {
      w.moving = false;
      if(w.phase==='blocked'){
        w.retryIn=Math.max(0,(w.retryIn??3)-dt);
        if(w.retryIn===0&&(w.routeRetries??0)<3)this.retryRoute([w.id],false);
        continue;
      }
      if (WALKING.includes(w.phase)) {
        if (w.path.length) {
          this.walk(w,dt);
          if(w.goal){
            const remaining=distance(w,w.goal);
            if(remaining<(w.bestDistance??Infinity)-.3){w.bestDistance=remaining;w.noProgress=0;}
            else w.noProgress=(w.noProgress??0)+dt;
            if((w.noProgress??0)>10)this.blockRoute(w,this.workers.some(o=>o!==w&&distance(o,w)<1.6)?'congestion':'unreachable');
          }
          continue;
        }
        w.vx=0;w.vz=0;
        if (w.phase==='waitWork'||w.phase==='waitDepot') continue;
        if (!w.goal || distance(w,w.goal)>.22) { this.blockRoute(w); continue; }
        w.routeRetries=0;
        w.goal=null;
        if (w.phase==='move') this.finish(w);
        else if (w.phase==='deliver') {w.phase='unload';w.progress=0;w.unloadDuration=unloadSeconds(this.logistics,w.cargo?.amount??0);}
        else {w.phase='work';w.progress=0;}
      } else if (w.phase==='work') {
        const s=this.sources.find(s=>s.id===w.source&&s.stock>0);
        if(!s){this.depleted(w);continue;}
        w.face=Math.atan2(s.x-w.x,s.z-w.z);
        if(!w.batch){
          const reserved=this.workers.reduce((n,o)=>n+(o!==w&&o.source===s.id&&o.phase==='work'?(o.batch?.amount??0):0),0);
          const amount=batchSize(this.logistics,s.kind,s.stock-reserved);
          if(!amount)continue;
          w.batch={amount,seconds:gatherSeconds(this.logistics,s.activity,amount),unitWeight:this.logistics.weights[s.kind]};
        }
        w.progress=Math.min(1,w.progress+dt/w.batch.seconds);
        if(w.progress>=1){
          const amount=Math.min(w.batch.amount,s.stock);s.stock-=amount;w.cargo={kind:s.kind,amount,unitWeight:w.batch.unitWeight};w.batch=undefined;
          if(w.finishAfterDelivery)w.source=null;
          this.say(w,`${s.activity==='pickup'?'装货':'采得'} ${KIND[s.kind]} ×${amount}，运往仓库`);this.deliver(w);
        }
      } else if(w.phase==='unload') {
        w.progress=Math.min(1,w.progress+dt/(w.unloadDuration??unloadSeconds(this.logistics,w.cargo?.amount??0)));
        if(w.progress>=1){
          if(w.cargo){this.inventory[w.cargo.kind]+=w.cargo.amount;this.collectionStats.delivered+=w.cargo.amount;this.say(w,`${KIND[w.cargo.kind]} +${w.cargo.amount} 已入库`);w.cargo=null;}
          w.bay=null;
          if(w.finishAfterDelivery){w.finishAfterDelivery=false;this.collectionStats.finished++;this.say(w,'本轮货物已入库，采集结束');w.source=null;}
          if(w.source)this.approach(w);
          else { const next=w.queue.shift();if(next)this.execute(w,next);else this.go(w,this.vacant(home(w.id),w,BAYS),'move'); }
        }
      }
    }
  }
}
function segmentDistance(p: Point,a: Point,b: Point){const dx=b.x-a.x,dz=b.z-a.z,t=Math.max(0,Math.min(1,((p.x-a.x)*dx+(p.z-a.z)*dz)/(dx*dx+dz*dz||1)));return distance(p,{x:a.x+dx*t,z:a.z+dz*t});}
const DIRECTIONS = [[1,0],[-1,0],[0,1],[0,-1],[1,1],[1,-1],[-1,1],[-1,-1]];
