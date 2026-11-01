# FinalAnswerUnity — ПК ведущего и браузерные игроки

**Ведущий — игрок за компьютером.** Обычная игра запускается через `HOST_GAME.command`: ПК создаёт комнату, показывает код и список игроков, запускает и отображает партию; участники отвечают через существующие браузерные контроллеры. Персонажа ведущего нет.

`SOLO_TEST.command` — отдельный запуск для одного разработчика с ботами и ручными заданиями. Руководство обычного ПК-интерфейса: [HOST_VIEW.md](HOST_VIEW.md).


Unity **6000.6.7f1 (Unity 6.6), Apple Silicon, URP 17.6.0**. Основная платформа — macOS arm64. Проект расположен отдельно от Java/libGDX; сервер и телефонный сайт остаются существующими.

## Оболочка ведущего (Assets/FinalAnswerShell)

Перенос Java/libGDX-интерфейса один к одному: главное меню, создание комнаты, код комнаты, лобби с клетками и
персонажами, переход старта, полный экран вопроса (комната, печать вопроса, ответы, готические часы, персонажи,
музыка/тиканье/замок/голос ведущего) и финальная таблица. Сцена `Assets/FinalAnswerShell/Scenes/Shell.unity` —
первая в сборке; существующие `Bootstrap`/`GameHall`/`TheLastCommand` не изменены.

- Отдельные сборки `FA.Contracts/Domain/Application/Infrastructure/UI/Quiz/CursedBoxes/App` (+ `FA.Shell.Editor`, `FA.Shell.Tests.EditMode`).
- Меню **Final Answer Shell → Configure Project** (импорт, каталог ассетов, сцена — без изменения URP и Player Settings),
  **Build macOS (Developer)** → `Builds/macOS-Shell-Dev/`.
- **F1** — соло-тест с встроенными ботами-телефонами, **F3** — отладка; `-fa-solo quiz|cursed|impostor|lastcommand|lobby`,
  `-fa-tour <папка>` — автоматический визуальный тур со скриншотами.
- Персонажи клеток — флипбуки, запечённые из оригинального Java-кода (`FinalAnswer/tools/unity/bake_cage_characters.sh`).
- Сервер ищется в соседней папке `../FinalAnswer` (или `FINAL_ANSWER_REPO`).
- **Звук и озвучка (все из Java):** UI, клетки, подвал; музыка викторины, тиканье, замок; Cursed Boxes (15 звуков:
  выстрел, затвор, гильза, дерево, сердцебиение, появления, раскрытие); The Impostor (9 звуков по фазам);
  голос ведущего — 12 вопросов, 26 событий, 24 реплики интро (`audio/host/ru/manifest.json`).
- **Подвальное интро** (74 с, субтитры, пропуск Esc/Space/Enter, пауза P) перед викториной; выключается в SETTINGS.
- **Cursed Boxes:** подвал, полка 4×4, ящики, вспышка/дым/щепки, найденные персонажи, итог с выжившими и призраками.
- **The Impostor:** стол, карточки участников с живыми персонажами, подсказки по раундам, таймер, штампы голосования,
  разоблачение самозванца, «Ещё раз» / «В лобби», озвучка ведущего по фазам.
- **The Last Command:** оригинальная 3D-машина (`Assets/FinalAnswer/Scenes/TheLastCommand`) открывается прямо из лобби.
  Сцена грузится поверх оболочки, её GameManager работает в режиме `Hosted` без своего сокета: команды и состояние
  идут через единственное соединение ведущего. Кнопки ОБЗОР / ДАЛЕЕ / ЗАВЕРШИТЬ; Tab, F, Esc работают как раньше.
  Боты в соло отвечают готовностью терминала, синхронизацией часов и случайными нажатиями.
  `-fa-end-after N` — ведущий сам завершает Last Command через N секунд (проверка возврата в лобби).
- F3 → «ЗВУК ▶ (ВСЕ)» перебирает каждый клип и каждую реплику ведущего; `-fa-shots <папка> -fa-shot-every N` — периодические скриншоты окна.

## Запуск

Открыть эту папку в установленном Unity 6000.6.7f1 либо выполнить:

```sh
./Tools/open-unity.sh
```

Открыть `Assets/FinalAnswer/Scenes/Bootstrap.unity`, нажать Play. Bootstrap запускает общий GameHall. Слева доступны **ВОПРОСЫ**, **CURSED BOXES**, **THE IMPOSTOR**, **LAST COMMAND**. Отдельная `TheLastCommand.unity` по-прежнему открывает машину для осмотра. В `Builds/macOS/FinalAnswerUnity.app` находится проверяемая desktop-сборка после выполнения build-команды.

1. Для автономного осмотра сервер не нужен: кнопки, переключатель, регулятор, люк и камера работают; очки в этом режиме не начисляются.
2. `Tools/run-java-server.sh` запускает существующую Gradle-задачу Java-сервера на порту 18096, если сервер ещё не отвечает. Скрипт не редактирует Java-исходники. Требуются соседняя папка FinalAnswer и её Java/Gradle-зависимости.
3. В консоли Unity указать URL сервера. **Создать комнату** → открыть сайт сервера на телефонах → подключить игроков → выбрать игру → **Начать с 2–8 телефонами**. Для THE IMPOSTOR нужны **3–4 участника**, как в исходном Java-сервере; остальные режимы рассчитаны на 2–8.
4. Телефонам нужен LAN IP компьютера, например `http://192.168.x.x:18096`, а не `127.0.0.1`. Компьютер и телефоны должны видеть сервер в сети.
5. **Локальный DEV-стенд** создаёт настоящую серверную development-сессию без телефонов. После подключения нажать кнопку старта. DEV-эндпойнт доступен только при `lastcommand.dev=true`; production-сервер закономерно отвечает 404.
6. **Проверить звуки** открывает отдельную ручную панель всех 36 клипов, включая четыре утверждённые голосовые команды. Она не начисляет очки. Это инструмент прослушивания, не игровой ответ.

Управление: ЛКМ — короткое взаимодействие / drag для обзора; регулятор — левая/правая половина и шаговый drag; ПКМ — сдвиг камеры; колесо — масштаб; F — к механизму задачи; Tab / Shift+Tab — следующий/предыдущий механизм; Esc — общий вид. В числовых задачах работают обычные цифры, numpad, минус, Backspace/Delete и Enter. Экранные цифры принимаются на pointerdown.

## Реализовано

- Реальные GameManager, SceneManager, MiniGameManager, RoundManager, PlayerManager, RoomManager, NetworkManager, AudioManager, UIManager и GameStateManager; единый lifetime приложения, сменяемый IMiniGame.
- HTTP form API и JSON/WebSocket v1 существующего Java RoomServer: create/join/auth, roster, snapshots, correlation requestId, отбрасывание устаревших revision, PING, reconnect, ошибки и ready/clock/visibility handshake Last Command.
- Персональный terminal snapshot; server-owned генерация, проверка, ошибки, замена задания и прогресс до 20. C# не вычисляет правильные ответы в runtime. Небольшой evaluator есть только в тестах для решения публичных математических выражений.
- Сцена из существующих GLB, независимые cap/lever/dial/door, скрытая записка, свет, выбор панелей/символов/животных, механический код, CRT world-space UI, pressure/radio/circuit/choice/number/sequence/reaction views.
- 12 mechanism bindings; RotatableMechanism, PressableButton, MechanicalSwitch, SlidingPanel, HiddenCompartment, IndicatorLight, InteractiveScreen, MachineController, плавная orbit/focus камера.
- Локальная numeric projection без ожидания сети; она не меняет авторитетное input/score. Semantic SFX, проверенные серверные verdict/victory, голос по spokenColor, память/свет, ограниченный пул, ручное прослушивание.
- Подтверждённые результаты в `Application.persistentDataPath/results.json`; идемпотентный ключ, запись через временный файл и atomic replace с `.bak`. Токены/пароли туда не записываются.
- URP: металл с отдельными производными roughness-картами, тени, янтарные/холодные акценты, умеренный Bloom/ACES, downsampled SSAO, render scale 0.9, ограничение 60 FPS.

## Структура

```text
Assets/FinalAnswer/
  App/                    приложение и managers
  Network/                протокол, транспорт, комнаты, input projection
  Games/Shared/           общий экран викторины, ящиков и расследования; host milestones
  Games/LastCommand/      механизмы, камера, terminal, audio event router
  UI/                     UI factory, подключения, sound audition
  Audio/                  26 SFX Last Command + 4 voice clips + 6 эффектов остальных игр
  Art/Models/             12 неизменённых medium GLB
  Art/Textures/           исходное оформление + отдельные derived карты
  Data/                   исходные конфиги, audio manifest, material calibration
  Fonts/                  NotoSans и созданный TMP asset
  Prefabs/CommandMachine.prefab
  Scenes/Bootstrap.unity, GameHall.unity, TheLastCommand.unity
  Editor/                 сборщик сцены, validator, macOS/Windows build commands
  Tests/                  EditMode и PlayMode
Tools/                    запуск, проверка импортов, browser regression
ImportedAssets.json       происхождение и SHA-256 63 скопированных файлов
Artifacts/                локальные отчёты и кадры проверок (не Git)
```

## Проверки

```sh
python3 Tools/verify-imports.py
# дополнительно сравнить с исходным checkout:
python3 Tools/verify-imports.py --compare-java ../FinalAnswer
```

Unity CLI (проект должен быть закрыт в другом экземпляре Editor):

```sh
UNITY='/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity'
"$UNITY" -batchmode -projectPath "$PWD" -runTests -testPlatform EditMode -testResults "$PWD/Artifacts/edit-tests.xml" -logFile "$PWD/Artifacts/edit-tests.log"
"$UNITY" -batchmode -projectPath "$PWD" -runTests -testPlatform PlayMode -testResults "$PWD/Artifacts/play-tests.xml" -logFile "$PWD/Artifacts/play-tests.log"
```

PlayMode включает настоящие Java integration, BrowserIntegration (два телефона Last Command) и AllGamesIntegration (три телефона для остальных игр). Запускайте соответствующий runner параллельно с нужной категорией/фильтром, либо исключайте её из автономного прогона. Для прежней BrowserIntegration: Перед запуском удалить устаревший `Artifacts/browser-room.json` и параллельно выполнить browser runner:

```sh
npm ci --prefix Tools
npm --prefix Tools run install-browser
npm --prefix Tools run check:phones
```

Для проверки только независимой кинематики добавить `-testFilter FinalAnswer.Tests.MachineTests`. Для серверной математики/30 task views: `-testFilter FinalAnswer.Tests.JavaIntegrationTests.JavaAuthorityAllTaskPresentationsAndTwentyCorrectAnswers`.

Сборка: меню **Final Answer → Build macOS Apple Silicon**; полная регенерация сцен и сборка через CLI `-executeMethod FinalAnswer.Editor.BuildPlayer.PrepareAndBuild -batchmode -quit`. Windows x64 предусмотрен отдельной build-командой, но требует Windows Build Support установленного Editor. На этом Mac модуль пока отсутствует; Windows build/run не проверены.

Standalone smoke: запустить player с `-fa-smoke -screen-fullscreen 0 -screen-width 1600 -screen-height 1000`. Он соединяется с dev-сервером, сохраняет PNG и аппаратный отчёт в `Application.persistentDataPath/Smoke` и завершает процесс. Обычный запуск без флага не выполняет автоматических действий/выхода.

## Границы этой итерации

Это рабочий перенос первого модуля, не законченная миграция всей игры. Полные прохождения всех семейств, RECALL после отложенной памяти, точность реакции на разных задержках, стресс на восьми физических телефонах и длительный reconnect пока не покрыты полностью. Все 30 активных семейств получены от реального сервера и прошли проверку построения представления; полная серия до 20 автоматически проверена на MATH. Не возвращены отключённые BALANCE и другие excluded families.

Cursed Boxes, Impostor, полный quiz/intro/lobby UI, classic/inspection-specific экранные сценарии, автоматическая упаковка Java server/JRE и полноценный release pipeline остаются следующими этапами. Шейдеры CRT/пара/искр не заявляются как точная копия GLSL/Three.js версии. Сам сервер не встроен в Unity player — его нужно запустить отдельно или указать внешний URL.

Голос — готовые утверждённые OGG. ОС TTS/внешний runtime-синтез не используется. QUESTION_TYPING/ALT доступны для ручного прослушивания; typewriter-анимация Last Command не выдумана. Наличие клипа и успешный вызов AudioSource не заменяют проверку слышимости на конкретном аудиоустройстве.

## Остальные игры

`GameHall` показывает реальные серверные состояния основной викторины, CURSED BOXES и THE IMPOSTOR. В викторине Unity печатает вопрос/варианты, подтверждает завершение представления, показывает серверный таймер и результат. Смешанный исход запускает серверные ящики, после их завершения кнопка «Продолжить» возвращает к той же викторине. Для ящиков используются исходные intact/destroyed GLB, 16 позиций и реальные этапы выстрела. В THE IMPOSTOR общий экран показывает ассоциации, обсуждение, публичные голоса и результат; приватные роли остаются на телефонах.

Это функциональный перенос представления с прежним Java-сервером, а не завершённый художественный паритет Java-экранов. Постановка дробовика, персонажи/призраки и полноценные кинематографические переходы ещё требуют переноса. Актуальные результаты проверок — в `MULTIGAME_STATUS.md`.

Сквозная проверка остальных игр (Java-сервер должен отвечать на 18096):

```sh
# Удалить только устаревший Artifacts/allgames-room.json перед новым прогоном.
node Tools/check-allgames-browser.cjs
# В другом терминале, пока runner ждёт комнату:
"$UNITY" -batchmode -projectPath "$PWD" -runTests -testPlatform PlayMode -testFilter FinalAnswer.Tests.AllGamesIntegrationTests -testResults "$PWD/Artifacts/allgames-play.xml" -logFile "$PWD/Artifacts/allgames-play.log"
```

Runner использует неизменённые HTML/CSS/JS контроллеры Java-сервера и реальные DOM-кнопки, не вызывает внутренние игровые функции страницы.

Для совместного запуска Unity-теста и трёх браузеров используйте `./Tools/run-allgames-tests.sh` (Editor должен быть закрыт).

## Один разработчик без других игроков

Откройте `SOLO_TEST.command`. В ручном стенде доступны «Я + боты», «Все автоматически», «Все вручную», выбор ручного игрока, состава и профиля ответов, отключение/возврат контроллера. Полное руководство и фактически выполненные проверки — в [MANUAL_QA.md](MANUAL_QA.md).