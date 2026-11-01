> Исторический отчёт первого этапа THE LAST COMMAND. Продолжение по всем играм: [MULTIGAME_STATUS.md](MULTIGAME_STATUS.md).

# FinalAnswerUnity — результат первой реализации

## Созданный проект

- Путь: `/Users/erni/Documents/FinalAnswerUnity`, отдельно от исходного Java-проекта.
- Реальный установленный Editor: `/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity`, native arm64.
- Unity 6.6 / 6000.6.7f1; официальный URP-шаблон, URP 17.6.0; macOS arm64 Development Player.
- Сцены: `Assets/FinalAnswer/Scenes/Bootstrap.unity`, `Assets/FinalAnswer/Scenes/TheLastCommand.unity`.
- Сборка: `Builds/macOS/FinalAnswerUnity.app`. Команды сборки доступны в меню Final Answer.

## Системы и их фактическая работа

| Система | Реализованная ответственность |
|---|---|
| GameManager | Создание/владение сервисами, lifecycle, routing snapshots к активной сцене, server-ready/clock/visibility handshake |
| SceneManager | Асинхронная загрузка сцены и состояние загрузки |
| MiniGameManager | Registry IMiniGame, enter/apply/exit, замена экземпляра при повторной загрузке того же ID |
| RoundManager | Применение серверной задачи и однократное событие подтверждённого ответа; сдвиг deadline при resume не повторяет verdict |
| PlayerManager | Реальный roster из серверных snapshots, имена/персонажи/online/score |
| RoomManager | HTTP form create/join, credentials в памяти, start/end Last Command, отдельный dev endpoint |
| NetworkManager | Native ClientWebSocket, serialized send, text reassembly, bounded inbox, AUTH, PING, reconnect, generation fencing, ошибки |
| AudioManager | 30 зарегистрированных клипов, ограниченный пул, приоритеты/throttle/dedup, SFX volume, отдельное ручное прослушивание |
| UIManager | Рабочая консоль комнаты/URL/состава, навигация камеры, sound audition; это ещё не полный финальный menu/lobby UI |
| GameStateManager | Производное состояние приложения/подключения/лобби/игры/результата без второго серверного арбитра |

Подтверждённые исходы сохраняются в локальный `results.json` без токенов, через временный файл/atomic replace и backup. Активная комната по-прежнему живёт на Java-сервере; локальная история не заменяет серверное восстановление.

## Машина и импорт

Использованы 12 существующих medium GLB; корпус и механизмы не заменены кубами. `CommandMachine.prefab` содержит 12 mechanism bindings. Исходные transforms, позиции, оси и pivots адаптированы к X-reflection glTFast. Нота люка размещена в отдельной coordinate-space группе, чтобы authored rotation GLB не поворачивал её горизонтально.

Реализованы самостоятельные RotatableMechanism, PressableButton, MechanicalSwitch, SlidingPanel, HiddenCompartment, IndicatorLight, InteractiveScreen и MachineController; также CodeSelector и ChoicePanel. Кнопка имеет независимый ход/удержание/возврат, регулятор — шаги и текущий угол, рычаг — три положения, люк — шарнир/открытие/закрытие/скрытую записку. Взаимодействие камеры не превращается в отправку нажатия при orbit drag.

Камера: orbit, pan, zoom, плавный focus, overview, Tab/Shift+Tab/кнопки перехода по механизмам, консервативная защита от входа в корпус. Свет/материалы: реальные PBR-карты, отдельные derived roughness-карты и цветовые варианты caps, тени, янтарный/холодный свет, инспекционная подсветка, emission индикатора, Bloom/ACES/SSAO. Это первый художественный проход; точная GLSL/Three.js копия CRT/пара/искр не заявляется.

`ImportedAssets.json` и `Tools/verify-imports.py` проверяют SHA-256 49 исходных файлов: 12 GLB, 26 SFX, 4 голосовые записи, 4 конфига, 2 изображения и 1 TTF. Лицензия NotoSans скопирована дополнительно. Производные карты хранятся отдельно; исходные GLB/WAV/OGG/MP3 не перезаписаны.

## Сеть и игровой сценарий

Используется существующий Java RoomServer и существующий JSON/WebSocket protocol v1. Unity не создаёт новый сетевой протокол и не вычисляет runtime-правильные ответы.

Проверены персональные taskId и терминальные состояния; Numeric projection отображает ввод до ACK и не изменяет авторитетный input/score. Клавиши нажимаются на pointerdown; release не повторяет цифру. Механические кнопки заблокированы во время показа памяти и открываются по серверному reveal timestamp. Pressure display учитывает scale/offset/unit, physical selectors посылают SELECT_PHYSICAL, а не ошибочный choice-index submit.

Источник генерации/проверки всех семейств — Java CommandRace. Подключены представления number/choice/circuit/physical/sequence/store/pressure/radio/timing/silence/delayed и связанные состояния. Все 30 активных семейств получены с сервера и прошли smoke построения UI. Это не означает, что каждое семейство полностью пройдено человеком/автотестом. Полная автоматическая гонка до 20 выполнена на MATH. Отложенный RECALL работает через тот же payload renderer, но полный отложенный цикл ещё не подтверждён отдельным end-to-end тестом.

Телефоны остаются обычными браузерами, без Unity/приложения. Проверены два настоящих Chromium-контекста с неизменённым Java-сайтом, общим sessionId и разными taskId. Production-сессия проверена отдельно от разрешённого server development режима.

## Проверки и артефакты

Точные итоговые числа и результаты находятся в `Artifacts/validation.json`; XML отчёты — `Artifacts/tests/`.

- 14 Edit Mode tests: numeric rules/ACK/rebase/cancellation/limit, snapshots/revision, coordinate basis, roster, verdict idempotency/resume, module replacement, pressure units, circuit illumination.
- 3 Play Mode tests основного прогона: независимая кинематика/ближайший collider/видимый текст записки; 30 task views + wrong answer + comparable replacement + 20 правильных серверных ответов; production Unity host + два браузерных контроллера.
- Дополнительный Play Mode test: реальный GraphicRaycaster по 3D-клавише → pointerdown projection → release/click без дубля → authoritative ACK без локального score; блокировка физического ввода во время memory presentation.
- Отдельная проверка 49 imported hashes и 1092 защищённых файлов исходного Java-проекта: совпадают.
- Native macOS arm64 build и standalone подключение к серверу проверены. Последний hardware/frame-time smoke отчёт и PNG копируются в `Artifacts/standalone/`.
- Ранее удалённые кэши Chromium и `server/build/resources` потребовали восстановления. После восстановления выполнен успешный повторный прогон; Java gameplay/HTML/CSS/JS исходники не редактировались.

Измерение производительности — короткий development-run на Apple M4 при 1600×1000 и render scale 0.9, 180 кадров после прогрева. Это не долговременный benchmark, не подтверждение всех ракурсов/режимов и не сравнение с Java FPS. Число p95 берётся из реального итогового отчёта.

## Что ещё осталось

1. Полные прохождения каждого семейства, особенно RECALL, память/обратные последовательности, pressure/radio/circuit и реакционные окна при разных задержках.
2. Стресс и reconnect на восьми физических телефонах; проверка Safari/iOS. Автотест двух Chromium-контроллеров не заменяет эту матрицу.
3. Полный визуальный паритет CRT, панелей животных/символов, скрытых механизмов и финальной shutdown/blackout-постановки; больше художественной полировки света и окружения.
4. Полные экраны основного меню/лобби/вступления/викторины, затем Cursed Boxes и Impostor. Никаких игровых заглушек вместо них не создавалось.
5. Classic/inspection-specific диагностические интерфейсы, перенос расширенных настроек/локализации, production-сборка и упаковка Java server/JRE.
6. Windows x64: build command создана, но Windows Build Support не установлен; Windows build/run не проверены.
7. Долговременное серверное хранилище и восстановление комнат после рестарта — отдельная серверная задача, не новая функция этой Unity-итерации.
8. Автоматическая проверка слышимости всех клипов на реальном аудиоустройстве не выполнялась; в Unity есть панель ручной проверки.

Unity-проект не публиковался на GitHub. Java backup-ветка/теги не изменялись.

## Итоговая проверка текущего состояния

18 Unity-тестов завершились успешно: 14 Edit Mode + 3 основных Play Mode + дополнительный world-space pointer/memory-gate test. Актуальная native arm64-сборка повторно запущена и соединилась с Java; p95 времени кадра — 17,5876 мс на Apple M4 при 1600×1000, render scale 0.9, 180 кадров после прогрева.

Unity Editor открыт с этим проектом и вошёл в Play Mode; compiler/runtime exceptions в live-логе не обнаружены. Прямое управление GUI/чтение окна Console через native computer-use инструмент недоступно в этой сессии. Поэтому состояние Editor подтверждено процессом/логами, а визуальный результат проверен по реальным PNG native player и offscreen renders, не по снимку самого Editor.
