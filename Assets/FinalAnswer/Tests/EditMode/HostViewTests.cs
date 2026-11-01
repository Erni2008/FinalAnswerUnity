using Newtonsoft.Json.Linq;
using NUnit.Framework;
namespace FinalAnswer.Tests
{
 public sealed class HostViewTests
 {
  static JObject Room(int count){var players=new JArray();for(int i=0;i<count;i++)players.Add(new JObject{{"id","p"+i},{"connectionStatus","ONLINE"}});return new JObject{{"host",true},{"players",players}};}
  [Test]public void HostIsNotCountedAsARequiredPhoneParticipant(){Assert.IsFalse(HostViewState.CanStart(Room(1),GameKind.Quiz));Assert.IsTrue(HostViewState.CanStart(Room(2),GameKind.Quiz));Assert.IsTrue(HostViewState.CanStart(Room(8),GameKind.Quiz));}
  [Test]public void OnlyHostWithOnlineRosterCanStartANewGame(){var s=Room(3);s["host"]=false;Assert.IsFalse(HostViewState.CanStart(s,GameKind.Quiz));s["host"]=true;s["players"][0]["connectionStatus"]="OFFLINE";Assert.IsFalse(HostViewState.CanStart(s,GameKind.Quiz));s=Room(3);s["cursedBoxes"]=new JObject();Assert.IsFalse(HostViewState.CanStart(s,GameKind.Quiz));}
  [Test]public void ImpostorNeedsThreePhonesAndAllowsSelectingParticipantsFromALargerRoom(){Assert.IsFalse(HostViewState.CanStart(Room(2),GameKind.Impostor));Assert.IsTrue(HostViewState.CanStart(Room(3),GameKind.Impostor));Assert.IsTrue(HostViewState.CanStart(Room(8),GameKind.Impostor));}
  [Test]public void PhoneAddressPreservesRemoteServerAndPublicUrl(){Assert.AreEqual("https://game.example",HostViewState.JoinAddress("http://127.0.0.1:18096","https://game.example/"));Assert.AreEqual("http://192.168.1.20:18096",HostViewState.JoinAddress("http://192.168.1.20:18096/"));}
 }
}
