namespace FinalAnswer.Contracts
{
    /// <summary>Existing v1 message names, see RoomService.command / cursedCommand. No new commands are introduced.</summary>
    public static class MessageTypes
    {
        // Session
        public const string Connected = "CONNECTED";
        public const string Auth = "AUTH";
        public const string Authenticated = "AUTHENTICATED";
        public const string Ping = "PING";
        public const string Sync = "SYNC";
        public const string State = "STATE";
        public const string LeaveRoom = "LEAVE_ROOM";
        public const string Error = "ERROR";
        public const string RoomClosed = "ROOM_CLOSED";

        // Quiz
        public const string StartGame = "START_GAME";
        public const string PresentationReady = "PRESENTATION_READY";
        public const string RevealReady = "REVEAL_READY";
        public const string NextReady = "NEXT_READY";

        // Intro
        public const string IntroReady = "INTRO_READY";
        public const string SkipIntro = "SKIP_INTRO";

        // Last Command
        public const string StartLastCommand = "START_LAST_COMMAND";
        public const string EndLastCommand = "END_LAST_COMMAND";
        public const string LastCommandAction = "LAST_COMMAND_ACTION";

        // Cursed Boxes (host milestones)
        public const string StartCursedBoxes = "START_CURSED_BOXES";
        public const string CursedSceneReady = "CURSED_SCENE_READY";
        public const string CursedShotStarted = "CURSED_SHOT_STARTED";
        public const string CursedShotImpact = "CURSED_SHOT_IMPACT";
        public const string CursedShotComplete = "CURSED_SHOT_COMPLETE";
        public const string CursedReturnToLobby = "CURSED_RETURN_TO_LOBBY";

        // Impostor (host)
        public const string StartImpostor = "START_IMPOSTOR";
        public const string ImpostorReturnToLobby = "IMPOSTOR_RETURN_TO_LOBBY";

        /// <summary>Rejection reply types the server uses in place of a generic ERROR.</summary>
        public static bool IsRejection(string type) =>
            type == Error || type.EndsWith("_REJECTED", System.StringComparison.Ordinal);
    }

    /// <summary>Server-side quiz phases (Protocol.Phase).</summary>
    public static class QuizPhases
    {
        public const string Lobby = "LOBBY";
        public const string Starting = "STARTING";
        public const string QuestionPresentation = "QUESTION_PRESENTATION";
        public const string Answering = "ANSWERING";
        public const string Revealing = "REVEALING";
        public const string Results = "RESULTS";
        public const string GameFinished = "GAME_FINISHED";
    }
}
