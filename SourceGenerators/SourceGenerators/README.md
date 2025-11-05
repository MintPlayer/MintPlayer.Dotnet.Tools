# Dependency-Injection generators
This library contains source-generators to simplify dependency-injection in your application

## Getting started
You need to install both [`MintPlayer.SourceGenerators`](https://nuget.org/packages/MintPlayer.SourceGenerators) and [`MintPlayer.SourceGenerators.Attributes`](https://nuget.org/packages/MintPlayer.SourceGenerators.Attributes) packages in your project.

## Service-registration

Place this class in your abstractions library:

```csharp
public interface ICustomerService { }
```

Place this class in your implementation library:

```csharp
[Register(typeof(ICustomerService), "DemoServices")]
internal class CustomerService : ICustomerService { }
```

Now you get an extension method generated for you, which will register all services for you:

```csharp
var services = new ServiceCollection()
    .AddDemoServices()
    .BuildServiceProvider();
```

## Dependency Injection

Inject a registered service anywhere:

```csharp
public partial class CustomerController {
    [Inject] private readonly ICustomerService customerService;
}
```

The source-generator will generate the constructor for you. It supports DI when using inheritance too.

## Interface Implementation

There's also an analyzer that will check if all `public` class members are known on the implemented interface. The analyzer provides a code-fix to add the missing members.

```csharp
public interface ICustomerService { }

[Register(typeof(ICustomerService), "DemoServices")]
internal class CustomerService : ICustomerService {
    public Task<Customer> GetCustomer(int id) => throw new NotImplementedException();
}
```

This also works when the interface resides in an abstractions-library and the class resides in an implementation-library. Which is why this analyzer is so powerfull.

