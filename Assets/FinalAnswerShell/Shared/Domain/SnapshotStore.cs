using System;
using FinalAnswer.Contracts;

namespace FinalAnswer.Domain
{
    /// <summary>
    /// Accepts immutable snapshots in revision order for one room identity.
    /// Equal revisions are accepted: a STATE reply may repeat the broadcast revision (NETWORK_ARCHITECTURE §5).
    /// </summary>
    public sealed class SnapshotStore
    {
        public RoomSnapshot Current { get; private set; }
        public event Action<RoomSnapshot> Changed;

        public bool TryApply(RoomSnapshot snapshot)
        {
            if (snapshot == null) return false;
            if (Current != null && Current.RoomId.Length > 0 && snapshot.RoomId == Current.RoomId
                && snapshot.Revision < Current.Revision) return false;
            Current = snapshot;
            Changed?.Invoke(snapshot);
            return true;
        }

        public void Reset() => Current = null;
    }

    /// <summary>Estimates server time from snapshot samples; local clock is monotonic milliseconds.</summary>
    public sealed class ServerClock
    {
        long offset;
        long bestRtt = long.MaxValue;
        bool synced;

        public bool IsSynced => synced;

        /// <summary>Request-correlated sample: keep the lowest-RTT estimate (NTP-style midpoint).</summary>
        public void Sample(long serverTime, long sentAtLocal, long receivedAtLocal)
        {
            var rtt = Math.Max(0, receivedAtLocal - sentAtLocal);
            if (synced && rtt > bestRtt) { bestRtt += 1; return; } // slowly forget stale best
            bestRtt = rtt;
            offset = serverTime - (sentAtLocal + rtt / 2);
            synced = true;
        }

        /// <summary>Broadcast sample without a request: only used until a correlated one arrives.</summary>
        public void SampleBroadcast(long serverTime, long receivedAtLocal)
        {
            if (synced) return;
            offset = serverTime - receivedAtLocal;
        }

        public long Now(long localNow) => localNow + offset;

        public void Reset()
        {
            synced = false;
            bestRtt = long.MaxValue;
            offset = 0;
        }
    }

    /// <summary>Matches the Java desktop reconnect: up to 12 attempts, 500 ms .. 15 s exponential plus jitter.</summary>
    public sealed class ReconnectPolicy
    {
        public const int MaxAttempts = 12;
        public const long BaseDelayMs = 500, MaxDelayMs = 15_000;
        readonly Random random;

        public ReconnectPolicy(int seed = 0) { random = seed == 0 ? new Random() : new Random(seed); }

        public int Attempt { get; private set; }
        public bool Exhausted => Attempt >= MaxAttempts;

        public long NextDelayMs()
        {
            var exp = Math.Min(MaxDelayMs, BaseDelayMs << Math.Min(Attempt, 10));
            Attempt++;
            var jitter = (long)(random.NextDouble() * exp * 0.25);
            return Math.Min(MaxDelayMs, exp + jitter);
        }

        public void Reset() => Attempt = 0;
    }
}
