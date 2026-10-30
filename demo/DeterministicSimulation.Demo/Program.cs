using DeterministicSimulation;

var initial = Simulation.Create(new(), [new Body(0,0,0)]);
var reference = new RollbackSession(initial);
var delayed = new RollbackSession(initial);
reference.SetInput(3,0,new PlayerInput(1,-1));
reference.Advance(12);
delayed.Advance(12);
Console.WriteLine($"Before correction: {Simulation.Hash(delayed.Current)}");
delayed.SetInput(3,0,new PlayerInput(1,-1));
Console.WriteLine($"After correction:  {Simulation.Hash(delayed.Current)}");
Console.WriteLine($"Reference:         {Simulation.Hash(reference.Current)}");
Console.WriteLine($"Replayed ticks: {delayed.ReplayedTicks}; converged: {Simulation.Hash(reference.Current)==Simulation.Hash(delayed.Current)}");
