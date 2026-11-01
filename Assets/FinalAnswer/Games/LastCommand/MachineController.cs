using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
namespace FinalAnswer {
 public sealed class MachineController:MonoBehaviour,IMiniGame {
  public string Id=>"last-command";public InteractiveMechanism[] Mechanisms;public InteractiveScreen Screen;public MachineCameraRig CameraRig;public HiddenCompartment Hatch;public RotatableMechanism Adjuster;public MechanicalSwitch Lever;public IndicatorLight Indicator;public CodeSelector Code;public TMP_Text AngleLabel;public Transform HatchMount;public TextAsset MechanismConfig;
  public GameManager App{get;private set;}public bool Ready=>App&&Screen&&Screen.Built;public JObject Panel{get;private set;}readonly Dictionary<string,InteractiveMechanism> bindings=new Dictionary<string,InteractiveMechanism>();readonly HashSet<string> required=new HashSet<string>();LastCommandAudioRouter sound;string task="";int focusIndex=-1;bool active;JObject mounts;
  public void Initialize(GameManager app){App=app;bindings.Clear();foreach(var m in Mechanisms){m.Owner=this;bindings[m.Id]=m;}Screen.Owner=this;Screen.Initialize(app.font);Code.Initialize(app.font);foreach(var c in Mechanisms)if(c is ChoicePanel p)p.Initialize(app.font);mounts=JObject.Parse(MechanismConfig.text);sound=new LastCommandAudioRouter(this);}
  public void Enter(){active=true;gameObject.SetActive(true);}
  public void Exit(){active=false;CancelInput();App?.Audio.StopTransient();}
  public void SetOffline(){Panel=null;foreach(var m in Mechanisms)m.Available=true;}
  public void Apply(JObject state){var p=state.Obj("lastCommand");if(p==null){SetOffline();return;}Panel=p;var r=p.Obj("testRound");string next=p.S("sessionId")+":"+r.S("taskId");if(next!=task){CancelInput();task=next;}required.Clear();foreach(var id in r.Arr("requiredMechanisms"))required.Add(id.ToString());SetAvailability(App.Clock.Now);foreach(var mechanism in Mechanisms){var slide=mechanism.GetComponent<SlidingPanel>();if(slide)slide.SetOpen(required.Contains(mechanism.Id));}
   Adjuster.SetAngle((float)p.D("adjuster"));Lever.SetPosition(Array.IndexOf(new[]{"UP","MIDDLE","DOWN"},p.S("lever","UP")));Indicator.SetColor(p.S("light","OFF"));Code.SetCode(p.S("code","0000"));SetPlacement(p.S("placementId","right"));Hatch.SetOpen(p.B("hatch"),p.S("hint"));foreach(var m in Mechanisms)if(m is ChoicePanel choice)choice.SetSelection(p.S(m.Id=="ANIMAL_PANEL"?"animal":"symbol"));sound?.Snapshot(p);
  }
  void SetAvailability(long now){var r=Panel.Obj("testRound");if(r==null)return;bool enabled=App.Network.Authenticated&&r.S("status")=="PLAYING"&&!r.B("paused")&&!r.B("waitingForTerminals")&&r.L("nextAt")==0&&now>=r.L("readyAt")&&now>=r.Obj("touchscreen").L("revealUntil");foreach(var m in Mechanisms)m.Available=enabled&&required.Contains(m.Id);}
  public bool Dispatch(InteractiveMechanism mechanism,string action,string value){if(!mechanism.Available&&action!="RELEASE")return false;if(Panel!=null){var r=Panel.Obj("testRound");if(!App.Network.Authenticated||r.B("paused")||r.L("nextAt")>0||App.Clock.Now<r.L("readyAt"))return false;if(string.IsNullOrEmpty(App.Action(mechanism.Id,action,value)))return false;}sound?.Local(mechanism.Id,action,value);return true;}
  public void Touch(string action,string value=""){if(Panel==null||!App.Network.Authenticated)return;var r=Panel.Obj("testRound");if(r.S("status")!="PLAYING"||r.B("paused")||r.L("nextAt")>0||App.Clock.Now<r.L("readyAt")||App.Clock.Now<r.Obj("touchscreen").L("revealUntil"))return;if(action=="ROTATE"&&int.TryParse(value,out var cell)&&r.Obj("touchscreen").Arr("board").Count>cell&&(int)r.Obj("touchscreen").Arr("board")[cell]==0)return;string id=App.Action("TOUCH_PANEL",action,value);if(id=="")return;App.Audio.Play(action=="CONFIRM"?"CONFIRM":action=="ROTATE"?"CIRCUIT_ROTATE":action=="ADJUST"?"ROTARY_STEP":(action=="SELECT"||action=="SELECT_PHYSICAL")&&r.S("taskType").Contains("SYMBOL")?"SYMBOL_CLICK":"SMALL_BUTTON_CLICK");}
  public void Tick(long now){if(!active||!App)return;SetAvailability(now);Screen.Show(Panel,now,App.Numbers.Value);if(AngleLabel)AngleLabel.text=Mathf.RoundToInt(Adjuster.Degrees)+"°";sound?.Tick(now);}
  public void FocusNext(int direction=1){if(Mechanisms.Length==0)return;focusIndex=(focusIndex+direction+Mechanisms.Length)%Mechanisms.Length;CameraRig.Focus(Mechanisms[focusIndex].transform.position);}
  public void FocusCurrent(){string focus=Panel.Obj("testRound").S("focusId","SCREEN");if(bindings.TryGetValue(focus,out var m))CameraRig.Focus(m.transform.position);else CameraRig.Focus(new Vector3(0,.49f,.12f),.7f);}
  public void CancelInput(){CameraRig?.Cancel();if(Mechanisms!=null)foreach(var m in Mechanisms)if(m)m.Release();}
  public static Vector3 SourceVector(JToken t)=>new Vector3(-(float)t.D("x"),(float)t.D("y"),(float)t.D("z"));
  public static Quaternion SourceRotation(JToken t){var q=Quaternion.AngleAxis((float)t.D("y"),Vector3.up)*Quaternion.AngleAxis((float)t.D("x"),Vector3.right)*Quaternion.AngleAxis((float)t.D("z"),Vector3.forward);return new Quaternion(q.x,-q.y,-q.z,q.w);}
  void SetPlacement(string id){if(!HatchMount||Hatch.PlacementId==id)return;foreach(var p in mounts.Arr("hatches"))if(p.S("id")==id){HatchMount.localPosition=SourceVector(p["position"]);HatchMount.localRotation=SourceRotation(p["rotation"]);var s=p["scale"];HatchMount.localScale=new Vector3((float)s.D("x",1),(float)s.D("y",1),(float)s.D("z",1));Hatch.PlacementId=id;Hatch.SetOpen(false,"");CameraRig.Cancel();break;}}
 }
}
