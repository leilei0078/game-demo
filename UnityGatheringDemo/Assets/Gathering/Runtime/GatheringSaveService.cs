using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace GatheringDemo
{
    public static class GatheringSaveStore
    {
        [Serializable] private sealed class Envelope {public int format=1;public string payload,sha256;}
        public static string DefaultPath=>Path.Combine(Application.persistentDataPath,"WorldSaves","world-v1.json");
        private static string Hash(string text)
        {using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();}
        public static void Write(string path,GatheringSaveData data)
        {
            data.Validate();string payload=JsonUtility.ToJson(data);var envelope=new Envelope{payload=payload,sha256=Hash(payload)};
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temporary=path+".tmp";byte[] bytes=Encoding.UTF8.GetBytes(JsonUtility.ToJson(envelope));
            using(var file=new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None)){file.Write(bytes,0,bytes.Length);file.Flush(true);}
            if(!File.Exists(path)){File.Move(temporary,path);return;}
            bool valid=true;try{ReadOne(path);}catch(Exception){valid=false;}
            // Preserve the last good backup if the current primary was damaged.
            File.Replace(temporary,path,valid?path+".bak":null);
        }
        public static GatheringSaveData ReadOne(string path)
        {
            var info=new FileInfo(path);if(!info.Exists)throw new FileNotFoundException("没有找到存档",path);
            if(info.Length>8*1024*1024)throw new InvalidDataException("存档大小超出限制");
            var envelope=JsonUtility.FromJson<Envelope>(File.ReadAllText(path,Encoding.UTF8));
            if(envelope==null||envelope.format!=1||string.IsNullOrEmpty(envelope.payload)||Hash(envelope.payload)!=envelope.sha256)throw new InvalidDataException("存档不完整或校验失败");
            var data=JsonUtility.FromJson<GatheringSaveData>(envelope.payload);if(data==null)throw new InvalidDataException("存档内容缺失");data.Validate();return data;
        }
        public static GatheringSaveData Read(string path,out bool backup)
        {
            backup=false;
            try{return ReadOne(path);}
            catch(Exception first)
            {
                try{var data=ReadOne(path+".bak");backup=true;return data;}
                catch(Exception){throw new InvalidDataException("存档和备份均无法读取，当前世界保留。"+first.Message,first);}
            }
        }
    }

    public sealed class GatheringSaveService : MonoBehaviour
    {
        private GatheringWorld world;
        public bool Loading {get;private set;}
        public string Status {get;private set;}="手动保存可在下次运行后继续；读档会回到保存时刻";
        public string SavePath=>GatheringSaveStore.DefaultPath;
        public bool CanLoad=>File.Exists(SavePath)||File.Exists(SavePath+".bak");
        private void Awake(){world=GetComponent<GatheringWorld>();}
        public OrderResult Save(string path=null)
        {
            if(Loading||!world.Ready)return OrderResult.No("正在恢复，请稍后保存");
            try{var data=world.CaptureSave();if(world.GuideStep>=6)data.guideStep=7;GatheringSaveStore.Write(path??SavePath,data);world.GuideStep=data.guideStep;Status="已保存 · "+DateTime.Now.ToString("HH:mm:ss")+" · "+(path??SavePath);world.Notice=Status;return OrderResult.Yes(Status);}
            catch(Exception ex){Status="保存失败，上一份存档保留："+ex.Message;world.Notice=Status;return OrderResult.No(Status);}
        }
        public OrderResult Load(string path=null)
        {
            if(Loading||!world.Ready)return OrderResult.No("正在恢复，请稍后");
            GatheringSaveData data;bool backup;
            try{data=GatheringSaveStore.Read(path??SavePath,out backup);}
            catch(Exception ex){Status=ex.Message;world.Notice=Status;return OrderResult.No(Status);}
            var rollback=world.CaptureSave();Loading=true;Status="正在恢复世界…";StartCoroutine(Apply(data,backup,rollback));return OrderResult.Yes(Status);
        }
        private IEnumerator Apply(GatheringSaveData data,bool backup,GatheringSaveData rollback)
        {
            var restore=world.RestoreSave(data);
            Exception failure=null;
            while(true)
            {
                object next;
                try{if(!restore.MoveNext())break;next=restore.Current;}
                catch(Exception ex){failure=ex;break;}
                yield return next;
            }
            if(failure!=null)
            {
                var previous=world.RestoreSave(rollback);
                while(previous.MoveNext())yield return previous.Current;
                Loading=false;Status="无法恢复此存档，已还原读取前的世界："+failure.Message;world.Notice=Status;world.GetComponent<GatheringController>().PersistenceFeedback(Status);yield break;
            }
            Loading=false;Status=(backup?"已从上一版备份恢复":"已恢复存档")+" · 路线重新计算，采集统计重新开始";world.Notice=Status;world.GetComponent<GatheringController>().PersistenceFeedback(Status);
        }
    }
}
