using System;
using System.Collections.Generic;
using System.Linq;
using FinalAnswer.Application;
using FinalAnswer.Contracts;
using Newtonsoft.Json.Linq;

namespace FinalAnswer.Infrastructure
{
    public enum BotAnswerMode { Random, First, Silent }

    /// <summary>
    /// Developer-only fake phone. Joins over the unchanged phone HTTP/WS API (exactly like web/app.js),
    /// sees only its own personal snapshot, and plays: quiz answers, Cursed Boxes hide/select/fire,
    /// Impostor clue/suspicion/final vote. Last Command terminals stay idle (they are personal and optional).
    /// Never used by production flows.
    /// </summary>
    public sealed class DevPhoneBot
    {
        static readonly Random Dice = new Random();
        static readonly string[] Clues = { "тепло", "круглое", "старое", "громко", "быстро", "тихо", "острое" };

        readonly IRoomApi api;
        readonly Func<IRealtimeTransport> transportFactory;
        readonly IMonotonicClock clock;
        readonly string baseUrl, roomCode;
        readonly HashSet<string> done = new HashSet<string>();
        readonly List<(long due, Action action)> scheduled = new List<(long, Action)>();
        IRealtimeTransport transport;
        string token = "";
        long lastPing;
        int requestCounter;

        public string Name { get; }
        public string CharacterId { get; }
        public string PlayerId { get; private set; } = "";
        public string Status { get; private set; } = "joining";
        public bool Online { get; private set; }
        public BotAnswerMode AnswerMode { get; set; } = BotAnswerMode.Random;
        public int ActionsSent { get; private set; }
        public string LastPhase { get; private set; } = "";

        public DevPhoneBot(IRoomApi api, Func<IRealtimeTransport> transportFactory, IMonotonicClock clock, string baseUrl, string roomCode, string name, string characterId)
        {
            this.api = api;
            this.transportFactory = transportFactory;
            this.clock = clock;
            this.baseUrl = baseUrl.TrimEnd('/');
            this.roomCode = roomCode;
            Name = name;
            CharacterId = characterId;
        }

        public void Join(Action<string> failed)
        {
            var form = new Dictionary<string, string> { ["name"] = Name };
            if (!string.IsNullOrEmpty(CharacterId)) form["characterId"] = CharacterId;
            api.Join(baseUrl, roomCode, form, joined =>
            {
                token = joined.Token;
                PlayerId = joined.SelfId;
                Connect();
            }, error => { Status = "join failed: " + error; failed?.Invoke(error); });
        }

        /// <summary>Simulates a phone losing network: the socket closes without LEAVE (server marks OFFLINE).</summary>
        public void DropConnection()
        {
            transport?.Dispose();
            transport = null;
            Online = false;
            Status = "dropped (dev)";
        }

        public void Reconnect()
        {
            if (token.Length > 0 && transport == null) Connect();
        }

        public void Leave()
        {
            DropConnection();
            if (token.Length > 0) api.Leave(baseUrl, roomCode, token, null);
            token = "";
            Status = "left";
        }

        public void Tick()
        {
            var now = clock.NowMs;
            for (var i = scheduled.Count - 1; i >= 0; i--)
            {
                if (scheduled[i].due > now) continue;
                var action = scheduled[i].action;
                scheduled.RemoveAt(i);
                action();
            }
            if (transport == null) return;
            while (transport != null && transport.TryDequeue(out var next))
            {
                if (next.Kind == TransportEventKind.Closed) { Online = false; Status = "closed: " + next.Text; transport.Dispose(); transport = null; return; }
                if (next.Kind != TransportEventKind.Message) continue;
                Envelope message;
                try { message = EnvelopeCodec.Decode(next.Text); }
                catch (FormatException) { continue; }
                Receive(message);
            }
            if (Online && now - lastPing > RoomManager.PingIntervalMs)
            {
                lastPing = now;
                Send(MessageTypes.Ping, new JObject());
            }
        }

        void Connect()
        {
            transport = transportFactory();
            Status = "connecting";
            transport.Connect(RoomManager.SocketUri(baseUrl));
        }

        void Receive(Envelope message)
        {
            switch (message.Type)
            {
                case MessageTypes.Connected:
                    transport.Send(EnvelopeCodec.Encode(new Envelope(MessageTypes.Auth, NextId(), new JObject { ["roomCode"] = roomCode, ["token"] = token })));
                    return;
                case MessageTypes.Authenticated:
                    Online = true;
                    Status = "online";
                    lastPing = clock.NowMs;
                    break;
                case MessageTypes.RoomClosed:
                    Online = false;
                    Status = "room closed";
                    DropConnection();
                    return;
            }
            if (MessageTypes.IsRejection(message.Type)) { Status = message.Type + " " + message.Payload.Value<string>("code"); return; }
            if (message.Payload["roomId"] == null) return;
            var s = message.Payload;
            LastPhase = s.Value<string>("phase") ?? "";
            if (s["cursedBoxes"] is JObject cursed) Cursed(cursed);
            else Quiz(s);
            if (s["impostor"] is JObject impostor) Impostor(impostor, s.Value<string>("selfId") ?? "");
        }

        void Quiz(JObject s)
        {
            if (AnswerMode == BotAnswerMode.Silent) return;
            if (s.Value<string>("phase") != QuizPhases.Answering || (s.Value<bool?>("hasSubmittedAnswer") ?? false)) return;
            if (!(s["question"] is JObject question)) return;
            var gameId = s.Value<string>("gameId");
            var questionId = question.Value<string>("id");
            var count = (question["answers"] as JArray)?.Count ?? 4;
            Once("answer:" + gameId + ":" + questionId, 700, () =>
            {
                var index = AnswerMode == BotAnswerMode.First ? 0 : Dice.Next(count);
                Send("SUBMIT_ANSWER", new JObject { ["gameId"] = gameId, ["questionId"] = questionId, ["answerIndex"] = index });
            });
        }

        void Cursed(JObject m)
        {
            var free = (m["boxes"] as JArray)?.OfType<JObject>().Where(b => b.Value<string>("state") == "AVAILABLE").Select(b => b.Value<int>("id")).ToList() ?? new List<int>();
            if (free.Count == 0) return;
            var session = m.Value<string>("sessionId");
            if ((m.Value<bool?>("canHide") ?? false) && !(m.Value<bool?>("hidingConfirmed") ?? false))
                Once("hide:" + session, 600, () => Send("CURSED_HIDE", new JObject { ["sessionId"] = session, ["boxId"] = Pick(free) }));
            if (!(m.Value<bool?>("canAct") ?? false) || m["pendingShot"] != null) return;
            var turn = m.Value<int>("turnId");
            var shotNumber = m.Value<int>("shotLimit") + 1 - m.Value<int>("shotsRemaining");
            var selected = m.Value<int?>("selectedCrateId") ?? -1;
            if (selected < 0 || !free.Contains(selected))
                Once("select:" + session + ":" + turn + ":" + shotNumber, 500, () =>
                    Send("CURSED_SELECT", new JObject { ["sessionId"] = session, ["turnId"] = turn, ["boxId"] = Pick(free) }));
            else
                Once("fire:" + session + ":" + turn + ":" + shotNumber, 400, () =>
                    Send("CURSED_FIRE", new JObject { ["sessionId"] = session, ["turnId"] = turn, ["boxId"] = selected, ["shotNumber"] = shotNumber }));
        }

        void Impostor(JObject m, string selfId)
        {
            var session = m.Value<string>("sessionId");
            var phase = m.Value<string>("phase");
            var round = m.Value<int?>("round") ?? 1;
            var others = (m["participants"] as JArray)?.OfType<JObject>().Select(p => p.Value<string>("id")).Where(id => id != selfId).ToList() ?? new List<string>();
            if (phase == "ROUND_CLUE_INPUT" && (m.Value<bool?>("activeParticipant") ?? false) && !(m.Value<bool?>("clueSubmitted") ?? false))
                Once("clue:" + session + ":" + round, 1500, () =>
                    Send("SUBMIT_CLUE", new JObject { ["sessionId"] = session, ["round"] = round, ["clue"] = Clues[Dice.Next(Clues.Length)] }));
            if (phase == "PRELIMINARY_SUSPICION" && (m.Value<bool?>("eligible") ?? false) && !(m.Value<bool?>("hasSubmitted") ?? false) && others.Count > 0)
                Once("suspect:" + session + ":" + round, 1500, () =>
                    Send("SUBMIT_SUSPICION", new JObject { ["sessionId"] = session, ["round"] = round, ["suspectId"] = Pick(others) }));
            if (phase == "FINAL_VOTING" && !(m.Value<bool?>("finalVoteSubmitted") ?? false) && others.Count > 0)
                Once("vote:" + session, 1500, () =>
                    Send("SUBMIT_FINAL_VOTE", new JObject { ["sessionId"] = session, ["suspectId"] = Pick(others) }));
        }

        void Once(string key, int delayMs, Action action)
        {
            if (!done.Add(key)) return;
            scheduled.Add((clock.NowMs + delayMs + Dice.Next(delayMs), action));
        }

        void Send(string type, JObject payload)
        {
            if (transport == null || !Online) return;
            payload["roomCode"] = roomCode;
            transport.Send(EnvelopeCodec.Encode(new Envelope(type, NextId(), payload)));
            ActionsSent++;
        }

        string NextId() => "bot" + Name.GetHashCode().ToString("x") + "n" + (++requestCounter);

        static T Pick<T>(IReadOnlyList<T> list) => list[Dice.Next(list.Count)];
    }
}
