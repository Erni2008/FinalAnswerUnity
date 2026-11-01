using System;
using System.Collections.Generic;
using FinalAnswer.Contracts;

namespace FinalAnswer.Application
{
    /// <summary>HTTP form API of the Java RoomServer. Callbacks are invoked on the Unity main thread.</summary>
    public interface IRoomApi
    {
        void Health(string baseUrl, Action<Domain.ServerHealth> done);
        void CreateRoom(string baseUrl, IReadOnlyDictionary<string, string> form, Action<RoomSnapshot> created, Action<string> failed);
        void Leave(string baseUrl, string roomCode, string token, Action done);
        /// <summary>POST /rooms/{code}/join as a phone member (developer bots).</summary>
        void Join(string baseUrl, string roomCode, IReadOnlyDictionary<string, string> form, Action<RoomSnapshot> joined, Action<string> failed);
        /// <summary>POST /rooms/{code}/reroll: host-only, empty unstarted room; returns the snapshot with the new code.</summary>
        void Reroll(string baseUrl, string roomCode, string token, Action<RoomSnapshot> done, Action<string> failed);
        /// <summary>POST /rooms/{code}/heartbeat: keeps a draft room alive before the socket is opened.</summary>
        void Heartbeat(string baseUrl, string roomCode, string token, Action<RoomSnapshot> done, Action<string> failed);
    }

    public enum TransportEventKind { Opened, Message, Closed }

    public readonly struct TransportEvent
    {
        public readonly TransportEventKind Kind;
        public readonly string Text;

        public TransportEvent(TransportEventKind kind, string text)
        {
            Kind = kind;
            Text = text ?? "";
        }
    }

    /// <summary>
    /// One socket generation. Network threads only enqueue events; the main thread drains them,
    /// so no network callback ever touches Unity objects.
    /// </summary>
    public interface IRealtimeTransport : IDisposable
    {
        void Connect(Uri endpoint);
        /// <summary>Queues a text frame; sends are serialized by the transport.</summary>
        void Send(string text);
        bool TryDequeue(out TransportEvent next);
        void Close();
    }

    public interface IMonotonicClock
    {
        long NowMs { get; }
    }

    /// <summary>Owns an optional local Java server process (development/solo hosting).</summary>
    public interface IServerProcessHost
    {
        bool IsAvailable { get; }
        bool IsRunning { get; }
        string Status { get; }
        /// <summary>developerBuild: build the current server from source first and enable Last Command debug commands.</summary>
        void Start(int port, bool developerBuild = false);
        void Stop();
    }
}
