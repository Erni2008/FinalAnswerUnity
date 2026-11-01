using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace FinalAnswer.Editor
{
 public static class HostUiBuilder
 {
  public static void Build(){GameHallBuilder.Build();foreach(string name in new[]{"Bootstrap","TheLastCommand","GameHall"}){var scene=EditorSceneManager.OpenScene("Assets/FinalAnswer/Scenes/"+name+".unity");foreach(var g in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include))if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(g)>0)throw new System.Exception("Missing component: "+g.name);foreach(var app in Object.FindObjectsByType<GameManager>(FindObjectsInactive.Include))EditorUtility.SetDirty(app);EditorSceneManager.SaveScene(scene);}Debug.Log("HOST_PC_UI_READY: no narrator character, independent phone controllers");}
 }
}
