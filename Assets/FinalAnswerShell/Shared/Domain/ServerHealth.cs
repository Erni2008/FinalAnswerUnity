using Newtonsoft.Json.Linq;

namespace FinalAnswer.Domain
{
    public enum ServerStatus { Unreachable, Compatible, Outdated }

    /// <summary>
    /// Result of GET /health. A server that answers but does not advertise the Last Command race engine with solo
    /// support runs older RoomServer code: phones would join, but the machine would never receive tasks.
    /// </summary>
    public readonly struct ServerHealth
    {
        public readonly ServerStatus Status;
        public readonly bool LastCommandDebug;
        public readonly string Detail;

        public ServerHealth(ServerStatus status, bool debug, string detail)
        {
            Status = status;
            LastCommandDebug = debug;
            Detail = detail ?? "";
        }

        public bool Compatible => Status == ServerStatus.Compatible;

        public static ServerHealth Parse(string body)
        {
            JObject json;
            try { json = JObject.Parse(body ?? ""); }
            catch (Newtonsoft.Json.JsonException) { return new ServerHealth(ServerStatus.Unreachable, false, "not a Final Answer server"); }
            if (json.Value<string>("status") != "ok") return new ServerHealth(ServerStatus.Unreachable, false, "status is not ok");
            var lastCommand = json["lastCommand"] as JObject;
            if (json.Value<int?>("protocol") != 1 || lastCommand?.Value<string>("engine") != "race" || lastCommand.Value<bool?>("solo") != true)
                return new ServerHealth(ServerStatus.Outdated, false, "server predates the Last Command race/solo protocol");
            return new ServerHealth(ServerStatus.Compatible, lastCommand.Value<bool?>("debug") == true, "");
        }
    }
}
