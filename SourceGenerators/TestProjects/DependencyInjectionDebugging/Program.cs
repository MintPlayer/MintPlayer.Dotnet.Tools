using Microsoft.Extensions.Options;
using MintPlayer.SourceGenerators.Attributes;

namespace DependencyInjectionDebugging;

public static class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("Hello, World!");
    }
}


public class CustomerConfig { }

public partial class CustomerController
{
    [Inject] private readonly IOptions<CustomerConfig> customerOptions;
    [Inject] private readonly ICustomerService customerService;
    [Inject] private readonly IProductService productService;
}

public partial class HttpWithBaseUrlService
{
    [Inject] private readonly HttpClient httpClient;
}

public partial class ApiServiceBase : HttpWithBaseUrlService
{
    [Inject] private readonly ITestService testService;
}

public interface ITestService { }

public interface ICustomerService { }

public interface IProductService { }

public interface ICustomerRepository { }

public interface IProductRepository { }

public partial class CustomerService : ApiServiceBase, ICustomerService
{
    [Inject] private readonly ICustomerRepository customerRepository;
}

public partial class ProductService : ApiServiceBase, IProductService
{
    [Inject] private readonly IProductRepository productRepository;
}

#region Generic Class Tests

// T1: Simple generic class with one type parameter
public interface IDbContext<TEntity> { }

public partial class Repository<TEntity>
{
    [Inject] private readonly IDbContext<TEntity> context;
}

// T2: Generic class with multiple type parameters
public interface IStore<TEntity, TKey> { }

public partial class KeyedRepository<TEntity, TKey>
{
    [Inject] private readonly IStore<TEntity, TKey> store;
}

// T5: Multiple constraint types
public interface IEntity { }
public interface IFactory<T> { }

public partial class GenericService<T>
    where T : class, IEntity, new()
{
    [Inject] private readonly IFactory<T> factory;
}

// T7: Constraints referencing other type parameters (like MustChangePasswordService)
public class IdentityUser<TKey> { }
public class UserManager<TUser> { }
public class PasswordManager<TUser> { }

public partial class UserService<TUser, TKey>
    where TUser : IdentityUser<TKey>
    where TKey : IEquatable<TKey>
{
    [Inject] private readonly UserManager<TUser> userManager;
}

public partial class PasswordService<TUser, TKey>
    where TUser : IdentityUser<TKey>
    where TKey : IEquatable<TKey>
{
    [Inject] private readonly UserService<TUser, TKey> userService;
    [Inject] private readonly PasswordManager<TUser> passwordManager;
}

// T8: Nested generic classes
public interface ICombined<TOuter, TInner> { }

public partial class OuterService<TOuter>
{
    public partial class InnerService<TInner>
    {
        [Inject] private readonly ICombined<TOuter, TInner> combined;
    }
}

// T9: Generic class inheriting from generic base with [Inject]
public partial class BaseGenericService<T>
{
    [Inject] private readonly IFactory<T> baseFactory;
}

public partial class DerivedGenericService<T> : BaseGenericService<T>
    where T : class, IEntity, new()
{
    [Inject] private readonly IDbContext<T> derivedContext;
}

// T10: Generic class with PostConstruct
public partial class GenericServiceWithPostConstruct<T>
    where T : class
{
    [Inject] private readonly IFactory<T> factory;

    [PostConstruct]
    private void Initialize()
    {
        // Initialization logic
    }
}

#endregion