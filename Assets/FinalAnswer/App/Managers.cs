using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace FinalAnswer {
 public enum AppState {Inspecting,Connecting,Lobby,Playing,Finished,Disconnected}
 public sealed class GameStateManager {
  public AppState Current{get;private set;}=AppState.Inspecting;public event Action<AppState> Changed;
  public void Set(AppState state){if(state==Current)return;Current=state;Changed?.Invoke(state);}
  public void Observe(JObject state){if(state.Obj("lastCommand") is JObject p)Set(p.Obj("testRound").S("status")=="WON"?AppState.Finished:AppState.Playing);else if(state.Obj("cursedBoxes")!=null||state.Obj("impostor")!=null)Set(AppState.Playing);else Set(state.S("phase")=="GAME_FINISHED"?AppState.Finished:state.S("gameId")!=""?AppState.Playing:AppState.Lobby);}
 }
 public sealed class PlayerManager {
  public sealed class Player {public string Id,Name,Character;public int Score;public bool Online;}
  public IReadOnlyList<Player> Players=>players;readonly List<Player> players=new List<Player>();public event Action Changed;
  public void Apply(JObject state){players.Clear();foreach(var p in state.Arr("players"))players.Add(new Player{Id=p.S("id"),Name=p.S("name"),Character=p.S("characterId"),Score=(int)p.L("score"),Online=p.S("connectionStatus")=="ONLINE"});Changed?.Invoke();}
 }
 public sealed class RoundManager {
  public JObject Current{get;private set;}public event Action<JObject> TaskChanged;public event Action<JObject> Validated;string task="",verdict="";
  public void Apply(JObject panel){Current=panel.Obj("testRound");string next=panel.S("sessionId")+":"+Current.S("taskId");if(next!=task){task=next;verdict="";TaskChanged?.Invoke(Current);}if(Current.L("nextAt")>0){string key=task+":"+Current.S("feedback");if(key!=verdict){verdict=key;Validated?.Invoke(Current);}}}
 }
 public interface IMiniGame {string Id{get;}void Enter();void Apply(JObject snapshot);void Exit();}
 public sealed class MiniGameManager {
  readonly Dictionary<string,IMiniGame> games=new Dictionary<string,IMiniGame>();public IMiniGame Active{get;private set;}
  public void Register(IMiniGame game){games[game.Id]=game;}
  public void Activate(string id){games.TryGetValue(id,out var game);if(ReferenceEquals(Active,game))return;ExitActive();Active=game;Active?.Enter();}
  void ExitActive(){if(Active is UnityEngine.Object obj&&obj==null){Active=null;return;}Active?.Exit();Active=null;}
  public void Apply(JObject snapshot)=>Active?.Apply(snapshot);
  public void Clear(){ExitActive();games.Clear();}
 }
 public sealed class SceneManager {
  public bool Loading{get;private set;}public float Progress{get;private set;}
  public async Task LoadGame(string name){if(Loading)return;Loading=true;try{var operation=UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(name);while(!operation.isDone){Progress=operation.progress;await Task.Yield();}Progress=1;}finally{Loading=false;}}
 }
}
