using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;
namespace FinalAnswer {
 public sealed class RoomManager {
  readonly NetworkManager network;public string BaseUrl{get;private set;}="http://127.0.0.1:18096";public string Code{get;private set;}="";public string SelfId{get;private set;}="";public bool IsHost{get;private set;}
  public event Action<JObject> CredentialsReceived;
  public RoomManager(NetworkManager n){network=n;}
  public async Task<JObject> Post(string path,Dictionary<string,string> data){using(var request=UnityWebRequest.Post(BaseUrl.TrimEnd('/')+path,data)){var op=request.SendWebRequest();while(!op.isDone)await Task.Yield();if(request.result!=UnityWebRequest.Result.Success){string reason=request.downloadHandler?.text;try{reason=JObject.Parse(reason).S("error",request.error);}catch{reason=request.error;}throw new InvalidOperationException(reason);}return JObject.Parse(request.downloadHandler.text);}}
  public async Task Create(string url,string name,int players=8,int questionCount=5){BaseUrl=Validate(url);Accept(await Post("/rooms",new Dictionary<string,string>{{"name",name},{"maxPlayers",players.ToString()},{"gameMode","standard"},{"questionCount",questionCount.ToString()},{"password",""}}));}
  public async Task Join(string url,string code,string name,string password=""){BaseUrl=Validate(url);Accept(await Post("/rooms/"+Uri.EscapeDataString(code.Trim().ToUpperInvariant())+"/join",new Dictionary<string,string>{{"name",name},{"password",password}}));}
  public async Task Development(string url){BaseUrl=Validate(url);Accept(await Post("/last-command-dev/join",new Dictionary<string,string>{{"scenario","campaign"},{"seed","20261025"}}));}
  void Accept(JObject data){Code=data.S("code");SelfId=data.S("selfId");IsHost=data.B("host");CredentialsReceived?.Invoke(data);network.Connect(BaseUrl,Code,data.S("token"));}
  public void StartLastCommand(JObject state){network.Send("START_LAST_COMMAND",new JObject{{"roomCode",Code},{"sessionId",state.S("lobbyId")},{"seed","20261025"}});}
  public void EndLastCommand(JObject state){network.Send("END_LAST_COMMAND",new JObject{{"roomCode",Code},{"sessionId",state.Obj("lastCommand").S("sessionId")}});}
  public void Leave(){if(network.Authenticated)network.Send("LEAVE_ROOM",new JObject{{"roomCode",Code}});network.Disconnect();Code="";}
  static string Validate(string url){var u=new Uri(url);if((u.Scheme!="http"&&u.Scheme!="https")||!string.IsNullOrEmpty(u.UserInfo))throw new ArgumentException("Нужен HTTP(S) адрес сервера");return url.TrimEnd('/');}
 }
}
