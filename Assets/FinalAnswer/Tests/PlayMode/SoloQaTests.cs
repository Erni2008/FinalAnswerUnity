using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
namespace FinalAnswer.Tests
{
 public sealed class SoloQaTests
 {
  GameManager app;
  JObject State=>app.Snapshots.Current;
  IEnumerator Until(Func<bool> condition,string message,float timeout=40){float end=Time.realtimeSinceStartup+timeout;while(!condition()){Assert.Less(Time.realtimeSinceStartup,end,message);yield return null;}}
  [UnityTearDown]public IEnumerator Cleanup(){if(GameManager.Instance)UnityEngine.Object.Destroy(GameManager.Instance.gameObject);yield return null;}
  [UnityTest,Category("SoloQaIntegration")]
  public IEnumerator OneDeveloperCanRunQuizStandaloneBoxesAndThreeRoundImpostorWithBots()
  {
   yield return UnityEngine.SceneManagement.SceneManager.LoadSceneAsync("Bootstrap");yield return null;app=GameManager.Instance;
   var qa=app.gameObject.AddComponent<ManualQa>();qa.SignalPathOverride=Path.GetFullPath("Artifacts/solo-qa-room.json");qa.QuestionCount=1;
   yield return Until(()=>State.Obj("lastCommand").Obj("testRound").S("status")=="PLAYING"&&!State.Obj("lastCommand").Obj("testRound").B("waitingForTerminals"),"Initial manual machine");yield return null;
   qa.SetBotMode("auto");var controls=qa.GetComponentsInChildren<Button>(true);
   controls.First(x=>x.name=="ВОПРОСЫ · СОЛО-ТЕСТ").onClick.Invoke();
   yield return Until(()=>State.S("gameId")!=""&&State.S("phase")=="GAME_FINISHED","Auto phones and host continue quiz/cursed back to quiz",100);
   Assert.AreEqual(3,app.Players.Players.Count);Assert.AreEqual(100,app.Players.Players.Sum(p=>p.Score));Assert.IsNull(State.Obj("cursedBoxes"));
   controls.First(x=>x.name=="ЯЩИКИ · СОЛО-ТЕСТ").onClick.Invoke();
   yield return Until(()=>State.Obj("cursedBoxes").S("phase")=="FINISHED","Standalone boxes must finish all four shots",75);
   Assert.AreEqual(4,State.Obj("cursedBoxes").L("presentedShotCount"));Assert.AreEqual(15,State.Obj("cursedBoxes").L("destroyedCrateIds"));
   controls.First(x=>x.name=="ИМПОСТОР · СОЛО-ТЕСТ").onClick.Invoke();
   yield return Until(()=>State.Obj("impostor").S("phase")=="RESULT","Three real clue rounds, suspicion and final voting",150);
   Assert.AreEqual(3,State.Obj("impostor").Arr("revealedClues").Count);Assert.IsNotEmpty(State.Obj("impostor").S("revealedImpostorId"));Assert.IsNotNull(State.Obj("impostor")["result"]);
   File.WriteAllText(Path.GetFullPath("Artifacts/solo-qa-unity-result.json"),new JObject{{"quizCompletedWithoutPhoneInput",true},{"standaloneShots",4},{"impostorRounds",3},{"serverConfirmedResult",true},{"players",3}}.ToString());
  }
 }
}
