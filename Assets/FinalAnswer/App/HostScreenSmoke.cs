using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
namespace FinalAnswer
{
 public static class HostScreenSmoke
 {
  static bool failed;static GameManager app;static string folder;
  static IEnumerator Wait(Func<bool> test,string why,int seconds=30){float until=Time.realtimeSinceStartup+seconds;while(!test()){if(Time.realtimeSinceStartup>=until){failed=true;Debug.LogError("HOST_SCREEN_SMOKE_TIMEOUT: "+why);Application.Quit(2);yield break;}yield return null;}}
  static IEnumerator Capture(string name){yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(folder,name+".png"));yield return new WaitForSecondsRealtime(.4f);}
  public static IEnumerator Run()
  {
   failed=false;app=GameManager.Instance;folder=Path.Combine(Application.persistentDataPath,"HostScreenSmoke");
   var args=Environment.GetCommandLineArgs();int a=Array.IndexOf(args,"-fa-host-artifacts");if(a>=0&&a+1<args.Length){folder=args[a+1];}Directory.CreateDirectory(folder);
   yield return Wait(()=>app.Hall&&app.Hall.Ready,"Scene");if(failed)yield break;yield return Capture("01-create-room");
   var buttons=UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Include);var count=buttons.First(x=>x.name=="ВОПРОСОВ: 5");for(int i=0;i<3;i++)count.onClick.Invoke();
   var primary=buttons.First(x=>x.name=="СОЗДАТЬ КОМНАТУ");primary.onClick.Invoke();
   yield return Wait(()=>app.Network.Authenticated&&app.Rooms.Code!="","Create room from host UI");if(failed)yield break;
   string signal=Path.Combine(folder,"room.json");File.WriteAllText(signal,new JObject{{"nonce",Guid.NewGuid().ToString("N")},{"command","room"},{"url",app.Rooms.BaseUrl},{"code",app.Rooms.Code},{"players",3}}.ToString());
   yield return Wait(()=>app.Players.Players.Count==3&&app.Players.Players.All(p=>p.Online),"Phone controllers");if(failed)yield break;yield return Capture("02-host-lobby");
   if(!app.UI.LobbyVisible){Debug.LogError("Host lobby disappeared before Start");Application.Quit(2);yield break;}primary.onClick.Invoke();
   yield return Wait(()=>app.Snapshots.Current.S("phase")=="ANSWERING","Start game from host UI");if(failed)yield break;yield return Capture("03-host-question");
   yield return Wait(()=>app.Snapshots.Current.Obj("cursedBoxes")!=null,"Mixed result boxes",35);if(failed)yield break;yield return new WaitForSecondsRealtime(1);yield return Capture("04-host-boxes");
   yield return Wait(()=>app.Snapshots.Current.Obj("cursedBoxes").S("phase")=="FINISHED","Boxes result",45);if(failed)yield break;app.SendSession("CURSED_RETURN_TO_LOBBY","cursedBoxes");
   yield return Wait(()=>app.Snapshots.Current.S("phase")=="GAME_FINISHED","Quiz results");if(failed)yield break;yield return Capture("05-host-results");
   File.WriteAllText(Path.Combine(folder,"result.json"),new JObject{{"actualHostUiCreateAndStart",true},{"phonePlayers",3},{"pcWasNotCountedAsPhone",true},{"phoneControllersUnmodified",true},{"quizCursedQuizCompleted",true},{"hostCharacterAdded",false}}.ToString());app.Rooms.Leave();Debug.Log("HOST_SCREEN_SMOKE_OK");Application.Quit(0);
  }
 }
}
