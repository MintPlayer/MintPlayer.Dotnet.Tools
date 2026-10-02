namespace InterfaceImplementationDebugging.Overrides;

// Issue #191: INTF001 must not report `override` members. The contract of an override belongs to the
// base class it overrides, not to the interface the derived class implements.

public interface IWorker
{
    void Declared();
}

public abstract class WorkerBase
{
    public abstract void Work();
    public virtual string Name { get; set; } = string.Empty;
    public virtual void Reset() { }
}

public class Worker : WorkerBase, IWorker
{
    public void Declared() { }

    // Overrides of object members: never reported.
    public override string ToString() => nameof(Worker);
    public override bool Equals(object? obj) => ReferenceEquals(this, obj);
    public override int GetHashCode() => 0;

    // Override of an abstract base member: never reported.
    public override void Work() { }

    // Override of a single accessor: never reported.
    public override string Name { get => base.Name; }

    // Sealed override: never reported.
    public sealed override void Reset() { }

    // Genuinely new public surface: still reported (INTF001).
    public void Extra() { }
}

public interface IBar
{
    void Run();
}

public abstract class BarBase : IBar
{
    public abstract void Run();
}

// Run is declared on IBar, reached through the base class. The override is not reported.
public class Bar : BarBase, IWorker
{
    public void Declared() { }
    public override void Run() { }
}

public class Hider : WorkerBase, IWorker
{
    public void Declared() { }
    public override void Work() { }

    // `new` hides rather than overrides: still reported (INTF001).
    public new void Reset() { }
}

public record Thing : IWorker
{
    public void Declared() { }

    // A hand-written override in a record: never reported.
    public override string ToString() => nameof(Thing);
}
