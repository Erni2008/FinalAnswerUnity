using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
namespace FinalAnswer {
 public sealed class NetworkManager:IDisposable {
  readonly ConcurrentQueue<(int,string)> inbox=new ConcurrentQueue<(int,string)>();readonly SemaphoreSlim sender=new SemaphoreSlim(1,1);
  ClientWebSocket socket;CancellationTokenSource cancel;int generation,attempt;long retryAt,lastPing;bool disposed;string url,code,token;
  Func<string,JObject,string,string> external;
  public bool Authenticated{get;private set;}public string Status{get;private set;}="Отключено";public event Action<JObject> Message;public event Action<string> StatusChanged;
  void State(string s){Status=s;StatusChanged?.Invoke(s);}
  public void Connect(string baseUrl,string room,string secret){url=baseUrl;code=room;token=secret;attempt=0;disposed=false;Open();}
  void Open(){CloseSocket();var g=++generation;Authenticated=false;State("Подключение…");cancel=new CancellationTokenSource();socket=new ClientWebSocket();_ = Receive(socket,cancel.Token,g);}
  async Task Receive(ClientWebSocket ws,CancellationToken ct,int g){try{await ws.ConnectAsync(new Uri(url.Replace("https://","wss://").Replace("http://","ws://").TrimEnd('/')+"/ws"),ct).ConfigureAwait(false);await SendRaw(ws,Envelope("AUTH",Guid.NewGuid().ToString("N"),new JObject{{"roomCode",code},{"token",token}}),ct).ConfigureAwait(false);var buffer=new byte[8192];while(!ct.IsCancellationRequested){using(var stream=new MemoryStream()){WebSocketReceiveResult part;do{part=await ws.ReceiveAsync(new ArraySegment<byte>(buffer),ct).ConfigureAwait(false);if(part.MessageType==WebSocketMessageType.Close)throw new IOException("Сервер закрыл соединение");if(part.MessageType!=WebSocketMessageType.Text)throw new IOException("Ожидалось JSON-сообщение");stream.Write(buffer,0,part.Count);if(stream.Length>1048576||inbox.Count>512)throw new IOException("Переполнена очередь сети");}while(!part.EndOfMessage);inbox.Enqueue((g,Encoding.UTF8.GetString(stream.ToArray())));}}}catch(Exception e){if(!ct.IsCancellationRequested)inbox.Enqueue((g,new JObject{{"type","LOCAL_DISCONNECTED"},{"payload",new JObject{{"message",e.GetType().Name}}}}.ToString()));}}
  static string Envelope(string type,string id,JObject payload)=>new JObject{{"type",type},{"requestId",id},{"payload",payload}}.ToString(Newtonsoft.Json.Formatting.None);
  async Task SendRaw(ClientWebSocket ws,string value,CancellationToken ct){await sender.WaitAsync(ct).ConfigureAwait(false);try{var bytes=Encoding.UTF8.GetBytes(value);await ws.SendAsync(new ArraySegment<byte>(bytes),WebSocketMessageType.Text,true,ct).ConfigureAwait(false);}finally{sender.Release();}}
  public string Send(string type,JObject payload=null,string requestId=null){if(!Authenticated)return "";if(external!=null)return external(type,payload??new JObject(),requestId)??"";string id=requestId??Guid.NewGuid().ToString("N");_ = SendSafe(socket,cancel.Token,generation,Envelope(type,id,payload??new JObject()));return id;}
  async Task SendSafe(ClientWebSocket ws,CancellationToken ct,int g,string body){try{await SendRaw(ws,body,ct).ConfigureAwait(false);}catch(Exception){if(!ct.IsCancellationRequested)inbox.Enqueue((g,"{\"type\":\"LOCAL_DISCONNECTED\",\"payload\":{}}"));}}
  public void Tick(){int budget=200;while(budget-->0&&inbox.TryDequeue(out var item)){if(item.Item1!=generation)continue;try{var e=JObject.Parse(item.Item2);string type=e.S("type");if(type=="AUTHENTICATED"){Authenticated=true;attempt=0;lastPing=Json.MonoMs;State("Подключено");}if(type=="LOCAL_DISCONNECTED"){Authenticated=false;CloseSocket();generation++;if(++attempt<=12){retryAt=Json.MonoMs+Math.Min(15000,500L<<Math.Min(attempt-1,5));State("Переподключение…");}else State("Соединение потеряно");}if(type=="ROOM_CLOSED"){Disconnect();State("Комната закрыта");}Message?.Invoke(e);}catch(Exception e){State("Ошибка JSON: "+e.GetType().Name);}}
   if(!disposed&&!Authenticated&&retryAt>0&&Json.MonoMs>=retryAt){retryAt=0;Open();}if(Authenticated&&Json.MonoMs-lastPing>=10000){lastPing=Json.MonoMs;Send("PING");}}
  void CloseSocket(){cancel?.Cancel();socket?.Abort();socket?.Dispose();socket=null;cancel?.Dispose();cancel=null;}
  // Hosted by the Final Answer shell: its socket carries every command, so this manager never opens one.
  public void AttachExternal(Func<string,JObject,string,string> send){Disconnect();external=send;Authenticated=send!=null;State(Authenticated?"Подключено":"Отключено");}
  public void Disconnect(){external=null;disposed=true;Authenticated=false;retryAt=0;generation++;CloseSocket();State("Отключено");}
  public void Dispose()=>Disconnect();
 }
}
