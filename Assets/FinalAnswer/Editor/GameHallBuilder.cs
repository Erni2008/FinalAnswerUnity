using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
namespace FinalAnswer.Editor
{
    public static class GameHallBuilder
    {
        const string Root="Assets/FinalAnswer/";
        [MenuItem("Final Answer/Build Shared Game Hall")]
        public static void Build()
        {
            AssetDatabase.Refresh();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var hall=new GameObject("Final Answer — Quiz / Cursed Boxes / Impostor").AddComponent<GameHall>();
            hall.intactCrate=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"Art/CursedBoxes/intact.glb");
            hall.destroyedCrate=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"Art/CursedBoxes/destroyed.glb");
            if(!hall.intactCrate||!hall.destroyedCrate)throw new Exception("Original crate GLB import failed");
            hall.impostorBackground=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"Art/Impostor/background1.png");
            hall.quizBackground=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"Art/Stages/quiz.png");hall.lobbyBackground=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"Art/Stages/lobby.png");
            var backdrop=GameObject.CreatePrimitive(PrimitiveType.Quad);backdrop.name="Original Cursed Boxes room — backdrop";UnityEngine.Object.DestroyImmediate(backdrop.GetComponent<Collider>());backdrop.transform.position=new Vector3(0,1.47f,3);backdrop.transform.localScale=new Vector3(18,11,1);var backdropMat=AssetDatabase.LoadAssetAtPath<Material>(Root+"Materials/CursedBackdrop.mat");if(!backdropMat){backdropMat=new Material(Shader.Find("Universal Render Pipeline/Unlit"));AssetDatabase.CreateAsset(backdropMat,Root+"Materials/CursedBackdrop.mat");}backdropMat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"Art/Stages/cursed.png"));backdropMat.SetColor("_BaseColor",new Color(.65f,.65f,.65f));EditorUtility.SetDirty(backdropMat);backdrop.GetComponent<Renderer>().sharedMaterial=backdropMat;hall.cursedBackdrop=backdrop;backdrop.SetActive(false);
            hall.portraits=new[]{"skeleton","ghost","zombie","dracula","pumpkin","plague_doctor","voodoo","puppeteer"}.Select(id=>AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"Art/CursedBoxes/"+id+".png")).ToArray();
            hall.shotgun=Clip("shotgun_shoot_done");hall.pageTurn=Clip("page_turn");hall.stamp=Clip("wooden_stumb_hit");
            hall.voting=Clip("vote_started");hall.votingClosed=Clip("voting_closed");hall.roleReveal=Clip("impostor_role_reveal");
            var cam=new GameObject("Gallery camera",typeof(Camera),typeof(AudioListener)).GetComponent<Camera>();
            cam.tag="MainCamera";cam.transform.position=new Vector3(0,1.95f,-7);cam.transform.LookAt(new Vector3(0,1.95f,0));cam.fieldOfView=55;
            cam.rect=new Rect(0,0,1,1);cam.backgroundColor=new Color(.02f,.025f,.035f);cam.clearFlags=CameraClearFlags.SolidColor;
            var key=new GameObject("Gallery warm key").AddComponent<Light>();key.type=LightType.Directional;key.intensity=1.8f;key.color=new Color(1,.82f,.63f);key.transform.rotation=Quaternion.Euler(38,-30,0);key.shadows=LightShadows.Soft;
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.35f,.38f,.43f);
            var app=new GameObject("Final Answer application").AddComponent<GameManager>();
            app.font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Root+"Fonts/FinalAnswerFont.asset");app.audioCatalog=AssetDatabase.LoadAssetAtPath<AudioCatalog>(Root+"Data/AudioCatalog.asset");
            var catalog=AssetDatabase.LoadAssetAtPath<AudioCatalog>(Root+"Data/AudioCatalog.asset");
            var extra=new[]{("CURSED_SHOT",hall.shotgun),("IMPOSTOR_PAGE",hall.pageTurn),("IMPOSTOR_STAMP",hall.stamp),("IMPOSTOR_VOTE_OPEN",hall.voting),("IMPOSTOR_VOTE_CLOSED",hall.votingClosed),("IMPOSTOR_ROLE",hall.roleReveal)};
            var cues=catalog.cues.Where(c=>!extra.Any(e=>e.Item1==c.id)).ToList();foreach(var e in extra)cues.Add(new AudioCatalog.Cue{id=e.Item1,clip=e.Item2,gain=.7f,priority=e.Item1=="CURSED_SHOT"?3:2,throttleMs=100});catalog.cues=cues.ToArray();EditorUtility.SetDirty(catalog);
            EditorSceneManager.SaveScene(scene,Root+"Scenes/GameHall.unity");
            EditorBuildSettings.scenes=new[]{"Bootstrap","TheLastCommand","GameHall"}.Select(s=>new EditorBuildSettingsScene(Root+"Scenes/"+s+".unity",true)).ToArray();
            AssetDatabase.SaveAssets();Debug.Log("GAME_HALL_READY: original crate meshes, 3 host modes, existing Java contracts");
        }
        static AudioClip Clip(string name)
        {
            string path=Root+"Audio/OtherGames/"+name+".wav";
            var importer=(AudioImporter)AssetImporter.GetAtPath(path);if(!importer)throw new Exception("Missing sound "+path);
            var settings=importer.defaultSampleSettings;settings.loadType=name=="impostor_role_reveal"?AudioClipLoadType.Streaming:AudioClipLoadType.DecompressOnLoad;importer.defaultSampleSettings=settings;importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }
    }
}
