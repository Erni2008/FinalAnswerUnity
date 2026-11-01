using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace FinalAnswer.Contracts
{
    /// <summary>
    /// Immutable typed view over RoomService.snapshot. The raw object is kept so modules can read
    /// fields this view does not model yet; unknown fields are preserved, never rejected.
    /// </summary>
    public sealed class RoomSnapshot
    {
        public JObject Raw { get; }

        public int ProtocolVersion { get; }
        public long Revision { get; }
        public long ServerTime { get; }
        public string RoomId { get; }
        public string RoomCode { get; }
        public string Name { get; }
        public string GameMode { get; }
        public int MaxPlayers { get; }
        public int QuestionCount { get; }
        public string RoomStatus { get; }
        public string Phase { get; }
        public string LobbyId { get; }
        public string PublicUrl { get; }
        public bool HostConnected { get; }
        public bool CanStart { get; }
        public bool IsHost { get; }
        public string SelfId { get; }
        /// <summary>Present only in HTTP create/join/heartbeat responses. Never log it.</summary>
        public string Token { get; }
        public IReadOnlyList<PlayerInfo> Players { get; }

        public QuizView Quiz { get; }
        public JObject LastCommand { get; }
        public JObject CursedBoxes { get; }
        public JObject Impostor { get; }

        public RoomSnapshot(JObject raw)
        {
            Raw = raw ?? throw new System.ArgumentNullException(nameof(raw));
            ProtocolVersion = raw.Value<int?>("protocolVersion") ?? 0;
            Revision = raw.Value<long?>("revision") ?? 0;
            ServerTime = raw.Value<long?>("serverTime") ?? 0;
            RoomId = raw.Value<string>("roomId") ?? "";
            RoomCode = raw.Value<string>("roomCode") ?? raw.Value<string>("code") ?? "";
            Name = raw.Value<string>("name") ?? "";
            GameMode = raw.Value<string>("gameMode") ?? "";
            MaxPlayers = raw.Value<int?>("maxPlayers") ?? 0;
            QuestionCount = raw.Value<int?>("questionCount") ?? 0;
            RoomStatus = raw.Value<string>("roomStatus") ?? "";
            Phase = raw.Value<string>("phase") ?? "";
            LobbyId = raw.Value<string>("lobbyId") ?? "";
            PublicUrl = raw.Value<string>("publicUrl") ?? "";
            HostConnected = raw.Value<bool?>("hostConnected") ?? false;
            CanStart = raw.Value<bool?>("canStart") ?? false;
            IsHost = raw["host"] is JValue hostFlag && hostFlag.Type == JTokenType.Boolean && (bool)hostFlag;
            SelfId = raw.Value<string>("selfId") ?? "";
            Token = raw.Value<string>("token") ?? "";

            var players = new List<PlayerInfo>();
            if (raw["players"] is JArray array)
                foreach (var item in array)
                    if (item is JObject player) players.Add(new PlayerInfo(player));
            Players = players;

            Quiz = raw["gameId"] != null ? new QuizView(raw) : null;
            LastCommand = raw["lastCommand"] as JObject;
            CursedBoxes = raw["cursedBoxes"] as JObject;
            Impostor = raw["impostor"] as JObject;
        }

        public int OnlinePlayerCount
        {
            get { var n = 0; foreach (var p in Players) if (p.Online) n++; return n; }
        }
    }

    public sealed class PlayerInfo
    {
        public string Id { get; }
        public string Name { get; }
        public string CharacterId { get; }
        public bool Online { get; }
        public bool IsGhost { get; }
        public int Score { get; }
        public bool HasSubmittedAnswer { get; }

        public PlayerInfo(JObject raw)
        {
            Id = raw.Value<string>("id") ?? "";
            Name = raw.Value<string>("name") ?? raw.Value<string>("nickname") ?? "";
            CharacterId = raw.Value<string>("characterId") ?? "";
            Online = raw.Value<string>("connectionStatus") == "ONLINE";
            IsGhost = raw.Value<bool?>("isGhost") ?? false;
            Score = raw.Value<int?>("score") ?? 0;
            HasSubmittedAnswer = raw.Value<bool?>("hasSubmittedAnswer") ?? false;
        }
    }

    /// <summary>Quiz fields of the room snapshot; present only while room.game exists.</summary>
    public sealed class QuizView
    {
        public string GameId { get; }
        public int QuestionNumber { get; }
        public int QuestionCount { get; }
        public long PhaseStartedAt { get; }
        public long Deadline { get; }
        public long AnsweringEndedAt { get; }
        public long TransitionEndsAt { get; }
        public string FinishReason { get; }
        public string QuestionId { get; }
        public string QuestionText { get; }
        public IReadOnlyList<string> Answers { get; }
        /// <summary>-1 until the server discloses it (host: REVEALING; everyone: RESULTS).</summary>
        public int CorrectAnswerIndex { get; }
        public JObject Intro { get; }

        public QuizView(JObject raw)
        {
            GameId = raw.Value<string>("gameId") ?? "";
            QuestionNumber = raw.Value<int?>("questionNumber") ?? 0;
            QuestionCount = raw.Value<int?>("questionCount") ?? 0;
            PhaseStartedAt = raw.Value<long?>("phaseStartedAt") ?? 0;
            Deadline = raw.Value<long?>("deadline") ?? 0;
            AnsweringEndedAt = raw.Value<long?>("answeringEndedAt") ?? 0;
            TransitionEndsAt = raw.Value<long?>("transitionEndsAt") ?? 0;
            FinishReason = raw.Value<string>("finishReason") ?? "";
            Intro = raw["intro"] as JObject;
            var answers = new List<string>();
            if (raw["question"] is JObject question)
            {
                QuestionId = question.Value<string>("id") ?? "";
                QuestionText = question.Value<string>("text") ?? "";
                if (question["answers"] is JArray list) foreach (var a in list) answers.Add(a.ToString());
                CorrectAnswerIndex = question.Value<int?>("correctAnswerIndex") ?? -1;
            }
            else
            {
                QuestionId = "";
                QuestionText = "";
                CorrectAnswerIndex = -1;
            }
            Answers = answers;
        }
    }
}
