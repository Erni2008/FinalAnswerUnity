using Newtonsoft.Json.Linq;
namespace FinalAnswer {
 public sealed class LastCommandAudioRouter {
  readonly MachineController machine;JObject previous;string verdict="",voice="",note="",face="",victory="";long victoryAt;int stress;long localOpen;
  GameManager App=>machine.App;AudioManager Audio=>App.Audio;
  public LastCommandAudioRouter(MachineController m){machine=m;}
  public void Local(string id,string action,string value){if(action=="RELEASE")return;string cue=id=="BIG_BUTTON"?"BUTTON_PRESS":id.StartsWith("SMALL_BUTTON")?"SMALL_BUTTON_CLICK":id=="LEVER_MODULE"?"LEVER_MOVE":id=="ADJUSTER"||id=="CODE_PANEL"?"ROTARY_STEP":id=="HIDDEN_HATCH"?(value=="OPEN"?"HATCH_OPEN":""):id=="SYMBOL_PANEL"?"SYMBOL_CLICK":"SMALL_BUTTON_CLICK";if(cue!="")Audio.Play(cue);if(id=="HIDDEN_HATCH"&&value=="OPEN")localOpen=Json.MonoMs;}
  public void Snapshot(JObject panel){var r=panel.Obj("testRound");if(r==null)return;string key=panel.S("sessionId")+":"+r.S("taskId");Audio.Protected=r.S("category")=="MEMORY"||r.S("taskType")=="SILENCE"||r.S("taskType")=="FALSE_COMMAND"||r.S("taskType")=="TIMING"||r.S("taskType")=="DELAYED_EXECUTION";
   if(previous!=null){var old=previous.Obj("testRound");if(panel.S("sessionId")!=previous.S("sessionId")){Audio.StopTransient();voice=note=face=verdict=victory="";}
    if(r.S("status")=="WON"&&old.S("status")!="WON"){Audio.StopPrefix("MEMORY_NOTE");Audio.Play("MACHINE_SHUTDOWN",key+":shutdown");if(r.L("step")>=20){victory=key+":"+r.L("finishedAt");victoryAt=r.L("finishedAt")+700;}App.SaveResult(r);}
    else if(r.L("nextAt")>0){string result=key+":"+r.S("feedback");if(result!=verdict){verdict=result;bool ok=r.S("feedback")=="COMMAND ACCEPTED";bool wrong=r.S("feedback")=="WRONG COMMAND";if(ok||wrong){if(r.S("taskType")=="CIRCUIT")Audio.Play(ok?"CIRCUIT_COMPLETE":"CIRCUIT_FAILED",result);Audio.Play(ok?"ANSWER_CORRECT":r.S("taskType").Contains("SYMBOL")?"SYMBOL_WRONG":"ANSWER_WRONG",result);}}}
    if(panel.B("hatch")&&!previous.B("hatch")&&Json.MonoMs-localOpen>2000)Audio.Play("HATCH_OPEN",key+":open");
    int stage=(int)r.L("step")/5;if(stage>stress){Audio.Play("METAL_CREAK",key+":stress");if(stage>=2)Audio.Play("METAL_CRACK",key+":crack");}stress=stage;
   }
   previous=(JObject)panel.DeepClone();
  }
  public void Tick(long now){var p=machine.Panel;var r=p.Obj("testRound");if(r==null)return;string task=p.S("sessionId")+":"+r.S("taskId");if(victory!=""&&now>=victoryAt){Audio.Play("VICTORY",victory);victory="";}if(r.B("paused")||r.L("nextAt")>0||r.S("status")!="PLAYING")return;var t=r.Obj("touchscreen");long reveal=t.L("revealUntil");
   if(r.S("taskType")=="FALSE_COMMAND"&&now>=r.L("readyAt")&&voice!=task){voice=task;Audio.Play("PRESS_"+t.S("spokenColor"),task);}
   string nextFace=task+":"+(reveal>0&&now>=reveal?"after":"before");if(nextFace!=face){bool prior=face!="";face=nextFace;if(prior&&r.S("taskType")=="FIND_CHANGE"&&now>=reveal)Audio.Play("SCREEN_TRANSITION",face);if(r.S("taskType").Contains("SYMBOL"))Audio.Play("SYMBOL_APPEAR",face);}
   var flashes=t.Arr("flashes");long elapsed=now-r.L("readyAt");int index=(int)(elapsed/850);if(flashes.Count>0&&elapsed>=0&&now<reveal&&index<flashes.Count){int color=(int)flashes[index];string key=task+":"+index;if(key!=note&&elapsed%850<250&&color>=0&&color<4){note=key;string name=new[]{"RED","GREEN","BLUE","YELLOW"}[color];Audio.Play("MEMORY_NOTE_"+name,key);machine.Indicator.SetColor(name);}if(elapsed%850>450)machine.Indicator.SetColor("OFF");}
  }
 }
}
