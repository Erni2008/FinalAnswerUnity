using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
namespace FinalAnswer
{
 public sealed class UIManager:IDisposable
 {
  readonly GameManager app;readonly TMP_FontAsset font;readonly GameObject root,lobby;readonly TMP_InputField server,name;
  readonly TMP_Text connection,notice,roomCode,joinUrl,mode,heading,description,readiness;
  readonly Button primary;readonly List<Button> modeButtons=new List<Button>();readonly List<GameObject> machineControls=new List<GameObject>();
  readonly TMP_Text[] seats=new TMP_Text[8];readonly Image[] seatFrames=new Image[8];
  readonly GameObject audioPanel,auditionButton;GameObject participantPicker;RawImage lobbyArt;MachineController machine;bool menuRequested,creating;int questionCount=5;
  JObject current;string phoneAddress="";GameKind selected;
  public bool LobbyVisible=>lobby.activeSelf;
  public bool Typing=>EventSystem.current&&EventSystem.current.currentSelectedGameObject&&EventSystem.current.currentSelectedGameObject.GetComponent<TMP_InputField>();
  public UIManager(GameManager owner,TMP_FontAsset f)
  {
   app=owner;font=f;
   root=new GameObject("Host PC — main display",typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));UnityEngine.Object.DontDestroyOnLoad(root);
   var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=10;
   var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1600,1000);scaler.matchWidthOrHeight=.5f;
   if(!EventSystem.current){var es=new GameObject("Input Events",typeof(EventSystem),typeof(InputSystemUIInputModule));UnityEngine.Object.DontDestroyOnLoad(es);es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();}
   lobby=UIFactory.Panel("Host room and lobby",root.transform,0,104,1600,896,new Color(.017f,.022f,.03f,.96f)).gameObject;
   var p=lobby.transform;
   heading=UIFactory.Text(p,font,"СОБЕРИТЕ СВОИХ ИГРОКОВ",56,48,1488,60,40);
   UIFactory.Text(p,font,"Название комнаты",56,135,700,35,18,new Color(.65f,.7f,.7f));
   name=UIFactory.Input(p,font,"Final Answer",56,175,740,58);name.characterLimit=48;
   readiness=UIFactory.Text(p,font,"Игроки подключаются через браузер на телефоне.",840,137,700,105,25,Color.white);
   string[] hints={"Вопросы, ответы на телефонах и общий счёт.\n2–8 игроков","Выберите укрытие или цель и испытайте удачу.\n2–8 игроков","Тайные роли, ассоциации и голосование.\n3–4 участника","Механическая машина и личные задания.\n2–8 игроков"};
   for(int i=0;i<4;i++){var kind=(GameKind)i;var button=UIFactory.Button(p,font,HostViewState.Name(kind),56+i%2*380,264+i/2*126,362,108,()=>app.SelectGame(kind));modeButtons.Add(button);}
   description=UIFactory.Text(p,font,hints[0],840,270,700,100,26,Color.white);
   primary=UIFactory.Button(p,font,"СОЗДАТЬ КОМНАТУ",840,390,704,108,Primary,new Color(.35f,.17f,.065f));
   UIFactory.Text(p,font,"УЧАСТНИКИ",56,530,1488,40,22);
   for(int i=0;i<8;i++){seatFrames[i]=UIFactory.Panel("Seat "+(i+1),p,56+i%4*378,581+i/4*112,360,96,new Color(.06f,.08f,.09f));seats[i]=UIFactory.Text(seatFrames[i].transform,font,"ЖДЁМ ИГРОКА",18,14,326,70,22,new Color(.6f,.66f,.66f));seats[i].enableAutoSizing=true;seats[i].fontSizeMin=17;seats[i].fontSizeMax=24;}
   Button settings=null;server=UIFactory.Input(p,font,PlayerPrefs.GetString("FA.Server","http://127.0.0.1:18096"),56,802,740,40);server.gameObject.SetActive(false);
   settings=UIFactory.Button(p,font,"АДРЕС СЕРВЕРА",56,849,240,34,()=>server.gameObject.SetActive(!server.gameObject.activeSelf));
   Button questions=null;questions=UIFactory.Button(p,font,"ВОПРОСОВ: 5",316,849,240,34,()=>{if(current!=null)return;questionCount=questionCount==5?8:questionCount==8?12:questionCount==12?1:questionCount==1?3:5;questions.GetComponentInChildren<TMP_Text>().text="ВОПРОСОВ: "+questionCount;});
   UIFactory.Button(p,font,"ЗАКРЫТЬ КОМНАТУ",1230,849,314,34,()=>{app.LeaveRoom();menuRequested=false;Refresh(null);});
   notice=UIFactory.Text(root.transform,font,"",56,905,1180,34,18,new Color(.85f,.76f,.61f));
   // Always-visible host header. Players never need to install Unity.
   UIFactory.Panel("Host header",root.transform,0,0,1600,104,new Color(.012f,.018f,.025f,.97f));
   UIFactory.Text(root.transform,font,"FINAL ANSWER",56,24,370,60,34);
   mode=UIFactory.Text(root.transform,font,"ЭКРАН ВЕДУЩЕГО",430,33,360,40,20);
   UIFactory.Button(root.transform,font,"КОМНАТА",790,28,170,48,()=>{menuRequested=!LobbyVisible;Refresh(current);});
   joinUrl=UIFactory.Text(root.transform,font,"Телефоны → браузер",988,19,365,70,19,Color.white);
   roomCode=UIFactory.Text(root.transform,font,"—",1368,16,195,73,40);
   connection=UIFactory.Text(root.transform,font,"Создайте комнату · игроки подключаются через браузер",56,83,700,19,12,new Color(.56f,.64f,.65f));
   foreach(var entry in new[]{("← УЗЕЛ",1275f,751f,(Action)(()=>machine?.FocusNext(-1))),("УЗЕЛ →",1411f,751f,(Action)(()=>machine?.FocusNext(1))),("К МЕХАНИЗМУ [F]",1275f,861f,(Action)(()=>machine?.FocusCurrent())),("ОБЩИЙ ВИД",1275f,916f,(Action)(()=>machine?.CameraRig?.Overview()))})machineControls.Add(UIFactory.Button(root.transform,font,entry.Item1,entry.Item2,entry.Item3,entry.Item1.Contains("УЗЕЛ")?124:260,45,entry.Item4).gameObject);
   audioPanel=UIFactory.Panel("Sound audition — manual QA",root.transform,380,120,850,790,new Color(.02f,.04f,.04f,.99f)).gameObject;
   UIFactory.Text(audioPanel.transform,font,"ПРОВЕРКА ЗВУКОВ · НЕ ВЛИЯЕТ НА ОЧКИ",20,18,800,45,24);int cueIndex=0;
   foreach(var id in app.Audio.AvailableIds){string cue=id;UIFactory.Button(audioPanel.transform,font,cue,20+cueIndex%4*205,80+cueIndex/4*59,195,49,()=>app.Audio.Preview(cue));cueIndex++;}
   UIFactory.Button(audioPanel.transform,font,"ЗАКРЫТЬ",20,710,800,55,()=>{app.Audio.StopPreview();audioPanel.SetActive(false);});audioPanel.SetActive(false);
   auditionButton=UIFactory.Button(root.transform,font,"ПРОВЕРИТЬ ЗВУКИ",1275,806,260,45,()=>audioPanel.SetActive(true)).gameObject;
   Refresh(null);
  }
  async void Primary()
  {
   if(HostViewState.HasGame(current)){menuRequested=false;Refresh(current);return;}
   if(current!=null){app.StartSelectedGame();return;}
   creating=true;primary.interactable=false;Notify("Создаю комнату…");
   try{PlayerPrefs.SetString("FA.Server",server.text);await app.Rooms.Create(server.text,name.text,8,questionCount);Notify("Комната создана. Ждём игроков.");}catch(Exception e){Notify(e.Message);}finally{creating=false;Refresh(app.Snapshots.Current);}
  }
  public void SetGame(GameKind value){selected=value;Refresh(current);}
  public void SetLobbyBackground(Texture texture){if(!texture)return;if(!lobbyArt){var rect=UIFactory.Rect("Original host lobby art",lobby.transform,0,0,1600,896);rect.SetAsFirstSibling();lobbyArt=rect.gameObject.AddComponent<RawImage>();lobbyArt.raycastTarget=false;}lobbyArt.texture=texture;lobbyArt.color=new Color(.40f,.40f,.40f,1);}
  public void SetMachine(MachineController value)=>machine=value;
  public void SetHosted()=>root.SetActive(false);
  public void SetConnection(string value){connection.text=value;}
  public void Notify(string value){notice.text=value;server.gameObject.SetActive(false);notice.gameObject.SetActive(true);}
  public void Tick(){notice.gameObject.SetActive(!server.gameObject.activeSelf);bool qa=ManualQa.Requested||app.GetComponent<ManualQa>();auditionButton.SetActive(qa);foreach(var control in machineControls)control.SetActive(selected==GameKind.LastCommand&&!LobbyVisible);}
  public void Refresh(JObject state)
  {
   if(state.S("roomId")!=current.S("roomId")||!HostViewState.HasGame(current)&&HostViewState.HasGame(state)){menuRequested=false;notice.text="";}
   current=state;bool active=HostViewState.HasGame(state);lobby.SetActive(!active||menuRequested);Tick();
   mode.text=HostViewState.Name(selected)+" · ВЕДУЩИЙ";
   roomCode.text=state==null?"—":state.S("code");
   if(state!=null){phoneAddress=HostViewState.JoinAddress(app.Rooms.BaseUrl,state.S("publicUrl"));joinUrl.text="Откройте на телефоне\n"+phoneAddress;}else joinUrl.text="Игроки заходят через браузер";
   heading.text=state==null?"СОБЕРИТЕ СВОИХ ИГРОКОВ":active?"КОМНАТА · "+state.S("name"):"ВСЕ НА МЕСТЕ? НАЧИНАЕМ!";
   name.interactable=state==null;
   string[] descriptions={"Вопросы, ответы на телефонах и общий счёт.\n2–8 игроков","Выберите укрытие или цель и испытайте удачу.\n2–8 игроков","Тайные роли, ассоциации и голосование.\n3–4 участника","Механическая машина и личные задания.\n2–8 игроков"};description.text=descriptions[(int)selected];
   int count=state.Arr("players").Count,online=state.Arr("players").Count(p=>p.S("connectionStatus")=="ONLINE");
   readiness.text=state==null?"Создай комнату, затем пригласи игроков.\nПриложение на телефон не требуется.":"Подключено: "+online+" / "+count+"\nКод комнаты: "+state.S("code");
   primary.GetComponentInChildren<TMP_Text>().text=state==null?"СОЗДАТЬ КОМНАТУ":active?"ВЕРНУТЬСЯ В ИГРУ":HostViewState.CanStart(state,selected)?"НАЧАТЬ ИГРУ":"ЖДЁМ ИГРОКОВ";
   primary.interactable=!creating&&(state==null||active||HostViewState.CanStart(state,selected)&&app.Network.Authenticated);
   for(int i=0;i<4;i++){modeButtons[i].interactable=!active;modeButtons[i].GetComponent<Image>().color=i==(int)selected?new Color(.35f,.17f,.065f):new Color(.07f,.1f,.12f);}
   var players=state.Arr("players");for(int i=0;i<8;i++){var p=i<players.Count?players[i]:null;bool connected=p.S("connectionStatus")=="ONLINE";seats[i].text=p==null?"МЕСТО "+(i+1)+"\nЖДЁМ ИГРОКА":p.S("name")+"\n"+(connected?"В КОМНАТЕ":"НЕТ СВЯЗИ")+" · "+p.L("score");seatFrames[i].color=p==null?new Color(.035f,.05f,.06f):connected?new Color(.07f,.18f,.16f):new Color(.20f,.10f,.07f);}
  }
  public void ChooseImpostorParticipants(Action<string[]> start)
  {
   if(participantPicker)UnityEngine.Object.Destroy(participantPicker);
   var panel=UIFactory.Panel("Impostor participant selection",root.transform,400,135,800,700,new Color(.02f,.035f,.045f,1));participantPicker=panel.gameObject;
   UIFactory.Text(panel.transform,font,"THE IMPOSTOR · ВЫБЕРИТЕ 3–4 ИГРОКОВ",24,20,750,50,26);var picked=new HashSet<string>();var status=UIFactory.Text(panel.transform,font,"Остальные участники останутся зрителями",24,79,750,45,18);Button accept=null;int row=0;
   foreach(var player in app.Players.Players){string id=player.Id,label=player.Name;Button button=null;button=UIFactory.Button(panel.transform,font,label,24,140+row++*53,750,45,()=>{if(picked.Contains(id))picked.Remove(id);else if(picked.Count<4)picked.Add(id);button.GetComponentInChildren<TMP_Text>().text=(picked.Contains(id)?"[+] ":"")+label;status.text="Выбрано: "+picked.Count+" / 4 · минимум 3";accept.interactable=picked.Count>=3;});}
   accept=UIFactory.Button(panel.transform,font,"НАЧАТЬ",410,612,364,56,()=>{var ids=picked.ToArray();UnityEngine.Object.Destroy(participantPicker);start(ids);});accept.interactable=false;UIFactory.Button(panel.transform,font,"ОТМЕНА",24,612,364,56,()=>UnityEngine.Object.Destroy(participantPicker));
  }
  public void Dispose(){if(root)UnityEngine.Object.Destroy(root);}
 }
}
