using System.Collections.Generic;
using UnityEngine;
namespace FinalAnswer {
 public sealed class AudioManager:MonoBehaviour {
  readonly Dictionary<string,AudioCatalog.Cue> cues=new Dictionary<string,AudioCatalog.Cue>();readonly Dictionary<string,long> last=new Dictionary<string,long>();readonly HashSet<string> seen=new HashSet<string>();readonly Queue<string> order=new Queue<string>();readonly AudioSource[] pool=new AudioSource[8];readonly int[] priorities=new int[8];readonly string[] ids=new string[8];AudioSource preview,loop;public bool LoopPlaying=>loop&&loop.isPlaying;public IEnumerable<string> AvailableIds=>cues.Keys;public bool Protected;public float Volume{get;private set;}=0.8f;
  public void Initialize(AudioCatalog data){for(int i=0;i<pool.Length;i++){pool[i]=gameObject.AddComponent<AudioSource>();pool[i].playOnAwake=false;pool[i].spatialBlend=0;}if(data)foreach(var c in data.cues)if(c.clip)cues[c.id]=c;Volume=PlayerPrefs.GetFloat("FinalAnswer.SFX",.8f);}
  public void Register(string id,AudioClip clip,float gain=.7f,int priority=2){if(clip)cues[id]=new AudioCatalog.Cue{id=id,clip=clip,gain=gain,priority=priority,throttleMs=100};}
  public void SetLoop(string id,bool active){if(!active||Protected||Volume<=0||!cues.TryGetValue(id,out var c)){if(loop)loop.Stop();return;}if(!loop){loop=gameObject.AddComponent<AudioSource>();loop.playOnAwake=false;loop.loop=true;loop.priority=240;}if(loop.isPlaying&&loop.clip==c.clip)return;loop.clip=c.clip;loop.volume=c.gain*Volume;loop.Play();}
  /// <summary>Hosted by the shell: follows the shell mixer (master × effects, mute) without touching saved prefs.</summary>
  public void SetHostedVolume(float value){Volume=Mathf.Clamp01(value);}
  public void SetVolume(float value){Volume=Mathf.Clamp01(value);PlayerPrefs.SetFloat("FinalAnswer.SFX",Volume);}
  public bool Play(string id,string key=null){if(!cues.TryGetValue(id,out var cue)||Volume<=0||Protected&&cue.priority==0)return false;if(key!=null){string k=id+":"+key;if(!seen.Add(k))return false;order.Enqueue(k);while(order.Count>512)seen.Remove(order.Dequeue());}long now=Json.MonoMs;if(last.TryGetValue(id,out var at)&&now-at<cue.throttleMs)return false;if(id.StartsWith("MEMORY_NOTE"))StopPrefix("MEMORY_NOTE");if(id=="VICTORY")StopPrefix("ANSWER_CORRECT");int slot=-1;for(int i=0;i<pool.Length;i++)if(!pool[i].isPlaying){slot=i;break;}if(slot<0)for(int i=0;i<pool.Length;i++)if(priorities[i]<cue.priority){slot=i;break;}if(slot<0)return false;pool[slot].Stop();pool[slot].clip=cue.clip;pool[slot].volume=cue.gain*Volume;pool[slot].priority=256-Mathf.Clamp(cue.priority*40+40,0,256);pool[slot].Play();priorities[slot]=cue.priority;ids[slot]=id;last[id]=now;return true;}
  public void Preview(string id){if(!cues.TryGetValue(id,out var c))return;if(!preview){preview=gameObject.AddComponent<AudioSource>();preview.playOnAwake=false;}preview.Stop();preview.clip=c.clip;preview.volume=c.gain*Volume;preview.Play();}
  public void StopPreview(){if(preview)preview.Stop();}
  public void Tick(){if(loop){if(Protected||Volume<=0)loop.Stop();else if(loop.clip)foreach(var c in cues.Values)if(c.clip==loop.clip){loop.volume=c.gain*Volume;break;}}if(Protected)for(int i=0;i<pool.Length;i++)if(pool[i].isPlaying&&priorities[i]==0)pool[i].Stop();}
  public void StopPrefix(string prefix){for(int i=0;i<pool.Length;i++)if(ids[i]!=null&&ids[i].StartsWith(prefix))pool[i].Stop();}
  public void StopTransient(){if(loop)loop.Stop();StopPreview();foreach(var s in pool)if(s)s.Stop();}
  void OnDestroy()=>StopTransient();
 }
}
