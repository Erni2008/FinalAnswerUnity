using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
namespace FinalAnswer.Tests
{
    public sealed class HostFlowTests
    {
        static JObject State(string phase) => new JObject { {"host",true},{"roomId","r"},{"code","ABCD23"},{"gameId","g"},{"phase",phase},{"phaseStartedAt",1000},{"answeringEndedAt",2000},{"question",new JObject{{"id","q"},{"text","Тест"},{"answers",new JArray("А","Б","В","Г")}}} };
        [Test] public void QuizWaitsForPresentationAndServerDeadlinesAndDeduplicates()
        {
            var flow=new HostFlow();var sent=new List<string>();var s=State("QUESTION_PRESENTATION");
            flow.Tick(s,1100,true,(t,p)=>sent.Add(t));Assert.IsEmpty(sent);
            flow.Tick(s,3000,true,(t,p)=>sent.Add(t));flow.Tick(s,3001,true,(t,p)=>sent.Add(t));Assert.That(sent,Is.EqualTo(new[]{"PRESENTATION_READY"}));
            s["phase"]="REVEALING";flow.Tick(s,2719,true,(t,p)=>sent.Add(t));Assert.AreEqual(1,sent.Count);
            flow.Tick(s,2720,true,(t,p)=>sent.Add(t));Assert.AreEqual("REVEAL_READY",sent[1]);
            s["phase"]="RESULTS";flow.Tick(s,4099,true,(t,p)=>sent.Add(t));Assert.AreEqual(2,sent.Count);
            flow.Tick(s,4100,true,(t,p)=>sent.Add(t));Assert.AreEqual("NEXT_READY",sent[2]);
            Assert.IsNull(s["question"]["correctAnswerIndex"],"Host flow never computes an answer");
        }
        [Test] public void CursedShotStagesRequireServerConfirmationAndActualDurations()
        {
            var s=State("LOBBY");var c=new JObject{{"sessionId","c"},{"hostReady",true},{"pendingShot",new JObject{{"id","shot"},{"startedAt",0},{"impactedAt",0}}}};s["cursedBoxes"]=c;
            var flow=new HostFlow();var sent=new List<string>();
            flow.Tick(s,2000,true,(t,p)=>{sent.Add(t);Assert.AreEqual(3,p.Count);});flow.Tick(s,4000,true,(t,p)=>sent.Add(t));Assert.That(sent,Is.EqualTo(new[]{"CURSED_SHOT_STARTED"}));
            var shot=c.Obj("pendingShot");shot["startedAt"]=2000;
            flow.Tick(s,2029,true,(t,p)=>sent.Add(t));Assert.AreEqual(1,sent.Count);
            flow.Tick(s,2030,true,(t,p)=>sent.Add(t));Assert.AreEqual("CURSED_SHOT_IMPACT",sent[1]);
            flow.Tick(s,5000,true,(t,p)=>sent.Add(t));Assert.AreEqual(2,sent.Count,"No completion before authoritative impact");
            shot["impactedAt"]=2030;flow.Tick(s,3099,true,(t,p)=>sent.Add(t));Assert.AreEqual(2,sent.Count);
            flow.Tick(s,3100,true,(t,p)=>sent.Add(t));Assert.AreEqual("CURSED_SHOT_COMPLETE",sent[2]);
        }
        [Test] public void PhoneCannotSendHostMilestonesAndUnreadySceneCannotAdvance()
        {
            var s=State("RESULTS");var flow=new HostFlow();int sends=0;
            flow.Tick(s,9000,false,(t,p)=>sends++);s["host"]=false;flow.Tick(s,9000,true,(t,p)=>sends++);Assert.Zero(sends);
        }
        [Test] public void CursedTakesPriorityOverQuizAndReturnsToQuizWithoutReset()
        {
            var s=State("RESULTS");s["cursedBoxes"]=new JObject();Assert.AreEqual(GameKind.CursedBoxes,HostFlow.Resolve(s,GameKind.LastCommand));
            s.Remove("cursedBoxes");Assert.AreEqual(GameKind.Quiz,HostFlow.Resolve(s,GameKind.LastCommand));
            s.Remove("gameId");Assert.AreEqual(GameKind.Impostor,HostFlow.Resolve(s,GameKind.Impostor));
        }
    }
}
