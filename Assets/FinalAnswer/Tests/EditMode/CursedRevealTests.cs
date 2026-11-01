using Newtonsoft.Json.Linq;
using NUnit.Framework;
namespace FinalAnswer.Tests
{
 public sealed class CursedRevealTests
 {
  [Test] public void HostAssignmentsStayHiddenAndSurvivorsWaitForTheirScheduledReveal()
  {
   var p=new JObject{{"hostContents",new JArray(new JObject{{"boxId",1},{"nickname","secret"}})},{"survivors",new JArray(new JObject{{"boxId",1},{"nickname","a"}},new JObject{{"boxId",4},{"nickname","b"}})},{"revealStartedAt",1000},{"revealTimings",new JObject{{"anticipation",1},{"smoke",1},{"panel",.5},{"character",.5},{"idle",1},{"gap",1}}}};
   Assert.IsNull(CursedReveal.Occupant(p,1,2999));Assert.AreEqual("a",CursedReveal.Occupant(p,1,3000).S("nickname"));Assert.IsNull(CursedReveal.Occupant(p,4,5999));Assert.AreEqual("b",CursedReveal.Occupant(p,4,6000).S("nickname"));
   p.Remove("survivors");Assert.IsNull(CursedReveal.Occupant(p,1,9999));
  }
 }
}
