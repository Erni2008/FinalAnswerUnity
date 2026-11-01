using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Newtonsoft.Json.Linq;
namespace FinalAnswer
{
 public static class HostViewState
 {
  public static bool HasGame(JObject s)=>s.Obj("lastCommand")!=null||s.Obj("cursedBoxes")!=null||s.Obj("impostor")!=null||s.S("gameId")!="";
  public static bool CanStart(JObject s,GameKind kind)
  {
   if(s==null||!s.B("host")||HasGame(s))return false;
   var players=s.Arr("players");int minimum=kind==GameKind.Impostor?3:2;
   return players.Count>=minimum&&players.Count<=8&&players.All(p=>p.S("connectionStatus")=="ONLINE");
  }
  public static string JoinAddress(string baseUrl,string publicUrl="")
  {
   if(Uri.TryCreate(publicUrl,UriKind.Absolute,out var external)&&!external.IsLoopback)return publicUrl.TrimEnd('/');
   if(!Uri.TryCreate(baseUrl,UriKind.Absolute,out var uri))return baseUrl;
   if(!uri.IsLoopback)return baseUrl.TrimEnd('/');
   try {foreach(var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up&&(n.NetworkInterfaceType==NetworkInterfaceType.Ethernet||n.NetworkInterfaceType==NetworkInterfaceType.Wireless80211)))foreach(var address in nic.GetIPProperties().UnicastAddresses){var ip=address.Address;if(ip.AddressFamily==AddressFamily.InterNetwork&&!ip.ToString().StartsWith("169.254.")){var b=new UriBuilder(uri){Host=ip.ToString()};return b.Uri.ToString().TrimEnd('/');}}}catch(NetworkInformationException){}
   return baseUrl.TrimEnd('/');
  }
  public static string Name(GameKind game)=>game switch{GameKind.Quiz=>"ВОПРОСЫ",GameKind.CursedBoxes=>"CURSED BOXES",GameKind.Impostor=>"THE IMPOSTOR",_=>"THE LAST COMMAND"};
 }
}
