using FinalAnswer.Contracts;

namespace FinalAnswer.Application
{
    /// <summary>Which module the server snapshot currently asks the host to present.</summary>
    public enum ModuleKind { Lobby, Quiz, CursedBoxes, Impostor, LastCommand }

    public static class ModuleRouting
    {
        /// <summary>
        /// Mini-game sessions take precedence over the quiz: Cursed Boxes runs inside a quiz game
        /// and returns to it. The decision only reads server state, never local animation state.
        /// </summary>
        public static ModuleKind Select(RoomSnapshot snapshot)
        {
            if (snapshot == null) return ModuleKind.Lobby;
            if (snapshot.LastCommand != null) return ModuleKind.LastCommand;
            if (snapshot.CursedBoxes != null) return ModuleKind.CursedBoxes;
            if (snapshot.Impostor != null) return ModuleKind.Impostor;
            if (snapshot.Quiz != null) return ModuleKind.Quiz;
            return ModuleKind.Lobby;
        }
    }

    /// <summary>What a module may use; it never creates its own socket or looks up global singletons.</summary>
    public sealed class ModuleContext
    {
        public RoomManager Room { get; }
        public IMonotonicClock Clock { get; }

        public ModuleContext(RoomManager room, IMonotonicClock clock)
        {
            Room = room;
            Clock = clock;
        }
    }

    /// <summary>Lifecycle from UNITY_ARCHITECTURE §3: Enter → ApplySnapshot* / Tick* → Exit (disposes everything it made).</summary>
    public interface IGameModule
    {
        ModuleKind Kind { get; }
        void Enter(ModuleContext context, RoomSnapshot snapshot);
        void ApplySnapshot(RoomSnapshot snapshot);
        void Tick(float localDelta, long serverNow);
        void Exit();
    }
}
