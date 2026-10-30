using DeterministicSimulation;

var specs = new (string Name, Action Run)[]
{
    ("fixed step acceleration has explicit expected trajectory", () => {
        var s = Start(); for (int i = 0; i < 4; i++) s = Simulation.Step(s, Command(1, 0));
        Eq(new Body(0, 30, 0, 12, 0), s.Bodies.Single()); Eq(4, s.Tick);
    }),
    ("neutral input applies drag without changing sign", () => {
        var s = Simulation.Create(new(), [new Body(0, 0, 0, 2, -2)]);
        s = Simulation.Step(s); Eq(new Body(0, 1, -1, 1, -1), s.Bodies.Single());
        s = Simulation.Step(s); Eq(new Body(0, 1, -1), s.Bodies.Single());
    }),
    ("velocity cap and wall contact", () => {
        var s = Simulation.Create(new(20, 3, 5, 1), [new Body(0, 18, -18, 4, -4)]);
        s = Simulation.Step(s, Command(1,-1)); Eq(new Body(0, 20, -20), s.Bodies.Single());
    }),
    ("exact wall contact keeps velocity until next tick", () => {
        var s = Simulation.Create(new(20, 3, 5, 0), [new Body(0, 15, 0, 5, 0)]);
        s = Simulation.Step(s); Eq(5L, s.Bodies[0].VelocityX); s = Simulation.Step(s); Eq(0L, s.Bodies[0].VelocityX);
    }),
    ("input and roster order cannot change hash", () => {
        var a = Simulation.Create(new(), [new Body(2, 1, 2), new Body(1, 3, 4)]);
        var b = Simulation.Create(new(), [new Body(1, 3, 4), new Body(2, 1, 2)]);
        a = Simulation.Step(a, new Dictionary<int, PlayerInput> { [2] = new(1,0), [1] = new(0,1) });
        b = Simulation.Step(b, new Dictionary<int, PlayerInput> { [1] = new(0,1), [2] = new(1,0) });
        Eq(Simulation.Hash(a), Simulation.Hash(b));
    }),
    ("configuration and tick are included in hash", () => {
        var a = Start(); var b = Simulation.Create(new(10000,3,40,2), [new Body(0,0,0)]);
        Yes(Simulation.Hash(a) != Simulation.Hash(b)); Yes(Simulation.Hash(a) != Simulation.Hash(Simulation.Step(a)));
    }),
    ("canonical serialization golden hash", () => {
        // Independently encoded with Python struct.pack little-endian fields and hashlib.sha256.
        Eq("e8468f7527a8826f0b312341a84308fc9259b92d3ef757369a28b563191d58f8", Simulation.Hash(Start()));
    }),
    ("step leaves input state immutable", () => {
        var s = Start(); var hash = Simulation.Hash(s); Simulation.Step(s, Command(1,1)); Eq(hash, Simulation.Hash(s));
        Throws<NotSupportedException>(() => ((IList<Body>)s.Bodies)[0] = new Body(0,99,99));
    }),
    ("creation rejects duplicate roster and invalid configuration", () => {
        Throws<ArgumentException>(() => Simulation.Create(new(), [new Body(0,0,0), new Body(0,1,1)]));
        Throws<ArgumentException>(() => Simulation.Create(new(), Array.Empty<Body>()));
        Throws<ArgumentOutOfRangeException>(() => Simulation.Create(new(HalfExtent:long.MaxValue), [new Body(0,0,0)]));
        Throws<ArgumentOutOfRangeException>(() => Simulation.Create(new(), [new Body(0,long.MinValue,0)]));
        Throws<ArgumentOutOfRangeException>(() => new PlayerInput(2,0));
    }),
    ("unknown player rejected before simulation mutation", () => {
        var s = Start(); Throws<ArgumentException>(() => Simulation.Step(s, new Dictionary<int,PlayerInput> { [5] = new(1,0) })); Eq(0, s.Tick);
    }),
    ("late input converges with on-time replay", () => {
        var onTime = new RollbackSession(Start()); var late = new RollbackSession(Start());
        onTime.SetInput(2,0,new(1,1)); onTime.Advance(10); late.Advance(10);
        Eq(InputUpdate.Replayed, late.SetInput(2,0,new(1,1))); Eq(8L, late.ReplayedTicks);
        Eq(Simulation.Hash(onTime.Current), Simulation.Hash(late.Current));
    }),
    ("duplicate input is idempotent and neutral correction retracts input", () => {
        var s = new RollbackSession(Start()); s.SetInput(0,0,new(1,0)); s.Advance(5);
        Eq(InputUpdate.Unchanged, s.SetInput(0,0,new(1,0))); Eq(0L, s.ReplayedTicks);
        Eq(InputUpdate.Replayed, s.SetInput(0,0,default)); Eq(5L,s.ReplayedTicks);
        var empty = new RollbackSession(Start()); empty.Advance(5); Eq(Simulation.Hash(empty.Current), Simulation.Hash(s.Current));
    }),
    ("history keeps inclusive oldest correctable tick", () => {
        var s = new RollbackSession(Start(), historyWindow:3); s.Advance(6); Eq(3,s.EarliestTick); Eq(4,s.SnapshotCount);
        Eq(InputUpdate.Replayed, s.SetInput(3,0,new(1,0))); Throws<ArgumentOutOfRangeException>(() => s.SetInput(2,0,new(1,0)));
    }),
    ("future horizon is inclusive and rejected updates are inert", () => {
        var s = new RollbackSession(Start(), futureWindow:2); Eq(InputUpdate.Scheduled,s.SetInput(2,0,new(1,0)));
        var hash = Simulation.Hash(s.Current); Throws<ArgumentOutOfRangeException>(() => s.SetInput(3,0,new(1,0)));
        Eq(hash,Simulation.Hash(s.Current)); Eq(1,s.InputFrameCount); s.Advance(3); Eq(3L,s.Current.Bodies[0].X);
    }),
    ("history and input frames stay bounded", () => {
        var s = new RollbackSession(Start(),historyWindow:7,futureWindow:4);
        for(int i=0;i<500;i++) { s.SetInput(s.Current.Tick,0,new(1,0)); s.Advance(); Yes(s.SnapshotCount<=8); Yes(s.InputFrameCount<=7); }
        Eq(493,s.EarliestTick); Throws<ArgumentOutOfRangeException>(() => s.SnapshotAt(492));
    }),
    ("old returned snapshots stay immutable after correction", () => {
        var s = new RollbackSession(Start()); s.Advance(5); var before = s.Current; var hash = Simulation.Hash(before);
        s.SetInput(0,0,new(1,0)); Eq(hash,Simulation.Hash(before)); Yes(hash!=Simulation.Hash(s.Current));
    }),
    ("sessions can resume from a nonzero snapshot", () => {
        var seed = new RollbackSession(Start()); seed.Advance(20); var resumed = new RollbackSession(seed.Current,historyWindow:3);
        Eq(20,resumed.EarliestTick); Throws<ArgumentOutOfRangeException>(() => resumed.SetInput(19,0,new(1,0)));
        resumed.Advance(4); Eq(21,resumed.EarliestTick);
    }),
    ("advance validates complete request before changing state", () => {
        var s = new RollbackSession(Start()); s.Advance(0); Eq(0,s.Current.Tick);
        Throws<ArgumentOutOfRangeException>(() => s.Advance(-1)); Throws<ArgumentOutOfRangeException>(() => s.Advance(10001)); Eq(0,s.Current.Tick);
    }),
    ("1000 seeded ticks match independent decimal axis model", () => {
        var cfg = new SimulationConfig(100,3,9,2); var state = Simulation.Create(cfg,[new Body(0,0,0)]); var model = new decimal[4]; var random = new Random(41);
        for(int i=0;i<1000;i++) {
            int x=random.Next(-1,2), y=random.Next(-1,2);
            ModelAxis(model,0,2,x); ModelAxis(model,1,3,y);
            state=Simulation.Step(state,Command(x,y)); var b=state.Bodies[0];
            Eq(model[0],(decimal)b.X); Eq(model[1],(decimal)b.Y); Eq(model[2],(decimal)b.VelocityX); Eq(model[3],(decimal)b.VelocityY);
        }
    }),
    ("shuffled delivery of 240 inputs converges to reference timeline", () => {
        var initial=Simulation.Create(new(),[new Body(0,0,0),new Body(1,10,20),new Body(2,-10,-20)]);
        var expected=new RollbackSession(initial,historyWindow:100,futureWindow:100);
        var actual=new RollbackSession(initial,historyWindow:100,futureWindow:100); var random=new Random(823);
        var events=new List<(int Tick,int Id,PlayerInput Input)>();
        for(int tick=0;tick<80;tick++) for(int id=0;id<3;id++) events.Add((tick,id,new(random.Next(-1,2),random.Next(-1,2))));
        foreach(var e in events) expected.SetInput(e.Tick,e.Id,e.Input); expected.Advance(80); actual.Advance(80);
        for(int i=events.Count-1;i>0;i--) { int j=random.Next(i+1); (events[i],events[j])=(events[j],events[i]); }
        foreach(var e in events) actual.SetInput(e.Tick,e.Id,e.Input);
        for(int tick=0;tick<=80;tick++) Eq(Simulation.Hash(expected.SnapshotAt(tick)),Simulation.Hash(actual.SnapshotAt(tick)));
    }),
    ("input log copies cannot be mutated by callers", () => {
        var s=new RollbackSession(Start()); s.SetInput(0,0,new(1,0)); var log=s.InputsAt(0);
        Throws<NotSupportedException>(() => ((IDictionary<int,PlayerInput>)log)[0]=new(-1,0));
        s.SetInput(0,0,new(0,1)); Eq(new PlayerInput(1,0),log[0]);
    })
};
int failures=0;
foreach(var (name,run) in specs) { try { run(); Console.WriteLine($"PASS {name}"); } catch(Exception ex) { failures++; Console.Error.WriteLine($"FAIL {name}: {ex}"); } }
Console.WriteLine($"{specs.Length-failures}/{specs.Length} specifications passed."); return failures==0?0:1;
static WorldState Start()=>Simulation.Create(new(),[new Body(0,0,0)]);
static Dictionary<int,PlayerInput> Command(int x,int y)=>new(){[0]=new(x,y)};
static void Yes(bool condition) { if(!condition) throw new Exception("Assertion failed."); }
static void Eq<T>(T expected,T actual) { if(!EqualityComparer<T>.Default.Equals(expected,actual)) throw new Exception($"Expected {expected}, got {actual}."); }
static void Throws<T>(Action action) where T:Exception { try { action(); } catch(T) { return; } throw new Exception($"Expected {typeof(T).Name}."); }
static void ModelAxis(decimal[] model,int p,int v,int input)
{
    // Deliberately uses decimal and branches rather than production long/clamp operations.
    if(input>0) model[v]+=3; else if(input<0) model[v]-=3;
    else if(model[v]>0) model[v]=model[v]<2?0:model[v]-2;
    else if(model[v]<0) model[v]=model[v]>-2?0:model[v]+2;
    if(model[v]>9) model[v]=9; if(model[v]<-9) model[v]=-9;
    model[p]+=model[v];
    if(model[p]>100) { model[p]=100; model[v]=0; }
    if(model[p]<-100) { model[p]=-100; model[v]=0; }
}
