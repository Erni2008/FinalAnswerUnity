using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
namespace FinalAnswer.Tests
{
 public sealed class GameHallTests
 {
  [UnityTearDown]public IEnumerator Cleanup(){if(GameManager.Instance)Object.Destroy(GameManager.Instance.gameObject);yield return null;}
  [UnityTest]public IEnumerator BootstrapAndModeSwitchingPreserveRealSceneBindings()
  {
   yield return UnityEngine.SceneManagement.SceneManager.LoadSceneAsync("Bootstrap");
   float until=Time.realtimeSinceStartup+15;
   while(!GameManager.Instance||!GameManager.Instance.Hall){Assert.Less(Time.realtimeSinceStartup,until);yield return null;}
   var app=GameManager.Instance;Assert.AreEqual(GameKind.Quiz,app.SelectedGame);Assert.IsTrue(app.Hall.Ready);
   Assert.IsNotNull(app.Hall.intactCrate);Assert.IsNotNull(app.Hall.destroyedCrate);Assert.AreEqual(8,app.Hall.portraits.Length);
   app.SelectGame(GameKind.Impostor);yield return null;Assert.AreEqual(GameKind.Impostor,app.Hall.Current);
   app.SelectGame(GameKind.CursedBoxes);yield return null;Assert.AreEqual(GameKind.CursedBoxes,app.Hall.Current);
   app.SelectGame(GameKind.LastCommand);
   while(!app.Machine){Assert.Less(Time.realtimeSinceStartup,until);yield return null;}
   Assert.IsTrue(app.Machine.Ready);app.SelectGame(GameKind.Quiz);
   while(!app.Hall){Assert.Less(Time.realtimeSinceStartup,until);yield return null;}
   Assert.IsTrue(app.Hall.Ready);Assert.IsNull(app.MiniGames.Active,"Destroyed Last Command module must be detached after scene switch");
  }
 }
}
