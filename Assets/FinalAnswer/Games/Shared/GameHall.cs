using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FinalAnswer
{
    public sealed class GameHall : MonoBehaviour
    {
        public GameObject intactCrate, destroyedCrate;
        public Texture2D impostorBackground,quizBackground,lobbyBackground;public GameObject cursedBackdrop;
        public Texture2D[] portraits;
        public AudioClip shotgun, pageTurn, stamp, voting, votingClosed, roleReveal;
        public bool Ready { get; private set; }
        public GameKind Current { get; private set; }
        GameManager app;
        GameObject ui, board;
        TMP_Text title, subtitle, timer, body, footer;
        readonly TMP_Text[] answers = new TMP_Text[4];
        readonly Image[] answerCards = new Image[4];
        readonly RawImage[] occupants = new RawImage[16];
        readonly string[] characterIds={"skeleton","ghost","zombie","dracula","pumpkin","plague_doctor","voodoo","puppeteer"};
        readonly TMP_Text[] crateLabels = new TMP_Text[16];
        readonly GameObject[] whole = new GameObject[16], broken = new GameObject[16];
        string presentationKey = "";
        long presentationStart;
        GameKind lastKind = (GameKind)(-1);
        JObject state;

        RawImage background;Image headerPlate;
        Button continueButton;

        public void Initialize(GameManager owner)
        {
            if(app)return;
            app = owner;app.UI.SetLobbyBackground(lobbyBackground);
            app.Audio.Register("CURSED_SHOT",shotgun,.7f,3);app.Audio.Register("IMPOSTOR_PAGE",pageTurn);app.Audio.Register("IMPOSTOR_STAMP",stamp);app.Audio.Register("IMPOSTOR_VOTE_OPEN",voting);app.Audio.Register("IMPOSTOR_VOTE_CLOSED",votingClosed);app.Audio.Register("IMPOSTOR_ROLE",roleReveal);
            ui = new GameObject("Shared host presentation", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = ui.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 1;
            var scaler = ui.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600,1000); scaler.matchWidthOrHeight = .5f;
            ui.transform.SetParent(transform, false);
            var surface = UIFactory.Panel("Stage", ui.transform, 0, 0, 1600, 1000, new Color(.02f,.025f,.035f,.97f));
            var bg = UIFactory.Rect("Original impostor backdrop", surface.transform, 0, 0, 1600, 1000);
            background = bg.gameObject.AddComponent<RawImage>(); background.texture = impostorBackground; background.color = new Color(.4f,.4f,.4f,1); background.raycastTarget = false;
            headerPlate=UIFactory.Panel("Header contrast",surface.transform,40,118,1520,195,new Color(.006f,.009f,.014f,.7f));
            title = UIFactory.Text(surface.transform, app.font, "FINAL ANSWER", 56, 126, 1400, 55, 40);
            subtitle = UIFactory.Text(surface.transform, app.font, "", 56, 198, 1420, 110, 28, Color.white);
            timer = UIFactory.Text(surface.transform, app.font, "", 1410, 128, 130, 60, 40);
            body = UIFactory.Text(surface.transform, app.font, "", 56, 335, 1488, 430, 29, new Color(.86f,.87f,.82f));
            for (int i=0;i<4;i++)
            {
                answerCards[i] = UIFactory.Panel("Answer " + i, surface.transform, 56+i%2*760, 430+i/2*140, 728, 115, new Color(.08f,.11f,.14f));
                answers[i] = UIFactory.Text(answerCards[i].transform, app.font, "", 22, 16, 680, 83, 28, Color.white);
            }
            footer = UIFactory.Text(surface.transform, app.font, "", 56, 820, 1150, 75, 22);
            continueButton = UIFactory.Button(surface.transform, app.font, "ПРОДОЛЖИТЬ", 1250, 820, 294, 64, Continue);
            BuildBoard(); Ready = intactCrate && destroyedCrate;
        }
        void BuildBoard()
        {
            board = new GameObject("CURSED BOXES — original GLB gallery"); board.transform.SetParent(transform,false);
            for(int i=0;i<16;i++)
            {
                var anchor = new GameObject("Crate " + (i+1)).transform; anchor.SetParent(board.transform,false);
                anchor.localPosition = new Vector3((i%4-1.5f)*1.65f, (3-i/4)*.98f, 0);
                whole[i] = Fit(intactCrate, anchor); broken[i] = Fit(destroyedCrate,anchor); broken[i].SetActive(false);
                var canvas = new GameObject("Crate number", typeof(Canvas)).GetComponent<Canvas>(); canvas.renderMode=RenderMode.WorldSpace;
                canvas.transform.SetParent(anchor,false); canvas.transform.localPosition=new Vector3(-.55f,-.32f,-.85f); canvas.transform.localScale=Vector3.one*.005f;
                var portrait=UIFactory.Rect("Publicly revealed occupant",canvas.transform,-20,-132,105,132).gameObject.AddComponent<RawImage>();portrait.raycastTarget=false;occupants[i]=portrait;portrait.gameObject.SetActive(false);
                crateLabels[i] = UIFactory.Text(canvas.transform,app.font,(i+1).ToString(),0,0,150,70,40,Color.white);
            }
            board.SetActive(false);
        }
        static GameObject Fit(GameObject prefab, Transform parent)
        {
            var wrap = new GameObject("Normalized supplied mesh"); wrap.transform.SetParent(parent,false);
            if(!prefab) return wrap;
            var mesh = Instantiate(prefab, wrap.transform); var rs=mesh.GetComponentsInChildren<Renderer>();
            var bounds=rs.Length>0?rs[0].bounds:new Bounds(Vector3.zero,Vector3.one);
            foreach(var renderer in rs)bounds.Encapsulate(renderer.bounds);
            var center=wrap.transform.InverseTransformPoint(bounds.center);mesh.transform.localPosition-=center;
            wrap.transform.localScale=new Vector3(1.4f/Mathf.Max(.001f,bounds.size.x),.90f/Mathf.Max(.001f,bounds.size.y),.55f/Mathf.Max(.001f,bounds.size.z));
            return wrap;
        }
        public void Apply(JObject snapshot, GameKind kind) { state=snapshot; Current=kind; }
        public void Tick(long now)
        {
            if(!app) return;ui.SetActive(!app.UI.LobbyVisible);
            if(Current!=lastKind) { app.Audio.StopTransient(); app.Audio.Protected=false; lastKind=Current; }
            body.enableAutoSizing=false;body.fontSize=27;
            background.texture=Current==GameKind.Impostor?impostorBackground:Current==GameKind.Quiz&&state.S("gameId")!=""?quizBackground:lobbyBackground;
            background.color=new Color(.57f,.57f,.57f,1);
            if(Current!=GameKind.Quiz||state.S("phase")!="QUESTION_PRESENTATION")app.Audio.SetLoop("QUESTION_TYPING",false);
            bool boxes=Current==GameKind.CursedBoxes && state.Obj("cursedBoxes")!=null;
            board.SetActive(boxes&&!app.UI.LobbyVisible);if(cursedBackdrop)cursedBackdrop.SetActive(boxes&&!app.UI.LobbyVisible);background.gameObject.SetActive(!boxes);headerPlate.gameObject.SetActive(!boxes);
            footer.rectTransform.anchoredPosition=new Vector2(56,boxes?-880:-820);continueButton.GetComponent<RectTransform>().anchoredPosition=new Vector2(1250,boxes?-880:-820);footer.rectTransform.sizeDelta=new Vector2(1150,75);
            // Cursed boxes remain visible below the compact header, instead of behind an opaque UI.
            var panel=title.transform.parent.GetComponent<Image>(); panel.color=new Color(.02f,.025f,.035f,boxes?.12f:.97f);
            foreach(var card in answerCards)card.gameObject.SetActive(false);
            continueButton.gameObject.SetActive(false); timer.text=""; body.text=""; footer.text="";
            if(state==null || state.S("roomId")=="") { Lobby(); return; }
            if(boxes) Boxes(now); else if(Current==GameKind.Impostor && state.Obj("impostor")!=null) Impostor(now);
            else if(Current==GameKind.Quiz && state.S("gameId")!="") Quiz(now); else Lobby();
        }
        void Lobby()
        {
            title.text=Current==GameKind.Impostor?"THE IMPOSTOR":Current==GameKind.CursedBoxes?"CURSED BOXES":"FINAL ANSWER";
            subtitle.text="Подключите телефоны через браузер и выберите игру слева.";
            body.text=Current==GameKind.Impostor?"3–4 игрока · тайные роли · ассоциации · обсуждение · голосование\n\nРоли и секретное слово показываются только на телефонах участников.":Current==GameKind.CursedBoxes?"2–8 игроков · 16 ящиков · последовательные выстрелы\n\nВыбор ящика и выстрел выполняются на телефоне. Результат определяет Java-сервер.":"2–8 игроков · вопросы · ответы на телефонах · серверный подсчёт очков\n\nПри смешанном результате раунда сервер запускает CURSED BOXES и затем возвращает партию к вопросам.";
        }
        void Quiz(long now)
        {
            string phase=state.S("phase"); var q=state.Obj("question");
            title.text=phase=="GAME_FINISHED"?"ИТОГИ FINAL ANSWER":"ВОПРОС "+state.L("questionNumber")+" / "+state.L("questionCount");
            if(phase=="STARTING") {subtitle.text="Приготовьтесь. Первый вопрос появится скоро.";Remaining(state.L("transitionEndsAt"),now);return;}
            if(phase=="GAME_FINISHED") {subtitle.text="Партия завершена";body.text=Scores();return;}
            string key=state.S("gameId")+":"+q.S("id");
            if(presentationKey!=key){presentationKey=key;presentationStart=state.L("phaseStartedAt");}
            string text=q.S("text");int budget=phase=="QUESTION_PRESENTATION"?Mathf.Max(0,(int)((now-presentationStart-350)*.035f)):int.MaxValue;
            subtitle.text=text.Substring(0,Math.Min(text.Length,budget));budget=Math.Max(0,budget-text.Length);
            int total=text.Length;foreach(var answer in q.Arr("answers"))total+=answer.ToString().Length;
            app.Audio.SetLoop("QUESTION_TYPING",app.Network.Authenticated&&phase=="QUESTION_PRESENTATION"&&now>presentationStart+350&&now<presentationStart+350+total*1000.0/35);
            var options=q.Arr("answers");
            for(int i=0;i<Math.Min(4,options.Count);i++)
            {
                string answer=options[i].ToString();int length=Math.Min(budget,answer.Length);budget=Math.Max(0,budget-answer.Length);
                answerCards[i].gameObject.SetActive(length>0);answers[i].text=(i+1)+". "+answer.Substring(0,length);
                bool correct=phase=="RESULTS"&&q.L("correctAnswerIndex",-1)==i;
                answerCards[i].color=correct?new Color(.12f,.35f,.23f):new Color(.08f,.11f,.14f);
            }
            footer.text=phase=="RESULTS"?"Результат подтверждён сервером · "+Scores("   "):phase=="ANSWERING"?"Выберите ответ на телефоне":"Подготовка вопроса…";
            Remaining(state.L("deadline"),now);
        }
        string Scores(string separator="\n") => string.Join(separator,app.Players.Players.OrderByDescending(p=>p.Score).Select(p=>p.Name+" — "+p.Score));
        void Boxes(long now)
        {
            var c=state.Obj("cursedBoxes");string phase=c.S("phase");title.text="CURSED BOXES";
            subtitle.text=phase switch {"HIDING_SELECTION"=>"Спрячьтесь в ящике на телефоне", "TARGET_SELECTION"=>"Выберите цель и подтвердите выстрел", "SELECTING"=>"Ход: "+c.S("activePlayerName"), "FIRING"=>"Выстрел", "ELIMINATION"=>"Результат выстрелов", "REVEALING"=>"Выжившие возвращаются", "FINISHED"=>"Испытание завершено", _=>"Подготовка сцены…"};
            long mask=c.L("destroyedCrateIds");
            for(int i=0;i<16;i++){
                bool hit=(mask&(1L<<i))!=0;var visible=CursedReveal.Occupant(c,i,now);bool reveal=visible!=null;
                whole[i].SetActive(!hit&&!reveal);broken[i].SetActive(hit||reveal);crateLabels[i].text=(i+1).ToString()+(reveal?" · "+visible.S("nickname"):"");crateLabels[i].fontSize=reveal?22:40;
                int portrait=Array.IndexOf(characterIds,visible.S("characterId"));occupants[i].gameObject.SetActive(reveal&&portrait>=0&&portraits!=null&&portrait<portraits.Length&&portraits[portrait]);if(portrait>=0&&portraits!=null&&portrait<portraits.Length)occupants[i].texture=portraits[portrait];
            }
            var shot=c.Obj("pendingShot");
            if(shot!=null&&shot.L("startedAt")>0){Sound(shotgun,c.S("sessionId")+":"+shot.S("id"));int id=(int)shot.L("boxId");if(id>=0&&id<16){float t=Mathf.Clamp01((now-shot.L("startedAt"))/180f);var anchor=whole[id].transform.parent;anchor.localPosition=new Vector3((id%4-1.5f)*1.65f,(3-id/4)*.98f,Mathf.Sin(t*Mathf.PI)*.10f);}}
            footer.text="Выстрелов осталось: "+c.L("remainingShotCount")+"   ·   Управление — на телефонах";
            if(phase=="FINISHED") {continueButton.gameObject.SetActive(true);footer.text="Выживших: "+c.L("survivorCount")+" · Можно продолжить партию";}
        }
        void Impostor(long now)
        {
            var p=state.Obj("impostor");string phase=p.S("phase"), session=p.S("sessionId");
            title.text="THE IMPOSTOR · РАУНД "+p.L("round");
            subtitle.text=phase switch {"ROLE_REVEAL"=>"Узнайте свою роль на телефоне", "ROUND_CLUE_INPUT"=>"Напишите ассоциацию на телефоне", "ROUND_CLUE_REVEAL"=>"Ассоциации участников", "PRELIMINARY_SUSPICION"=>"Обсудите и укажите подозреваемого", "EVIDENCE_REVIEW"=>"Сопоставьте все ассоциации", "FINAL_VOTING"=>"Финальное голосование на телефонах", "VOTING_LOCKED"=>"Голоса приняты", "FINAL_VOTE_REVEAL"=>"Результаты голосования", "SUSPECT_ANNOUNCEMENT"=>"Подозреваемый определён", "IMPOSTOR_REVEAL"=>"Раскрытие роли", "RESULT"=>"Итог расследования", "REPLAY_PREPARATION"=>"Готовность к новой партии", _=>phase};
            var lines=new List<string>();int round=0;
            foreach(var clues in p.Arr("revealedClues")){lines.Add("Раунд "+(++round));foreach(var player in p.Arr("participants")){string clue=clues.S(player.S("id"));if(clue!="")lines.Add(player.S("name")+": "+clue);}}
            var votes=p.Obj("finalVoteSnapshot").Obj("counts");if(votes!=null){lines.Add("Голоса:");foreach(var player in p.Arr("participants"))lines.Add(player.S("name")+": "+votes.L(player.S("id")));}
            string revealed=p.S("revealedImpostorId");if(revealed!="") {string name=p.Arr("participants").FirstOrDefault(x=>x.S("id")==revealed).S("name");lines.Add("Самозванец: "+name);lines.Add(p.S("outcome")=="IMPOSTOR_CAUGHT"?"Самозванец раскрыт":p.S("outcome"));}
            body.text=string.Join("\n",lines);body.enableAutoSizing=true;body.fontSizeMin=17;body.fontSizeMax=27;
            footer.text=phase=="FINAL_VOTING"?"Голосов: "+p.L("finalSubmittedCount")+" / "+p.L("finalEligibleCount"):"Ответов: "+p.L("submittedCount")+" / "+p.L("eligibleCount");
            Remaining(p.L("deadline"),now);
            AudioClip cue=phase=="ROLE_REVEAL"?roleReveal:phase=="FINAL_VOTING"?voting:phase=="VOTING_LOCKED"?votingClosed:phase=="IMPOSTOR_REVEAL"?stamp:phase=="ROUND_CLUE_REVEAL"?pageTurn:null;
            Sound(cue,session+":"+phase+":"+p.L("round"));
            if(phase=="RESULT"||phase=="REPLAY_PREPARATION")continueButton.gameObject.SetActive(true);
        }
        void Remaining(long deadline,long now) {timer.text=deadline>now?Math.Ceiling((deadline-now)/1000.0).ToString():"";}
        void Sound(AudioClip clip,string key) {if(!app.Network.Authenticated||!clip||(Current==GameKind.Impostor&&state.Obj("impostorSettings")?["soundEnabled"]!=null&&!state.Obj("impostorSettings").B("soundEnabled")))return;string id=clip==shotgun?"CURSED_SHOT":clip==pageTurn?"IMPOSTOR_PAGE":clip==stamp?"IMPOSTOR_STAMP":clip==voting?"IMPOSTOR_VOTE_OPEN":clip==votingClosed?"IMPOSTOR_VOTE_CLOSED":"IMPOSTOR_ROLE";app.Audio.Play(id,key);}
        void Continue()
        {
            if(Current==GameKind.CursedBoxes)app.SendSession("CURSED_RETURN_TO_LOBBY","cursedBoxes");
            else if(state.Obj("impostor").S("phase")=="RESULT")app.SendSession("IMPOSTOR_RETURN_TO_LOBBY","impostor");
            else app.StartSelectedGame();
        }
        void OnDestroy(){if(app) {app.Audio.StopPrefix("CURSED_");app.Audio.StopPrefix("IMPOSTOR_");}if(ui)Destroy(ui);}
    }
}
