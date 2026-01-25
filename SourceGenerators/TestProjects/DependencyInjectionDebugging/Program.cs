using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MintPlayer.SourceGenerators.Attributes;

namespace DependencyInjectionDebugging;

public static class Program
{
    public static void Main(string[] args)
    {
        // Build configuration from appsettings.json
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        // Build service provider
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);

        // Register options
        services.Configure<EmailSettings>(configuration.GetSection("Email:Settings"));
        services.Configure<CustomerConfig>(configuration.GetSection("Customer"));

        // Register our config-based services
        services.AddTransient<SimpleConfigService>();
        services.AddTransient<DatabaseService>();
        services.AddTransient<ConfigAwareService>();
        services.AddTransient<EmailService>();
        services.AddTransient<NullableConfigService>();
        services.AddTransient<ArrayConfigService>();

        var serviceProvider = services.BuildServiceProvider();

        // Demo: SimpleConfigService
        Console.WriteLine("=== SimpleConfigService ===");
        var simpleService = serviceProvider.GetRequiredService<SimpleConfigService>();
        Console.WriteLine($"  App Name: {simpleService.AppName}");
        Console.WriteLine();

        // Demo: DatabaseService
        Console.WriteLine("=== DatabaseService ===");
        var dbService = serviceProvider.GetRequiredService<DatabaseService>();
        Console.WriteLine($"  Database Type: {dbService.DatabaseType}");
        Console.WriteLine($"  Max Retries: {dbService.MaxRetries}");
        Console.WriteLine($"  Timeout: {dbService.Timeout}");
        Console.WriteLine($"  Connection String: {dbService.ConnectionString}");
        Console.WriteLine();

        // Demo: ConfigAwareService (shows IConfiguration deduplication)
        Console.WriteLine("=== ConfigAwareService ===");
        var configAware = serviceProvider.GetRequiredService<ConfigAwareService>();
        Console.WriteLine($"  Version: {configAware.Version}");
        Console.WriteLine($"  Debug Mode: {configAware.DebugMode}");
        Console.WriteLine($"  Can also use IConfiguration directly: {configAware.GetCustomValue("App:Name")}");
        Console.WriteLine();

        // Demo: EmailService (complex types)
        Console.WriteLine("=== EmailService ===");
        var emailService = serviceProvider.GetRequiredService<EmailService>();
        Console.WriteLine($"  SMTP Credentials: {emailService.Credentials.Username} / {emailService.Credentials.Password}");
        Console.WriteLine($"  SMTP Server: {emailService.Settings.SmtpServer}:{emailService.Settings.Port} (SSL: {emailService.Settings.UseSsl})");
        Console.WriteLine($"  Email DB Connection (optional): {emailService.EmailDbConnection ?? "(not configured)"}");
        Console.WriteLine();

        // Demo: NullableConfigService (nullable types)
        Console.WriteLine("=== NullableConfigService ===");
        var nullableService = serviceProvider.GetRequiredService<NullableConfigService>();
        Console.WriteLine($"  Optional String: {nullableService.OptionalString ?? "(null)"}");
        Console.WriteLine($"  Optional Int: {nullableService.OptionalInt?.ToString() ?? "(null)"}");
        Console.WriteLine($"  Optional Enum: {nullableService.OptionalEnum?.ToString() ?? "(null)"}");
        Console.WriteLine();

        // Demo: ArrayConfigService (collections)
        Console.WriteLine("=== ArrayConfigService ===");
        var arrayService = serviceProvider.GetRequiredService<ArrayConfigService>();
        Console.WriteLine($"  Allowed Hosts: [{string.Join(", ", arrayService.AllowedHosts)}]");
        Console.WriteLine($"  Ports: [{string.Join(", ", arrayService.Ports ?? [])}]");
        Console.WriteLine();

        Console.WriteLine("All configuration values loaded successfully!");
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

#region Config Attribute Tests

public enum DatabaseType
{
    SqlServer,
    PostgreSql,
    MySql,
    Sqlite
}

public class SmtpCredentials
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class EmailSettings
{
    public string SmtpServer { get; set; } = string.Empty;
    public int Port { get; set; }
    public bool UseSsl { get; set; }
}

// C1: Basic string config
public partial class SimpleConfigService
{
    [Config("App:Name")]
    private readonly string appName;

    public string AppName => appName;
}

// C2: Multiple config types
public partial class DatabaseService
{
    [Config("Database:Type")]
    private readonly DatabaseType databaseType;

    [Config("Database:MaxRetries", DefaultValue = 3)]
    private readonly int maxRetries;

    [Config("Database:Timeout")]
    private readonly TimeSpan timeout;

    [ConnectionString("DefaultConnection")]
    private readonly string connectionString;

    public DatabaseType DatabaseType => databaseType;
    public int MaxRetries => maxRetries;
    public TimeSpan Timeout => timeout;
    public string ConnectionString => connectionString;
}

// C3: Config with explicit IConfiguration (no duplication)
public partial class ConfigAwareService
{
    [Inject] private readonly IConfiguration configuration;

    [Config("App:Version")]
    private readonly string version;

    [Config("App:DebugMode")]
    private readonly bool debugMode;

    public string Version => version;
    public bool DebugMode => debugMode;
    public string? GetCustomValue(string key) => configuration[key];
}

// C4: Complex type config
public partial class EmailService
{
    [Config("Email:Credentials")]
    private readonly SmtpCredentials credentials;

    [Config("Email:Settings")]
    private readonly EmailSettings settings;

    [ConnectionString("EmailDb")]
    private readonly string? emailDbConnection;

    public SmtpCredentials Credentials => credentials;
    public EmailSettings Settings => settings;
    public string? EmailDbConnection => emailDbConnection;
}

// C5: Options pattern
public partial class OptionsService
{
    [Options("Email")]
    private readonly IOptions<EmailSettings> emailOptions;

    [Options("Customer")]
    private readonly IOptionsSnapshot<CustomerConfig> customerOptions;

    [Inject]
    private readonly ICustomerService customerService;
}

// C6: Mix of Inject, Config, ConnectionString, and Options
public partial class FullFeaturedService
{
    [Inject] private readonly IConfiguration configuration;
    [Inject] private readonly ICustomerService customerService;

    [Config("App:Name")]
    private readonly string appName;

    [Config("App:MaxConnections", DefaultValue = 100)]
    private readonly int maxConnections;

    [ConnectionString("MainDb")]
    private readonly string mainDbConnection;

    [Options("Features")]
    private readonly IOptionsMonitor<CustomerConfig> featureOptions;

    [PostConstruct]
    private void OnInitialized()
    {
        // All values available here
    }
}

// C7: Nullable types (optional by nullability inference)
public partial class NullableConfigService
{
    [Config("Optional:String")]
    private readonly string? optionalString;

    [Config("Optional:Int")]
    private readonly int? optionalInt;

    [Config("Optional:Enum")]
    private readonly DatabaseType? optionalEnum;

    public string? OptionalString => optionalString;
    public int? OptionalInt => optionalInt;
    public DatabaseType? OptionalEnum => optionalEnum;
}

// C8: Array config
public partial class ArrayConfigService
{
    [Config("AllowedHosts")]
    private readonly string[] allowedHosts;

    [Config("Ports")]
    private readonly int[]? ports;

    public string[] AllowedHosts => allowedHosts;
    public int[]? Ports => ports;
}

#endregion