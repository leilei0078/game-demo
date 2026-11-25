using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace GatheringDemo
{
    // Session diagnostics only. Economic inventory remains owned by GatheringWorld.
    public sealed class GatheringMetrics
    {
        public sealed class WorkerSample
        {
            public int id,delivered,trips;
            public double idle,travel,gather,waitWork,waitDepot,unload,blocked,distance,construction;
            public double Total=>idle+travel+gather+waitWork+waitDepot+unload+blocked+construction;
            public double Busy=>Total-idle;
        }
        public sealed class ResourceSample
        {
            public int items,trips;
            public double weight;
        }
        [Serializable] public sealed class Report
        {
            public int schemaVersion=1;
            public string recordedUtc;
            public double elapsedSeconds,workerSeconds,travelSeconds,gatherSeconds,workQueueSeconds,depotQueueSeconds,unloadSeconds,blockedSeconds,idleSeconds,distanceMeters;
            public int deliveredItems,completedTrips,carriedItems;
            public double deliveredKg,itemsPerMinute,kgPerMinute;
            public double constructionSeconds;
            public int[] deliveredByKind;
        }
        private struct Delivery { public double time;public int items;public float kg; }
        private readonly Dictionary<int,WorkerSample> workers=new Dictionary<int,WorkerSample>();
        private readonly Dictionary<int,Vector3> previousPositions=new Dictionary<int,Vector3>();
        private readonly Queue<Delivery> recent=new Queue<Delivery>();
        private readonly ResourceSample[] resources={new ResourceSample(),new ResourceSample(),new ResourceSample()};
        public double Elapsed { get; private set; }
        public int Items=>resources.Sum(r=>r.items);
        public int Trips=>resources.Sum(r=>r.trips);
        public double Weight=>resources.Sum(r=>r.weight);
        public double ItemsPerMinute=>Elapsed>0?Items*60/Elapsed:0;
        public double KgPerMinute=>Elapsed>0?Weight*60/Elapsed:0;
        public double RecentDuration=>Math.Min(30,Elapsed);
        public double RecentItemsPerMinute=>RecentDuration>0?recent.Sum(d=>d.items)*60/RecentDuration:0;
        public bool RecentWarming=>Elapsed<30;
        public ResourceSample Resource(ResourceKind kind)=>resources[(int)kind];
        public WorkerSample Worker(int id)
        {
            if(!workers.TryGetValue(id,out var sample)){sample=new WorkerSample{id=id};workers.Add(id,sample);}return sample;
        }
        public void Reset(IEnumerable<GatheringWorker> population)
        {
            Elapsed=0;workers.Clear();previousPositions.Clear();recent.Clear();
            foreach(var r in resources){r.items=0;r.trips=0;r.weight=0;}
            foreach(var w in population){Worker(w.Id);previousPositions[w.Id]=w.transform.position;}
        }
        public void Tick(float dt,IEnumerable<GatheringWorker> population,bool paused)
        {
            if(!paused)Elapsed+=dt;
            foreach(var w in population)
            {
                Vector3 position=w.transform.position;
                bool known=previousPositions.TryGetValue(w.Id,out var previous);previousPositions[w.Id]=position;
                if(paused)continue;
                var sample=Worker(w.Id);
                if(known)sample.distance+=Vector3.Distance(previous,position);
                switch(w.Phase)
                {
                    case WorkerPhase.Idle:sample.idle+=dt;break;
                    case WorkerPhase.Moving:case WorkerPhase.ToWork:case WorkerPhase.ToDepot:case WorkerPhase.ToSupply:case WorkerPhase.ToSite:sample.travel+=dt;break;
                    case WorkerPhase.Working:if(w.BatchCount>0)sample.gather+=dt;else sample.waitWork+=dt;break;
                    case WorkerPhase.WaitingWork:sample.waitWork+=dt;break;
                    case WorkerPhase.WaitingDepot:sample.waitDepot+=dt;break;
                    case WorkerPhase.Unloading:case WorkerPhase.LoadingSupply:case WorkerPhase.UnloadingSite:sample.unload+=dt;break;
                    case WorkerPhase.WaitingBuild:sample.waitWork+=dt;break;
                    case WorkerPhase.Constructing:sample.construction+=dt;break;
                    case WorkerPhase.Blocked:sample.blocked+=dt;break;
                }
            }
            while(recent.Count>0&&recent.Peek().time<=Elapsed-30)recent.Dequeue();
        }
        public void Deposit(int workerId,ResourceKind kind,int count,float kg)
        {
            var r=Resource(kind);r.items+=count;r.weight+=kg;r.trips++;
            var w=Worker(workerId);w.delivered+=count;w.trips++;
            recent.Enqueue(new Delivery{time=Elapsed,items=count,kg=kg});
        }
        public WorkerSample Aggregate(IEnumerable<int> ids=null)
        {
            var all=new WorkerSample();
            foreach(var w in ids==null?workers.Values:ids.Select(Worker))
            {
                all.delivered+=w.delivered;all.trips+=w.trips;all.idle+=w.idle;all.travel+=w.travel;all.gather+=w.gather;
                all.waitWork+=w.waitWork;all.waitDepot+=w.waitDepot;all.unload+=w.unload;all.blocked+=w.blocked;all.distance+=w.distance;all.construction+=w.construction;
            }
            return all;
        }
        public Report Snapshot(int carried)
        {
            var all=Aggregate();return new Report{recordedUtc=DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture),elapsedSeconds=Elapsed,
                workerSeconds=all.Total,travelSeconds=all.travel,gatherSeconds=all.gather,workQueueSeconds=all.waitWork,depotQueueSeconds=all.waitDepot,
                unloadSeconds=all.unload,blockedSeconds=all.blocked,idleSeconds=all.idle,distanceMeters=all.distance,deliveredItems=Items,completedTrips=Trips,
                carriedItems=carried,deliveredKg=Weight,itemsPerMinute=ItemsPerMinute,kgPerMinute=KgPerMinute,constructionSeconds=all.construction,deliveredByKind=resources.Select(r=>r.items).ToArray()};
        }
    }
}
