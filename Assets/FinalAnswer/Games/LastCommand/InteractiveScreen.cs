using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
namespace FinalAnswer {
 public sealed class InteractiveScreen:MonoBehaviour {
  public RectTransform Surface;public MachineController Owner;public TMP_FontAsset Font;[SerializeField]TMP_Text title,instruction,value,status;[SerializeField]RectTransform content;readonly List<Button> buttons=new List<Button>();readonly List<Image> wires=new List<Image>();string layout="",board="";JObject panel,round,touch;long serverNow;bool enabledInput;int signalIndex=-1;static readonly Key[] digitKeys={Key.Digit0,Key.Digit1,Key.Digit2,Key.Digit3,Key.Digit4,Key.Digit5,Key.Digit6,Key.Digit7,Key.Digit8,Key.Digit9};static readonly Key[] padKeys={Key.Numpad0,Key.Numpad1,Key.Numpad2,Key.Numpad3,Key.Numpad4,Key.Numpad5,Key.Numpad6,Key.Numpad7,Key.Numpad8,Key.Numpad9};public bool Built=>title;public string DisplayedValue=>value?value.text:"";
  public static string GaugeText(JObject touch)=>((touch.D("value")*touch.D("scale",1))+touch.D("offset")).ToString("F"+(int)touch.L("decimals",1),System.Globalization.CultureInfo.InvariantCulture)+" "+touch.S("unit","PSI");
  public void Initialize(TMP_FontAsset font){if(Built)return;Font=font;title=UIFactory.Text(Surface,font,"THE LAST COMMAND",28,18,968,42,30);instruction=UIFactory.Text(Surface,font,"Подключите Java-сервер через консоль слева. Машину можно осматривать и проверять автономно.",28,80,968,125,29);value=UIFactory.Text(Surface,font,"",28,203,968,50,33);content=UIFactory.Rect("Controls",Surface,24,270,976,285);status=UIFactory.Text(Surface,font,"READY / МЕХАНИЧЕСКИЙ ОСМОТР",28,573,968,40,22);}
  public void Show(JObject p,long now,string projected){if(!Built)return;panel=p;round=p.Obj("testRound");touch=round.Obj("touchscreen");serverNow=now;if(round==null){instruction.text="Подключите комнату к Java-серверу. Автономный осмотр не проверяет ответы и не начисляет очки.";return;}
   enabledInput=Owner.App.Network.Authenticated&&round.S("status")=="PLAYING"&&!round.B("paused")&&!round.B("waitingForTerminals")&&round.L("nextAt")==0&&now>=round.L("readyAt")&&now>=touch.L("revealUntil");
   string phase=touch.L("revealUntil")>now?"show":"input";if(round.S("taskType")=="FIND_CHANGE"&&now>=touch.L("revealUntil")-500&&now<touch.L("revealUntil"))phase="black";
   string key=round.S("taskId")+":"+touch.S("kind")+":"+phase;if(key!=layout){layout=key;BuildControls();}
   title.text="THE LAST COMMAND  /  "+round.L("localScore",round.L("step"))+" / 20";instruction.text=round.S("instruction");status.text=round.B("waitingForTerminals")?"ОЖИДАНИЕ ТЕРМИНАЛОВ":round.B("paused")?"ПАУЗА ТЕРМИНАЛА":round.L("nextAt")>0?round.S("feedback"):round.S("taskType")+" / "+phase.ToUpperInvariant();foreach(var b in buttons)b.interactable=enabledInput;
   string kind=touch.S("kind");value.text=kind=="number"?projected:kind=="pressure"?GaugeText(touch)+"  /  ← УМЕНЬШИТЬ · УВЕЛИЧИТЬ →":kind=="radio"?"ЧАСТОТА: "+touch.D("value").ToString("0.0")+" MHz":kind=="physical"?PhysicalSummary(panel,round):"";
   if(round.S("taskType")=="FALSE_COMMAND")value.text="ЭКРАН: "+touch.S("displayColor")+"    ГОЛОС: "+touch.S("spokenColor");
   if(kind=="timing"){double pos=(touch.D("start")+(now-round.L("readyAt"))/touch.D("period",4000)*100*touch.D("direction",1))%100;if(pos<0)pos+=100;value.text="ПОЗИЦИЯ: "+pos.ToString("00.0")+"   ЦЕЛЬ: "+string.Join("–",touch.Arr("range"));}
   if(kind=="silence")value.text=now>=touch.L("signalEndsAt")?"ТИШИНА · МОЖНО ПОДТВЕРДИТЬ":"ЖДИТЕ СИГНАЛА…";
   if(kind=="delayed")value.text="СИГНАЛ "+Math.Max(0,(now-round.L("readyAt"))/1000+1);
   if(kind=="circuit"&&board!=touch.Arr("board").ToString()){board=touch.Arr("board").ToString();DrawCircuit();}
   if(phase=="black"){instruction.text="";value.text="";content.gameObject.SetActive(false);}else content.gameObject.SetActive(true);
   if(kind=="sequence"&&touch.Arr("flashes").Count>0&&now<touch.L("revealUntil")){int idx=(int)((now-round.L("readyAt"))/850);signalIndex=idx;value.text=idx>=0&&idx<touch.Arr("flashes").Count?"СИГНАЛ: "+ColorName(touch.Arr("flashes")[idx].ToString()):"ЗАПОМНИТЕ";}
   if(Keyboard.current!=null&&enabledInput&&kind=="number"&&!Owner.App.UI.Typing){for(int i=0;i<=9;i++){var k=digitKeys[i];var nk=padKeys[i];if(Keyboard.current[k].wasPressedThisFrame||Keyboard.current[nk].wasPressedThisFrame)Owner.Touch("KEY",i.ToString());}if(Keyboard.current.minusKey.wasPressedThisFrame||Keyboard.current.numpadMinusKey.wasPressedThisFrame)Owner.Touch("KEY","-");if(Keyboard.current.backspaceKey.wasPressedThisFrame||Keyboard.current.deleteKey.wasPressedThisFrame)Owner.Touch("KEY","DELETE");if(Keyboard.current.enterKey.wasPressedThisFrame||Keyboard.current.numpadEnterKey.wasPressedThisFrame)Owner.Touch("CONFIRM");}
  }
  static string PhysicalSummary(JObject panel,JObject round){switch(round.S("taskType")){case "ADJUSTER":return "РЕГУЛЯТОР: "+panel.L("adjuster")+"°";case "LEVER":return "РЫЧАГ: "+panel.S("lever");case "HATCH":case "ENTER_CODE":return "КОД: "+panel.S("code");case "ANIMAL":return "ВЫБРАНО: "+panel.S("animal");case "SYMBOL":return "ВЫБРАНО: "+panel.S("symbol");case "COLOR":return "ИНДИКАТОР: "+panel.S("light");default:return "ПОДТВЕРДИТЕ БОЛЬШОЙ КНОПКОЙ";}}
  static string ColorName(string index)=>int.TryParse(index,out var i)&&i>=0&&i<4?new[]{"КРАСНЫЙ","ЗЕЛЁНЫЙ","СИНИЙ","ЖЁЛТЫЙ"}[i]:index;
  void Clear(){foreach(Transform c in content)Destroy(c.gameObject);buttons.Clear();wires.Clear();board="";}
  void Add(string text,float x,float y,float w,float h,Action action,Color? color=null,bool immediate=false){Action accepted=()=>{if(enabledInput)action();};var b=UIFactory.Button(content,Font,text,x,y,w,h,immediate?null:accepted,color);if(immediate)b.gameObject.AddComponent<ImmediatePress>().Accepted=accepted;buttons.Add(b);}
  void BuildControls(){Clear();string kind=touch.S("kind");if(kind=="number"){string[] keys={"7","8","9","4","5","6","1","2","3","-","0","DELETE"};for(int i=0;i<keys.Length;i++){string k=keys[i];Add(k=="DELETE"?"СТЕРЕТЬ":k,i%3*145,i/3*58,134,50,()=>Owner.Touch("KEY",k),null,true);}Add("ОЧИСТИТЬ",470,20,460,60,()=>Owner.Touch("KEY","CLEAR"));Add("ПОДТВЕРДИТЬ [ENTER]",470,118,460,80,()=>Owner.Touch("CONFIRM"));}
   else if(kind=="circuit"){for(int i=0;i<9;i++){int cell=i;Add("",300+i%3*86,i/3*80,76,70,()=>Owner.Touch("ROTATE",cell.ToString()));}Add("ПОДТВЕРДИТЬ ЦЕПЬ",16,150,265,63,()=>Owner.Touch("CONFIRM"));}
   else if(kind=="pressure"||kind=="radio"||kind=="balance"){Add("← УМЕНЬШИТЬ",20,30,310,70,()=>Owner.Touch("ADJUST","-1"));Add("УВЕЛИЧИТЬ →",350,30,310,70,()=>Owner.Touch("ADJUST","1"));Add("ПОДТВЕРДИТЬ",20,128,640,60,()=>Owner.Touch("CONFIRM"));}
   else if(touch.Arr("options").Count>0){var options=touch.Arr("options");int columns=options.Count>6?3:options.Count>2?2:1;float width=920/columns;for(int i=0;i<options.Count;i++){int index=i;string text=options[i].ToString();Add(text,i%columns*width,i/columns*64,width-12,55,()=>Owner.Touch(touch.B("physicalSelector")?"SELECT_PHYSICAL":"SELECT",touch.B("physicalSelector")?text:index.ToString()),new Color(.08f,.13f,.13f));}}
   else if(kind=="physical"||kind=="sequence"){UIFactory.Text(content,Font,"Используйте реальные детали машины.\nБольшая красная кнопка подтверждает положение.",20,20,920,130,26);}
   else if(kind!="store"&&kind!="waiting"){Add("ПОДТВЕРДИТЬ",100,60,700,90,()=>Owner.Touch("CONFIRM"));}
   if(touch.B("canRestartMemory"))Add("ПОВТОР ПАМЯТИ",600,216,350,54,()=>Owner.Touch("RESTART_MEMORY"));
  }
  void DrawCircuit(){foreach(var i in wires)if(i)Destroy(i.gameObject);wires.Clear();var cells=touch.Arr("board");int[] masks=new int[cells.Count];for(int n=0;n<cells.Count;n++)masks[n]=(int)cells[n];int powered=CircuitPresentation.Powered(masks);for(int i=0;i<cells.Count&&i<9;i++){int mask=(int)cells[i];for(int d=0;d<4;d++)if((mask&(1<<d))!=0){float x=300+i%3*86+38,y=i/3*80+35;float w=d%2==0?7:34,h=d%2==0?31:7;x+=d==3?-34:d%2==0?-3:0;y+=d==0?-31:d%2==1?-3:0;var segment=UIFactory.Panel("Contact",content,x,y,w,h,(powered&(1<<i))!=0?new Color(.3f,.82f,.65f):new Color(.32f,.36f,.34f));segment.raycastTarget=false;wires.Add(segment);}}}
 }
}
