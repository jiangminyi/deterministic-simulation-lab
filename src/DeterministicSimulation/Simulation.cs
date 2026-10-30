using System.Collections.ObjectModel;
using System.Security.Cryptography;

namespace DeterministicSimulation;

public sealed record SimulationConfig(long HalfExtent = 10000, long Acceleration = 3, long MaxSpeed = 40, long Drag = 1)
{
    internal void Validate()
    {
        if (HalfExtent is <= 0 or > 1_000_000_000 || MaxSpeed is <= 0 or > 1_000_000_000 ||
            Acceleration <= 0 || Acceleration > MaxSpeed || Drag < 0 || Drag > MaxSpeed)
            throw new ArgumentOutOfRangeException(nameof(SimulationConfig));
    }
}

public readonly record struct PlayerInput
{
    public int X { get; }
    public int Y { get; }
    public PlayerInput(int x, int y)
    {
        if (x is < -1 or > 1 || y is < -1 or > 1) throw new ArgumentOutOfRangeException(nameof(x));
        X = x; Y = y;
    }
}

public readonly record struct Body(int Id, long X, long Y, long VelocityX = 0, long VelocityY = 0);

public sealed class WorldState
{
    public int Tick { get; }
    public SimulationConfig Config { get; }
    public ReadOnlyCollection<Body> Bodies { get; }
    internal WorldState(int tick, SimulationConfig config, Body[] bodies)
    {
        Tick = tick; Config = config; Bodies = Array.AsReadOnly(bodies);
    }
}

public static class Simulation
{
    public static WorldState Create(SimulationConfig config, IEnumerable<Body> bodies)
    {
        ArgumentNullException.ThrowIfNull(config); ArgumentNullException.ThrowIfNull(bodies);
        config.Validate();
        var ordered = bodies.OrderBy(b => b.Id).ToArray();
        if (ordered.Length == 0 || ordered.Select(b => b.Id).Distinct().Count() != ordered.Length)
            throw new ArgumentException("A nonempty unique roster is required.", nameof(bodies));
        foreach (var b in ordered)
            if (b.Id < 0 || b.X < -config.HalfExtent || b.X > config.HalfExtent ||
                b.Y < -config.HalfExtent || b.Y > config.HalfExtent ||
                b.VelocityX < -config.MaxSpeed || b.VelocityX > config.MaxSpeed ||
                b.VelocityY < -config.MaxSpeed || b.VelocityY > config.MaxSpeed)
                throw new ArgumentOutOfRangeException(nameof(bodies));
        return new WorldState(0, config, ordered);
    }

    /// <summary>Exactly one logical tick; absent input means neutral axes.</summary>
    public static WorldState Step(WorldState state, IReadOnlyDictionary<int, PlayerInput>? inputs = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (inputs is not null)
        {
            var ids = state.Bodies.Select(b => b.Id).ToHashSet();
            if (inputs.Keys.Any(id => !ids.Contains(id)))
                throw new ArgumentException("Input contains an unknown player.", nameof(inputs));
        }
        int nextTick = checked(state.Tick + 1);
        var next = new Body[state.Bodies.Count];
        for (int i = 0; i < next.Length; i++)
        {
            var body = state.Bodies[i];
            var command = inputs is not null && inputs.TryGetValue(body.Id, out var supplied) ? supplied : default;
            var (x, vx) = Axis(body.X, body.VelocityX, command.X, state.Config);
            var (y, vy) = Axis(body.Y, body.VelocityY, command.Y, state.Config);
            next[i] = new Body(body.Id, x, y, vx, vy);
        }
        return new WorldState(nextTick, state.Config, next);
    }

    /// <summary>SHA-256 of a versioned, explicitly little-endian canonical state.</summary>
    public static string Hash(WorldState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(new byte[] { 0x53, 0x49, 0x4d, 0x31 }); // SIM1
            writer.Write(state.Tick);
            writer.Write(state.Config.HalfExtent); writer.Write(state.Config.Acceleration);
            writer.Write(state.Config.MaxSpeed); writer.Write(state.Config.Drag);
            writer.Write(state.Bodies.Count);
            foreach (var b in state.Bodies)
            {
                writer.Write(b.Id); writer.Write(b.X); writer.Write(b.Y);
                writer.Write(b.VelocityX); writer.Write(b.VelocityY);
            }
        }
        return Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant();
    }

    private static (long Position, long Velocity) Axis(long position, long velocity, int input, SimulationConfig config)
    {
        long accelerated = input == 0
            ? Math.Sign(velocity) * Math.Max(0, Math.Abs(velocity) - config.Drag)
            : velocity + input * config.Acceleration;
        velocity = Math.Clamp(accelerated, -config.MaxSpeed, config.MaxSpeed);
        long moved = position + velocity;
        if (moved > config.HalfExtent) return (config.HalfExtent, 0);
        if (moved < -config.HalfExtent) return (-config.HalfExtent, 0);
        return (moved, velocity);
    }
}
