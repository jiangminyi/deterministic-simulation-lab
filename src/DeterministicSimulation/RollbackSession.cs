namespace DeterministicSimulation;

public enum InputUpdate { Unchanged, Scheduled, Replayed }

/// <summary>Single-owner bounded history. One authoritative value per (tick, player).</summary>
public sealed class RollbackSession
{
    private readonly SortedDictionary<int, WorldState> snapshots = new();
    private readonly SortedDictionary<int, Dictionary<int, PlayerInput>> inputs = new();
    private readonly HashSet<int> playerIds;
    private readonly int initialTick;
    public WorldState Current { get; private set; }
    public int HistoryWindow { get; }
    public int FutureWindow { get; }
    public int EarliestTick => snapshots.First().Key;
    public int SnapshotCount => snapshots.Count;
    public int InputFrameCount => inputs.Count;
    public long ReplayedTicks { get; private set; }

    public RollbackSession(WorldState initial, int historyWindow = 120, int futureWindow = 120)
    {
        ArgumentNullException.ThrowIfNull(initial);
        if (historyWindow is < 1 or > 10000 || futureWindow is < 0 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(historyWindow));
        Current = initial; initialTick = initial.Tick;
        HistoryWindow = historyWindow; FutureWindow = futureWindow;
        playerIds = initial.Bodies.Select(b => b.Id).ToHashSet();
        snapshots.Add(initial.Tick, initial);
    }

    public InputUpdate SetInput(int tick, int playerId, PlayerInput command)
    {
        if (!playerIds.Contains(playerId)) throw new ArgumentException("Unknown player.", nameof(playerId));
        if (tick < EarliestTick || tick == int.MaxValue || (long)tick > (long)Current.Tick + FutureWindow)
            throw new ArgumentOutOfRangeException(nameof(tick), "Input is outside the retained or allowed future window.");
        var previous = inputs.TryGetValue(tick, out var frame) && frame.TryGetValue(playerId, out var old) ? old : default;
        if (previous == command) return InputUpdate.Unchanged;

        var changedFrame = frame is null ? new Dictionary<int, PlayerInput>() : new Dictionary<int, PlayerInput>(frame);
        if (command == default) changedFrame.Remove(playerId);
        else changedFrame[playerId] = command;

        // Build the complete replacement history before publishing it.
        var replacements = new List<WorldState>();
        if (tick < Current.Tick)
        {
            var state = snapshots[tick];
            while (state.Tick < Current.Tick)
            {
                IReadOnlyDictionary<int, PlayerInput>? commands = state.Tick == tick ? changedFrame : Frame(state.Tick);
                state = Simulation.Step(state, commands);
                replacements.Add(state);
            }
        }
        long newReplayCount = checked(ReplayedTicks + replacements.Count);
        if (changedFrame.Count == 0) inputs.Remove(tick); else inputs[tick] = changedFrame;
        foreach (var replacement in replacements) snapshots[replacement.Tick] = replacement;
        if (replacements.Count == 0) return InputUpdate.Scheduled;
        Current = replacements[^1];
        ReplayedTicks = newReplayCount;
        return InputUpdate.Replayed;
    }

    public void Advance(int ticks = 1)
    {
        if (ticks is < 0 or > 10000) throw new ArgumentOutOfRangeException(nameof(ticks));
        _ = checked(Current.Tick + ticks);
        for (int i = 0; i < ticks; i++)
        {
            Current = Simulation.Step(Current, Frame(Current.Tick));
            snapshots.Add(Current.Tick, Current);
            int floor = Math.Max(initialTick, Current.Tick - HistoryWindow);
            while (snapshots.First().Key < floor) snapshots.Remove(snapshots.First().Key);
            while (inputs.Count != 0 && inputs.First().Key < floor) inputs.Remove(inputs.First().Key);
        }
    }

    public WorldState SnapshotAt(int tick) => snapshots.TryGetValue(tick, out var state)
        ? state : throw new ArgumentOutOfRangeException(nameof(tick));

    public IReadOnlyDictionary<int, PlayerInput> InputsAt(int tick) =>
        new System.Collections.ObjectModel.ReadOnlyDictionary<int, PlayerInput>(
            inputs.TryGetValue(tick, out var frame) ? new Dictionary<int, PlayerInput>(frame) : new Dictionary<int, PlayerInput>());

    private IReadOnlyDictionary<int, PlayerInput>? Frame(int tick) => inputs.TryGetValue(tick, out var frame) ? frame : null;
}
