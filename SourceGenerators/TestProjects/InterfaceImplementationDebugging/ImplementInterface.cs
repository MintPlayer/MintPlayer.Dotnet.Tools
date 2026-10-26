using MintPlayer.SourceGenerators.Attributes;

namespace InterfaceImplementationDebugging;

public class Customer { }
public class Car { }

public interface ICustomerManagement
{
    IList<Customer> Customers { get; set; }
}

public interface IWarehouseManagement
{
    IList<Car> Cars { get; set; }
    string Location { get; init; }
}

public interface IAudited
{
    DateTime? ModifiedOn { get; set; }
}

// Generates Customers, Cars and Location, and adds both interfaces to the base list.
[ImplementInterface(typeof(ICustomerManagement), typeof(IWarehouseManagement))]
public partial class CarDealerContext { }

// The generic form, explicitly implemented.
[ImplementInterface<IAudited>(Explicit = true)]
public partial class CarDealerContext { }

public interface IRepository
{
    string ConnectionString { get; set; }
    int Count();
}

// An abstract class gets the read/write property and an abstract Count() to override.
[ImplementInterface(typeof(IRepository))]
public abstract partial class RepositoryBase { }

public sealed class CarRepository : RepositoryBase
{
    public override int Count() => 0;
}
