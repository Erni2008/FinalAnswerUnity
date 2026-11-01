using System;
using System.Collections.Generic;
using FinalAnswer.Application;
using FinalAnswer.Contracts;
using UnityEngine.Networking;

namespace FinalAnswer.Infrastructure
{
    /// <summary>application/x-www-form-urlencoded requests, exactly like RoomApiClient.java. Completion runs on the main thread.</summary>
    public sealed class UnityRoomApi : IRoomApi
    {
        const int TimeoutSeconds = 5;

        public void Health(string baseUrl, Action<Domain.ServerHealth> done)
        {
            var request = UnityWebRequest.Get(baseUrl.TrimEnd('/') + "/health");
            request.timeout = 2;
            request.SendWebRequest().completed += _ =>
            {
                var health = request.result == UnityWebRequest.Result.Success
                    ? Domain.ServerHealth.Parse(request.downloadHandler.text)
                    : new Domain.ServerHealth(Domain.ServerStatus.Unreachable, false, request.error);
                request.Dispose();
                done(health);
            };
        }

        public void CreateRoom(string baseUrl, IReadOnlyDictionary<string, string> form, Action<RoomSnapshot> created, Action<string> failed) =>
            PostSnapshot(baseUrl.TrimEnd('/') + "/rooms", form, created, failed);

        public void Join(string baseUrl, string roomCode, IReadOnlyDictionary<string, string> form, Action<RoomSnapshot> joined, Action<string> failed) =>
            PostSnapshot(RoomUrl(baseUrl, roomCode, "join"), form, joined, failed);

        public void Reroll(string baseUrl, string roomCode, string token, Action<RoomSnapshot> done, Action<string> failed) =>
            PostSnapshot(RoomUrl(baseUrl, roomCode, "reroll"), new Dictionary<string, string> { ["token"] = token }, done, failed);

        public void Heartbeat(string baseUrl, string roomCode, string token, Action<RoomSnapshot> done, Action<string> failed) =>
            PostSnapshot(RoomUrl(baseUrl, roomCode, "heartbeat"), new Dictionary<string, string> { ["token"] = token }, done, failed);

        static string RoomUrl(string baseUrl, string roomCode, string action) =>
            baseUrl.TrimEnd('/') + "/rooms/" + UnityWebRequest.EscapeURL(roomCode) + "/" + action;

        static void PostSnapshot(string url, IReadOnlyDictionary<string, string> form, Action<RoomSnapshot> created, Action<string> failed)
        {
            var fields = new Dictionary<string, string>();
            foreach (var pair in form) fields[pair.Key] = pair.Value;
            var request = UnityWebRequest.Post(url, fields);
            request.timeout = TimeoutSeconds;
            request.SendWebRequest().completed += _ =>
            {
                try
                {
                    var body = request.downloadHandler?.text ?? "";
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        created(new RoomSnapshot(EnvelopeCodec.ParseObject(body)));
                        return;
                    }
                    failed(ErrorCode(body, request.error));
                }
                catch (Exception error)
                {
                    failed("INVALID_RESPONSE: " + error.Message);
                }
                finally
                {
                    request.Dispose();
                }
            };
        }

        public void Leave(string baseUrl, string roomCode, string token, Action done)
        {
            var request = UnityWebRequest.Post(baseUrl.TrimEnd('/') + "/rooms/" + UnityWebRequest.EscapeURL(roomCode) + "/leave",
                new Dictionary<string, string> { ["token"] = token });
            request.timeout = 2;
            request.SendWebRequest().completed += _ =>
            {
                request.Dispose();
                done?.Invoke();
            };
        }

        static string ErrorCode(string body, string transportError)
        {
            try
            {
                var code = EnvelopeCodec.ParseObject(body).Value<string>("error");
                if (!string.IsNullOrEmpty(code)) return code;
            }
            catch (Exception) { /* not JSON */ }
            return string.IsNullOrEmpty(transportError) ? "SERVER_UNAVAILABLE" : transportError;
        }
    }
}
