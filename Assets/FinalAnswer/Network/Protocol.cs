using System;
using System.Collections.Generic;
using System.Diagnostics;
using Newtonsoft.Json.Linq;
namespace FinalAnswer {
 public static class Json {
  public static string S(this JToken t,string key,string fallback="")=>t?[key]?.Type==JTokenType.Null?fallback:t?[key]?.ToString()??fallback;
  public static long L(this JToken t,string key,long fallback=0)=>long.TryParse(t.S(key),out var v)?v:fallback;
  public static double D(this JToken t,string key,double fallback=0)=>double.TryParse(t.S(key),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var v)?v:fallback;
  public static bool B(this JToken t,string key)=>t?[key]?.Type==JTokenType.Boolean&&(bool)t[key];
  public static JObject Obj(this JToken t,string key)=>t?[key] as JObject;
  public static JArray Arr(this JToken t,string key)=>t?[key] as JArray??new JArray();
  public static long MonoMs=>Stopwatch.GetTimestamp()*1000/Stopwatch.Frequency;
 }
 public sealed class ServerClock {
  long server,received;public long Now=>server+Json.MonoMs-received;
  public void Observe(JObject snapshot){if(snapshot["serverTime"]==null)return;server=snapshot.L("serverTime");received=Json.MonoMs;}
 }
 public sealed class SnapshotStore {
  public JObject Current{get;private set;}public event Action<JObject> Changed;
  public bool Apply(JObject state){if(state?["revision"]==null)return false;if(Current!=null&&Current.S("roomId")==state.S("roomId")&&state.L("revision")<Current.L("revision"))return false;Current=state;Changed?.Invoke(state);return true;}
  public void Clear(){Current=null;}
 }
 public sealed class NumberInputPreview {
  struct Edit {public string Id,Value;}
  readonly List<Edit> edits=new List<Edit>();string task="",authority="";public string Value{get;private set;}="";public int PendingCount=>edits.Count;
  public static string Apply(string text,string key){if(key=="CLEAR")return "";if(key=="DELETE")return text.Length==0?"":text.Substring(0,text.Length-1);if(key.Length==1&&text.Length<9&&((key[0]>='0'&&key[0]<='9')||(key=="-"&&text.Length==0)))return text+key;return text;}
  public void Queue(string id,string value){if(edits.Count>=64)return;edits.Add(new Edit{Id=id,Value=value});Value=Apply(Value,value);}
  public void Snapshot(JObject panel,string reply){var r=panel.Obj("testRound");string next=panel.S("sessionId")+":"+r.S("taskId");bool blocked=r==null||r.Obj("touchscreen").S("kind")!="number"||r.B("paused")||r.L("nextAt")>0||r.S("status")!="PLAYING";authority=r.Obj("touchscreen").S("input");if(task!=next||blocked){task=next;edits.Clear();Value=authority;return;}int index=edits.FindIndex(e=>e.Id==reply);if(index>=0)edits.RemoveRange(0,index+1);if(edits.Count==0||index>=0){Value=authority;foreach(var e in edits)Value=Apply(Value,e.Value);}}
  public void Cancel(){edits.Clear();Value=authority;}
 }
}
