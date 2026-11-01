using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
namespace FinalAnswer.Tests
{
    public sealed class AllGamesIntegrationTests
    {
        GameManager app;
        string artifacts=>Path.GetFullPath("Artifacts");
        IEnumerator Until(Func<bool> condition,string reason,float timeout=25)
        {float start=Time.realtimeSinceStartup;while(!condition()){Assert.Less(Time.realtimeSinceStartup-start,timeout,reason);yield return null;}}
        JObject State=>app.Snapshots.Current;
        IEnumerator Room(string stage)
        {
            var work=app.Rooms.Create("http://127.0.0.1:18096","All games Unity QA",8,1);
            yield return Until(()=>work.IsCompleted,"HTTP create");Assert.IsFalse(work.IsFaulted,work.Exception?.ToString());
            yield return Until(()=>app.Network.Authenticated,"Host auth");
            File.WriteAllText(Path.Combine(artifacts,"allgames-room.json"),new JObject{{"stage",stage},{"code",app.Rooms.Code},{"url",app.Rooms.BaseUrl}}.ToString());
            yield return Until(()=>app.Players.Players.Count==3&&app.Players.Players.All(p=>p.Online),"Three browser phones",90);
        }
        [UnityTearDown]public IEnumerator Cleanup(){if(GameManager.Instance)UnityEngine.Object.Destroy(GameManager.Instance.gameObject);yield return null;}
        [UnityTest,Category("AllGamesIntegration")]
        public IEnumerator QuizCursedAndImpostorUseRealJavaAndUnmodifiedBrowserControllers()
        {
            Directory.CreateDirectory(artifacts);
            yield return UnityEngine.SceneManagement.SceneManager.LoadSceneAsync("GameHall");yield return null;
            app=GameManager.Instance;app.SelectGame(GameKind.Quiz);
            yield return Until(()=>app.Hall&&app.Hall.Ready,"Game hall meshes");
            yield return Room("quiz");Assert.IsTrue(app.UI.LobbyVisible,"PC host must see the player lobby before starting");Assert.AreEqual(GameKind.Quiz,app.SelectedGame);Assert.IsTrue(app.Rooms.IsHost);Assert.IsTrue(app.Network.Authenticated);app.Network.Message+=e=>{if(e.S("type")=="ERROR")Debug.Log("ALLGAMES_SERVER_ERROR: "+e.Obj("payload").S("code"));};app.StartSelectedGame();Debug.Log("ALLGAMES_START: "+app.SelectedGame+" "+app.Network.Status);
            yield return Until(()=>app.Audio.LoopPlaying,"Question typing starts with the printed text",20);
            yield return Until(()=>State.S("phase")=="ANSWERING","Question presentation acknowledgement",30);
            Assert.IsFalse(app.UI.LobbyVisible,"Host display must show the game, not its room menu");
            yield return null;Assert.IsFalse(app.Audio.LoopPlaying,"Typing stops when the presentation finishes");
            Assert.IsNull(State.Obj("question")["correctAnswerIndex"],"No answer key before validation");
            yield return Until(()=>State.S("phase")=="RESULTS","Authoritative quiz results",30);
            Assert.AreEqual(1,State.Arr("results").Count(x=>x.B("isCorrect")));
            Assert.AreEqual(100,app.Players.Players.Sum(p=>p.Score));
            yield return Until(()=>State.Obj("cursedBoxes")!=null,"Mixed results start cursed boxes");
            yield return Until(()=>State.Obj("cursedBoxes").B("hostReady"),"Original crate scene ready");
            Assert.AreEqual(16,State.Obj("cursedBoxes").Arr("boxes").Count);
            yield return Until(()=>State.Obj("cursedBoxes").S("phase")=="FINISHED","Phone hiding, shots and server reveal",65);
            Assert.Greater(State.Obj("cursedBoxes").L("destroyedCrateIds"),0);
            FrameCapture.Write(Camera.main,Path.Combine(artifacts,"allgames-cursed.png"));
            app.SendSession("CURSED_RETURN_TO_LOBBY","cursedBoxes");
            yield return Until(()=>State.S("phase")=="GAME_FINISHED"&&State.Obj("cursedBoxes")==null,"Return to same quiz session");
            Assert.AreEqual(100,app.Players.Players.Sum(p=>p.Score),"Cursed acknowledgements never award quiz points twice");
            var quizId=State.S("gameId");app.Rooms.Leave();yield return null;
            // Create a fresh real room; do not reset or mutate the Java match to force transitions.
            yield return Room("impostor");app.SelectGame(GameKind.Impostor);
            app.Network.Send("UPDATE_IMPOSTOR_SETTINGS",new JObject{{"roomCode",app.Rooms.Code},{"sessionId",State.S("lobbyId")},{"settings",new JObject{{"roundCount",1},{"discussionSeconds",5},{"votingSeconds",5},{"clueSeconds",15},{"wordCategory","DEFAULT"},{"soundEnabled",true},{"visualEffectsEnabled",true},{"reducedMotion",false}}}});
            yield return Until(()=>State.Obj("impostorSettings").L("roundCount")==1,"Real settings acknowledgement");
            app.StartSelectedGame();
            yield return Until(()=>State.Obj("impostor").S("phase")=="ROLE_REVEAL","Generated impostor scenario");
            Assert.IsNull(State.Obj("impostor")["secretWord"]);Assert.IsNull(State.Obj("impostor")["role"]);
            yield return Until(()=>State.Obj("impostor").S("phase")=="RESULT","Browser clue/suspicion/final vote and authoritative result",120);
            Assert.IsNotEmpty(State.Obj("impostor").S("revealedImpostorId"));
            Assert.IsNotNull(State.Obj("impostor")["result"]);
            FrameCapture.Write(Camera.main,Path.Combine(artifacts,"allgames-impostor.png"));
            File.WriteAllText(Path.Combine(artifacts,"allgames-unity-result.json"),new JObject{{"browserPlayers",3},{"quizCompleted",true},{"quizId",quizId},{"scoreAwardedOnce",100},{"mixedQuizCursedRoundTrip",true},{"impostorCompleted",true},{"hostDidNotReceivePrivateRole",true},{"impostorOutcome",State.Obj("impostor").S("outcome")}}.ToString());
            app.SendSession("IMPOSTOR_RETURN_TO_LOBBY","impostor");yield return Until(()=>State.Obj("impostor")==null,"Impostor return to lobby");
        }
    }
}
