using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
namespace FinalAnswer.Tests
{
 public sealed class ManualQaTests
 {
  [UnityTearDown]public IEnumerator Cleanup(){if(GameManager.Instance)Object.Destroy(GameManager.Instance.gameObject);yield return null;}
  [UnityTest,Category("Integration")]
  public IEnumerator ManualPickerConnectsToJavaAndSwitchesTasksWithoutAwardingProgress()
  {
   yield return UnityEngine.SceneManagement.SceneManager.LoadSceneAsync("Bootstrap");yield return null;
   var app=GameManager.Instance;var qa=app.gameObject.AddComponent<ManualQa>();float until=Time.realtimeSinceStartup+30;
   while(!app.Network.Authenticated||app.Snapshots.Current.Obj("lastCommand").Obj("testRound").S("status")!="PLAYING"||app.Snapshots.Current.Obj("lastCommand").Obj("testRound").B("waitingForTerminals")){Assert.Less(Time.realtimeSinceStartup,until);yield return null;}
   yield return null;Assert.IsTrue(app.Snapshots.Current.Obj("lastCommand").B("development"));
   Assert.AreEqual(30,ManualQa.Tasks.Length);
   var buttons=qa.GetComponentsInChildren<Button>(true);
   buttons.First(x=>x.name=="11. Давление").onClick.Invoke();
   while(app.Snapshots.Current.Obj("lastCommand").Obj("testRound").S("taskType")!="PRESSURE"){Assert.Less(Time.realtimeSinceStartup,until);yield return null;}
   yield return null;Assert.AreEqual(0,app.Snapshots.Current.Obj("lastCommand").Obj("testRound").L("step"));
   buttons.First(x=>x.name=="29. Противоречивые команды").onClick.Invoke();
   while(app.Snapshots.Current.Obj("lastCommand").Obj("testRound").S("taskType")!="FALSE_COMMAND"){Assert.Less(Time.realtimeSinceStartup,until);yield return null;}
   Assert.AreEqual(0,app.Snapshots.Current.Obj("lastCommand").Obj("testRound").L("step"));
   Assert.AreEqual(36,app.Audio.AvailableIds.Count());
  }
 }
}
