using System;
using System.Collections.Generic;
using FinalAnswer.Application;
using FinalAnswer.Contracts;
using FinalAnswer.Domain;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace FinalAnswer.Tests.EditMode
{
    public sealed class EnvelopeCodecTests
    {
        [Test]
        public void EncodesExactV1Envelope()
        {
            var json = EnvelopeCodec.Encode(new Envelope("AUTH", "req-1", new JObject { ["roomCode"] = "ABC234", ["token"] = "t" }));
            Assert.AreEqual("{\"type\":\"AUTH\",\"requestId\":\"req-1\",\"payload\":{\"roomCode\":\"ABC234\",\"token\":\"t\"}}", json);
        }

        [TestCase("auth")]
        [TestCase("TOO_LONG_TYPE_NAME_THAT_EXCEEDS_FORTY_CHARS")]
        public void RejectsInvalidClientType(string type) =>
            Assert.Throws<ArgumentException>(() => EnvelopeCodec.Encode(new Envelope(type, "r", null)));

        [Test]
        public void RejectsInvalidRequestId() =>
            Assert.Throws<ArgumentException>(() => EnvelopeCodec.Encode(new Envelope("PING", "bad id!", null)));

        [Test]
        public void MeasuresLimitInUtf8Bytes()
        {
            // 6000 Cyrillic chars < 16384 chars but > 16384 UTF-8 bytes.
            var payload = new JObject { ["clue"] = new string('ж', 9000) };
            Assert.Throws<ArgumentException>(() => EnvelopeCodec.Encode(new Envelope("SUBMIT_CLUE", "r", payload)));
        }

        [Test]
        public void DecodesBroadcastWithEmptyRequestId()
        {
            var message = EnvelopeCodec.Decode("{\"type\":\"ROOM_UPDATED\",\"requestId\":\"\",\"payload\":{\"roomId\":\"r\"}}");
            Assert.IsTrue(message.IsBroadcast);
            Assert.AreEqual("ROOM_UPDATED", message.Type);
        }

        [Test]
        public void KeepsLargeIntegersExact()
        {
            var message = EnvelopeCodec.Decode("{\"type\":\"STATE\",\"requestId\":\"\",\"payload\":{\"seed\":9007199254740993}}");
            Assert.AreEqual(9007199254740993L, message.Payload.Value<long>("seed"));
        }

        [Test]
        public void RejectsExcessiveDepth()
        {
            var deep = "";
            for (var i = 0; i < 40; i++) deep += "{\"a\":";
            deep += "1";
            for (var i = 0; i < 40; i++) deep += "}";
            Assert.Throws<FormatException>(() => EnvelopeCodec.Decode("{\"type\":\"STATE\",\"payload\":" + deep + "}"));
        }

        [Test]
        public void ParsesSnapshotFields()
        {
            var raw = JObject.Parse(@"{""roomId"":""id"",""roomCode"":""ABC234"",""revision"":7,""serverTime"":1000,
                ""phase"":""ANSWERING"",""host"":true,""lobbyId"":""lobby"",""canStart"":false,""gameId"":""g"",
                ""question"":{""id"":""q1"",""text"":""Вопрос?"",""answers"":[""a"",""b"",""c"",""d""]},
                ""players"":[{""id"":""p1"",""name"":""Аня"",""connectionStatus"":""ONLINE"",""score"":100}]}");
            var snapshot = new RoomSnapshot(raw);
            Assert.AreEqual(7, snapshot.Revision);
            Assert.IsTrue(snapshot.IsHost);
            Assert.AreEqual(1, snapshot.OnlinePlayerCount);
            Assert.AreEqual("q1", snapshot.Quiz.QuestionId);
            Assert.AreEqual(4, snapshot.Quiz.Answers.Count);
            Assert.AreEqual(-1, snapshot.Quiz.CorrectAnswerIndex);
            Assert.AreEqual(ModuleKind.Quiz, ModuleRouting.Select(snapshot));
        }

        [Test]
        public void CursedBoxesTakesPrecedenceOverQuiz()
        {
            var snapshot = new RoomSnapshot(JObject.Parse(@"{""roomId"":""id"",""gameId"":""g"",""cursedBoxes"":{""sessionId"":""s""}}"));
            Assert.AreEqual(ModuleKind.CursedBoxes, ModuleRouting.Select(snapshot));
        }

        [Test]
        public void RoomSettingsMatchServerBounds()
        {
            Assert.Throws<ArgumentException>(() => new RoomSettings { MaxPlayers = 9 }.Validate());
            Assert.Throws<ArgumentException>(() => new RoomSettings { QuestionCount = 0 }.Validate());
            Assert.Throws<ArgumentException>(() => new RoomSettings { GameMode = "battle_royale" }.Validate());
            var form = new RoomSettings { GameMode = "deadline", MaxPlayers = 8 }.ToForm();
            Assert.AreEqual("deadline", form["gameMode"]);
            Assert.AreEqual("8", form["maxPlayers"]);
            Assert.IsFalse(form.ContainsKey("password"));
            Assert.AreEqual(5, GameModes.All.Count);
        }
    }

    public sealed class DomainTests
    {
        static RoomSnapshot Snap(string room, long revision) =>
            new RoomSnapshot(new JObject { ["roomId"] = room, ["revision"] = revision });

        [Test]
        public void AcceptsEqualRevisionAndDropsOlder()
        {
            var store = new SnapshotStore();
            Assert.IsTrue(store.TryApply(Snap("r", 5)));
            Assert.IsTrue(store.TryApply(Snap("r", 5)), "STATE reply may repeat the broadcast revision");
            Assert.IsFalse(store.TryApply(Snap("r", 4)));
            Assert.IsTrue(store.TryApply(Snap("other", 1)), "a new room identity resets ordering");
        }

        [Test]
        public void ClockUsesLowestRttSample()
        {
            var clock = new ServerClock();
            clock.Sample(serverTime: 10_050, sentAtLocal: 0, receivedAtLocal: 100);
            Assert.AreEqual(10_100, clock.Now(100));
            clock.Sample(serverTime: 20_000, sentAtLocal: 1_000, receivedAtLocal: 1_900); // slow sample ignored
            Assert.AreEqual(10_100, clock.Now(100));
        }

        [Test]
        public void ReconnectBackoffIsBounded()
        {
            var policy = new ReconnectPolicy(seed: 42);
            long previous = 0;
            for (var i = 0; i < ReconnectPolicy.MaxAttempts; i++)
            {
                var delay = policy.NextDelayMs();
                Assert.That(delay, Is.InRange(ReconnectPolicy.BaseDelayMs, ReconnectPolicy.MaxDelayMs));
                Assert.That(delay, Is.GreaterThanOrEqualTo(Math.Min(previous, ReconnectPolicy.MaxDelayMs) * 0.8));
                previous = delay;
            }
            Assert.IsTrue(policy.Exhausted);
        }
    }

    public sealed class RoomManagerTests
    {
        sealed class FakeClock : IMonotonicClock { public long NowMs { get; set; } }

        sealed class FakeTransport : IRealtimeTransport
        {
            public readonly Queue<TransportEvent> Incoming = new Queue<TransportEvent>();
            public readonly List<Envelope> Sent = new List<Envelope>();
            public Uri Endpoint;
            public bool Disposed;
            public void Connect(Uri endpoint) => Endpoint = endpoint;
            public void Send(string text) => Sent.Add(EnvelopeCodec.Decode(text));
            public bool TryDequeue(out TransportEvent next)
            {
                if (Incoming.Count == 0) { next = default; return false; }
                next = Incoming.Dequeue();
                return true;
            }
            public void Close() => Disposed = true;
            public void Dispose() => Disposed = true;
            public void Push(string type, string requestId, JObject payload) => Incoming.Enqueue(new TransportEvent(TransportEventKind.Message,
                new JObject { ["type"] = type, ["requestId"] = requestId, ["payload"] = payload }.ToString()));
        }

        readonly List<FakeTransport> sockets = new List<FakeTransport>();
        FakeClock clock;
        RoomManager room;

        static JObject Snapshot(long revision) => new JObject { ["roomId"] = "room", ["roomCode"] = "ABC234", ["revision"] = revision, ["serverTime"] = 5_000 };

        [SetUp]
        public void SetUp()
        {
            sockets.Clear();
            clock = new FakeClock();
            room = new RoomManager(() => { var s = new FakeTransport(); sockets.Add(s); return s; }, clock, new ReconnectPolicy(seed: 1));
            var created = Snapshot(1);
            created["token"] = "host-token";
            room.Begin("http://127.0.0.1:8080", new RoomSnapshot(created));
        }

        FakeTransport Socket => sockets[sockets.Count - 1];

        void Authenticate()
        {
            Socket.Push("CONNECTED", "", new JObject { ["protocolVersion"] = 1 });
            room.Tick();
            Socket.Push("AUTHENTICATED", Socket.Sent[0].RequestId, Snapshot(2));
            room.Tick();
        }

        [Test]
        public void AuthenticatesWithHostTokenOverWs()
        {
            Assert.AreEqual("ws://127.0.0.1:8080/ws", Socket.Endpoint.ToString());
            Authenticate();
            Assert.AreEqual("AUTH", Socket.Sent[0].Type);
            Assert.AreEqual("host-token", Socket.Sent[0].Payload.Value<string>("token"));
            Assert.AreEqual(ConnectionState.Online, room.State);
            Assert.AreEqual(2, room.Snapshots.Current.Revision);
        }

        [Test]
        public void PingsEveryTenSeconds()
        {
            Authenticate();
            clock.NowMs += RoomManager.PingIntervalMs;
            room.Tick();
            Assert.AreEqual("PING", Socket.Sent[Socket.Sent.Count - 1].Type);
        }

        [Test]
        public void CorrelatesRejectionToCommand()
        {
            Authenticate();
            CommandRejection seen = null;
            room.CommandRejected += r => seen = r;
            var id = room.Send("START_GAME", room.RoomPayload());
            Socket.Push("ERROR", id, new JObject { ["code"] = "NOT_ENOUGH_PLAYERS" });
            room.Tick();
            Assert.AreEqual("START_GAME", seen.CommandType);
            Assert.AreEqual("NOT_ENOUGH_PLAYERS", seen.Code);
        }

        [Test]
        public void ReconnectsWithSameTokenAfterDrop()
        {
            Authenticate();
            var first = Socket;
            first.Incoming.Enqueue(new TransportEvent(TransportEventKind.Closed, "CONNECTION_LOST"));
            room.Tick();
            Assert.AreEqual(ConnectionState.Reconnecting, room.State);
            Assert.IsTrue(first.Disposed);
            clock.NowMs += ReconnectPolicy.MaxDelayMs;
            room.Tick();
            Assert.AreEqual(2, sockets.Count);
            Authenticate();
            Assert.AreEqual("host-token", Socket.Sent[0].Payload.Value<string>("token"));
            Assert.AreEqual(ConnectionState.Online, room.State);
        }

        [Test]
        public void AuthRejectionDoesNotRetry()
        {
            Socket.Push("CONNECTED", "", new JObject());
            room.Tick();
            Socket.Push("ERROR", Socket.Sent[0].RequestId, new JObject { ["code"] = "SESSION_ENDED" });
            room.Tick();
            Assert.AreEqual(ConnectionState.Failed, room.State);
            clock.NowMs += 60_000;
            room.Tick();
            Assert.AreEqual(1, sockets.Count);
        }

        [Test]
        public void RoomClosedStopsSession()
        {
            Authenticate();
            string reason = null;
            room.RoomClosed += r => reason = r;
            Socket.Push("ROOM_CLOSED", "", new JObject { ["reason"] = "HOST_TIMEOUT" });
            room.Tick();
            Assert.AreEqual("HOST_TIMEOUT", reason);
            Assert.AreEqual(ConnectionState.Closed, room.State);
        }
    }
}
