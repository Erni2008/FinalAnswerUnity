using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FinalAnswer
{
    // Explicit opt-in developer launch. Uses real rooms, original phone UI and the existing dev endpoint.
    public sealed class ManualQa : MonoBehaviour
    {
        public static bool Requested => Array.IndexOf(Environment.GetCommandLineArgs(),"-fa-manual-qa")>=0;
        GameManager app;
        GameObject canvas,menu;
        TMP_Text status,botStatus;
        Button modeButton,playerButton,countButton,answerButton;
        public string BotMode{get;private set;}="assist";
        public int ManualPlayer{get;private set;}
        public int PlayerCount{get;private set;}=3;
        string answerProfile="mixed";
        float nextPoll,lastContinue;
        int connectingPlayers;
        bool busy;
        string signal;
        public string SignalPathOverride;
        public int QuestionCount=3;
        public static readonly string[] Tasks={"MATH","NUMBER_SEQUENCE","COMPARISON","FORBIDDEN","ODD_SYMBOL","DECIPHER","POSITION","BUTTON","CIRCUIT","LEVER_SEQUENCE","PRESSURE","RADIO","FAULTY_GAUGE","LEVER","ADJUSTER","HATCH","COLOR","ENTER_CODE","ANIMAL","SYMBOL","REVERSE","SHUTDOWN","NUMBER_MEMORY","ANIMAL_MEMORY","LIGHT_MEMORY","FIND_CHANGE","TIMING","SILENCE","FALSE_COMMAND","DELAYED_EXECUTION"};
        static readonly string[] Names={"Математика","Числовой ряд","Сравнение","Запрещённая кнопка","Лишний символ","Расшифровка","Позиция","Кнопка","Электрическая цепь","Последовательность рычага","Давление","Радио","Сломанный прибор","Рычаг","Угол регулятора","Люк","Цвет","Ввод кода","Животные","Символы","Обратный порядок","Отключение","Память чисел","Память животных","Память ламп","Найди изменение","Точный момент","Тишина","Противоречивые команды","Отложенная команда"};
        IEnumerator Start()
        {
            app=GameManager.Instance;
            signal=string.IsNullOrEmpty(SignalPathOverride)?Path.Combine(Application.persistentDataPath,"ManualQA","room.json"):SignalPathOverride;
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-fa-qa-signal");if(index>=0&&index+1<args.Length)signal=args[index+1];
            canvas=new GameObject("MANUAL QA — explicit developer tools",typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            canvas.transform.SetParent(transform,false);canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;canvas.GetComponent<Canvas>().sortingOrder=50;
            var scaler=canvas.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1600,1000);scaler.matchWidthOrHeight=.5f;
            UIFactory.Button(canvas.transform,app.font,"РУЧНЫЕ ТЕСТЫ",1260,30,300,52,()=>menu.SetActive(!menu.activeSelf));
            menu=UIFactory.Panel("Scenario picker",canvas.transform,365,100,1200,820,new Color(.018f,.028f,.038f,1)).gameObject;
            UIFactory.Text(menu.transform,app.font,"РУЧНОЙ СТЕНД · РЕАЛЬНЫЙ JAVA-СЕРВЕР",25,18,1125,45,28);
            status=UIFactory.Text(menu.transform,app.font,"Один разработчик: ваш контроллер + боты; режим можно менять в любой момент.",25,67,1125,65,20,Color.white);
            UIFactory.Button(menu.transform,app.font,"LAST COMMAND · БЕЗ ТЕЛЕФОНОВ",25,140,550,48,()=>Launch(GameKind.LastCommand));
            UIFactory.Button(menu.transform,app.font,"ВОПРОСЫ · СОЛО-ТЕСТ",600,140,550,48,()=>Launch(GameKind.Quiz));
            UIFactory.Button(menu.transform,app.font,"ЯЩИКИ · СОЛО-ТЕСТ",25,198,550,48,()=>Launch(GameKind.CursedBoxes));
            UIFactory.Button(menu.transform,app.font,"ИМПОСТОР · СОЛО-ТЕСТ",600,198,550,48,()=>Launch(GameKind.Impostor));
            modeButton=UIFactory.Button(menu.transform,app.font,"",25,260,360,44,()=>SetBotMode(BotMode=="assist"?"auto":BotMode=="auto"?"manual":"assist"));
            playerButton=UIFactory.Button(menu.transform,app.font,"",405,260,360,44,()=>{int count=app.Players.Players.Count>=2?app.Players.Players.Count:PlayerCount;ManualPlayer=(ManualPlayer+1)%count;WriteControls();});
            UIFactory.Button(menu.transform,app.font,"ОТКЛЮЧИТЬ / ВЕРНУТЬ ИГРОКА",785,260,360,44,()=>WriteControls("connection"));
            countButton=UIFactory.Button(menu.transform,app.font,"",25,314,360,44,()=>{PlayerCount=PlayerCount==3?4:PlayerCount==4?8:PlayerCount==8?2:3;WriteControls();});
            answerButton=UIFactory.Button(menu.transform,app.font,"",405,314,360,44,()=>{answerProfile=answerProfile=="mixed"?"correct":answerProfile=="correct"?"wrong":"mixed";WriteControls();});
            UIFactory.Button(menu.transform,app.font,"ПРОДОЛЖИТЬ ПОСЛЕ ЯЩИКОВ",785,314,360,44,()=>{if(app.Snapshots.Current.Obj("cursedBoxes").S("phase")=="FINISHED")app.SendSession("CURSED_RETURN_TO_LOBBY","cursedBoxes");});
            botStatus=UIFactory.Text(menu.transform,app.font,"",25,367,1125,40,18,Color.white);
            UIFactory.Text(menu.transform,app.font,"THE LAST COMMAND: нажмите задание для ручной проверки",25,410,1125,34,20);
            for(int i=0;i<Tasks.Length;i++){string task=Tasks[i];UIFactory.Button(menu.transform,app.font,(i+1)+". "+Names[i],25+i%6*190,451+i/6*47,180,40,()=>Preview(task));}
            UIFactory.Text(menu.transform,app.font,"Я + боты: управляйте выбранным игроком. Все авто: только наблюдайте. Все вручную: управляйте всеми окнами. Состав применяется при новом запуске режима. Звуки — справа внизу.",25,708,1125,86,18);
            WriteControls();
            yield return null;
            Launch(GameKind.LastCommand);
        }
        void Launch(GameKind kind){if(!busy)StartCoroutine(Connect(kind));}
        IEnumerator Connect(GameKind kind)
        {
            connectingPlayers=kind==GameKind.Impostor?Mathf.Clamp(PlayerCount,3,4):PlayerCount;ManualPlayer=Mathf.Min(ManualPlayer,connectingPlayers-1);WriteControls();
            busy=true;status.text="Подключение тестовой комнаты…";menu.SetActive(true);
            app.LeaveRoom();app.SelectGame(kind);
            if(kind==GameKind.LastCommand)Signal("close","");
            var work=kind==GameKind.LastCommand?app.Rooms.Development("http://127.0.0.1:18096"):app.Rooms.Create("http://127.0.0.1:18096","Ручная проверка Unity",8,Mathf.Clamp(QuestionCount,1,12));
            float deadline=Time.realtimeSinceStartup+45;
            while(!work.IsCompleted&&Time.realtimeSinceStartup<deadline)yield return null;
            if(!work.IsCompleted||work.IsFaulted){status.text="Комната не подключилась: "+(work.Exception?.GetBaseException().Message??"тайм-аут");busy=false;yield break;}
            while(!app.Network.Authenticated&&Time.realtimeSinceStartup<deadline)yield return null;
            if(!app.Network.Authenticated){status.text="Не удалось авторизоваться на Java-сервере";busy=false;yield break;}
            if(kind!=GameKind.LastCommand)
            {
                Signal("room",app.Rooms.Code);status.text="Подключаю тестовых игроков. Ваш контроллер и боты…";
                while((app.Players.Players.Count<connectingPlayers||app.Players.Players.Any(p=>!p.Online))&&Time.realtimeSinceStartup<deadline)yield return null;
                if(app.Players.Players.Count<connectingPlayers||app.Players.Players.Any(p=>!p.Online)){status.text="Контроллеры не подключились. Запустите Tools/manual-qa-phones.cjs или подключите браузеры к комнате "+app.Rooms.Code;busy=false;yield break;}
            }
            else
            {
                while((!app.Machine||!app.Machine.Ready)&&Time.realtimeSinceStartup<deadline)yield return null;
                if(!app.Machine){status.text="Сцена машины не загрузилась";busy=false;yield break;}
            }
            app.StartSelectedGame();
            if(kind==GameKind.LastCommand)
            {
                while((app.Snapshots.Current.Obj("lastCommand").Obj("testRound").S("status")!="PLAYING"||app.Snapshots.Current.Obj("lastCommand").Obj("testRound").B("waitingForTerminals")||app.Snapshots.Current.Obj("lastCommand").Obj("testRound").B("awaitingReady"))&&Time.realtimeSinceStartup<deadline)yield return null;
                if(app.Snapshots.Current.Obj("lastCommand").Obj("testRound").S("status")!="PLAYING"||app.Snapshots.Current.Obj("lastCommand").Obj("testRound").B("waitingForTerminals")){status.text="Сервер ещё не подтвердил готовность терминала. Повторите запуск стенда.";busy=false;yield break;}
                status.text="Машина готова. Выберите любое задание ниже; ответы и ошибки проверяет сервер.";
            }
            else {status.text="Режим запущен. Управляйте выбранным игроком; боты выполняют остальные действия. Режим ботов меняется сверху.";menu.SetActive(false);}
            busy=false;
        }
        void Preview(string task)
        {
            var panel=app.Snapshots.Current.Obj("lastCommand");
            if(busy||!app.Network.Authenticated||!panel.B("development")||panel.Obj("testRound").S("status")!="PLAYING"){status.text="Сначала запустите LAST COMMAND и дождитесь готовности.";return;}
            app.Action("CODE_PANEL","PREVIEW_TASK",task);status.text="Выбрано: "+task;menu.SetActive(false);StartCoroutine(FocusTask(task));
        }
        IEnumerator FocusTask(string task){float until=Time.realtimeSinceStartup+3;while(app.Snapshots.Current.Obj("lastCommand").Obj("testRound").S("taskType")!=task&&Time.realtimeSinceStartup<until)yield return null;yield return null;app.Machine?.FocusCurrent();}
        public void SetBotMode(string value){if(value!="assist"&&value!="auto"&&value!="manual")return;BotMode=value;WriteControls();}
        void WriteControls(string action="")
        {
            if(modeButton)modeButton.GetComponentInChildren<TMP_Text>().text=BotMode=="assist"?"Я + БОТЫ":BotMode=="auto"?"ВСЕ ИГРОКИ — АВТО":"ВСЕ ИГРОКИ — ВРУЧНУЮ";
            if(playerButton)playerButton.GetComponentInChildren<TMP_Text>().text="МОЙ ИГРОК: "+(ManualPlayer+1);
            if(countButton)countButton.GetComponentInChildren<TMP_Text>().text="СОСТАВ: "+PlayerCount+" · НОВЫЙ ЗАПУСК";
            if(answerButton)answerButton.GetComponentInChildren<TMP_Text>().text="ОТВЕТЫ БОТОВ: "+(answerProfile=="mixed"?"СМЕШАННЫЕ":answerProfile=="correct"?"ВЕРНЫЕ":"ОШИБОЧНЫЕ");
            if(string.IsNullOrEmpty(signal))return;
            WriteJson(signal+".control.json",new JObject{{"id",Guid.NewGuid().ToString("N")},{"mode",BotMode},{"manualPlayer",ManualPlayer},{"answers",answerProfile},{"action",action}});
        }
        void Update()
        {
            if(app==null||string.IsNullOrEmpty(signal))return;
            var state=app.Snapshots.Current;
            if(BotMode=="auto"&&app.Network.Authenticated&&state.S("gameId")!=""&&state.Obj("cursedBoxes").S("phase")=="FINISHED"&&Time.unscaledTime-lastContinue>2){lastContinue=Time.unscaledTime;app.SendSession("CURSED_RETURN_TO_LOBBY","cursedBoxes");}
            if(Time.unscaledTime<nextPoll)return;nextPoll=Time.unscaledTime+.5f;
            try {var s=JObject.Parse(File.ReadAllText(signal+".status.json"));if(s.S("room")!=app.Rooms.Code)return;int online=s.Arr("players").Count(p=>p.B("online"));botStatus.text=s.S("error")!=""?s.S("error"):"Подключено: "+online+" · "+string.Join("   ",s.Arr("players").Select(p=>(p.L("index")+1)+": "+(!p.B("online")?"ОФЛАЙН":p.B("bot")?"БОТ":"ВРУЧНУЮ")));}catch(IOException){}catch(Newtonsoft.Json.JsonException){}
        }
        void WriteJson(string path,JObject value){try{Directory.CreateDirectory(Path.GetDirectoryName(path));string tmp=path+".tmp";File.WriteAllText(tmp,value.ToString());if(File.Exists(path))File.Delete(path);File.Move(tmp,path);}catch(Exception e){if(status)status.text="Стенд: "+e.GetType().Name;}}
        void Signal(string command,string code)
        {
            try {Directory.CreateDirectory(Path.GetDirectoryName(signal));string tmp=signal+".tmp";File.WriteAllText(tmp,new JObject{{"nonce",Guid.NewGuid().ToString("N")},{"command",command},{"code",code},{"url",app.Rooms.BaseUrl},{"players",connectingPlayers}}.ToString());if(File.Exists(signal))File.Delete(signal);File.Move(tmp,signal);}
            catch(Exception e){status.text="Не удалось открыть контроллеры: "+e.GetType().Name;}
        }
    }
}
