using Microsoft.Extensions.DependencyInjection;
using MintPlayer.SourceGenerators.Attributes;

namespace MintPlayer.SourceGenerators.Debug.GenericTests;

#region Base types for constraints

/// <summary>
/// Base entity class similar to IdentityUser&lt;TKey&gt;.
/// Used as a type constraint for generic repositories.
/// </summary>
public class EntityBase<TKey>
    where TKey : IEquatable<TKey>
{
    public TKey Id { get; set; } = default!;
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// A concrete entity type for testing.
/// </summary>
public class UserEntity : EntityBase<Guid>
{
    public string Email { get; set; } = string.Empty;
}

/// <summary>
/// Another concrete entity for testing with int key.
/// </summary>
public class ProductEntity : EntityBase<int>
{
    public decimal Price { get; set; }
}

#endregion

#region Generic interface with type constraints

/// <summary>
/// Generic repository interface with type constraints.
/// Similar to IMustChangePasswordService&lt;TUser, TKey&gt;.
/// </summary>
/// <typeparam name="TEntity">Type of entity, must inherit from EntityBase&lt;TKey&gt;.</typeparam>
/// <typeparam name="TKey">Type of the entity's ID, must be IEquatable.</typeparam>
public interface IGenericRepository<TEntity, TKey>
    where TEntity : EntityBase<TKey>
    where TKey : IEquatable<TKey>
{
    TEntity? GetById(TKey id);
    IEnumerable<TEntity> GetAll();
    void Save(TEntity entity);
    void Delete(TKey id);
}

#endregion

#region Implementation with [Register] attribute

/// <summary>
/// Generic repository implementation.
/// The expected generated extension method should be:
/// <code>
/// public static IServiceCollection AddGenericRepository&lt;TEntity, TKey&gt;(this IServiceCollection services)
///     where TEntity : EntityBase&lt;TKey&gt;
///     where TKey : IEquatable&lt;TKey&gt;
/// {
///     return services.AddScoped&lt;IGenericRepository&lt;TEntity, TKey&gt;, GenericRepository&lt;TEntity, TKey&gt;&gt;();
/// }
/// </code>
/// </summary>
[Register(typeof(IGenericRepository<,>), ServiceLifetime.Scoped, "GenericRepository")]
public class GenericRepository<TEntity, TKey> : IGenericRepository<TEntity, TKey>
    where TEntity : EntityBase<TKey>
    where TKey : IEquatable<TKey>
{
    private readonly List<TEntity> _store = new();

    public TEntity? GetById(TKey id) => _store.FirstOrDefault(e => e.Id.Equals(id));
    public IEnumerable<TEntity> GetAll() => _store.AsReadOnly();
    public void Save(TEntity entity) => _store.Add(entity);
    public void Delete(TKey id) => _store.RemoveAll(e => e.Id.Equals(id));
}

#endregion

#region Second generic service - Same methodNameHint to test grouping

/// <summary>
/// A cache service with generic type constraints.
/// Uses the same methodNameHint "GenericRepository" to test grouping.
/// </summary>
public interface IGenericCache<TEntity, TKey>
    where TEntity : EntityBase<TKey>
    where TKey : IEquatable<TKey>
{
    TEntity? Get(TKey id);
    void Set(TEntity entity);
}

/// <summary>
/// Cache implementation - also uses "GenericRepository" methodNameHint.
/// This tests the scenario: what happens when multiple generic services
/// with different type parameters share the same method name?
/// </summary>
[Register(typeof(IGenericCache<,>), ServiceLifetime.Singleton, "GenericRepository")]
public class GenericCache<TEntity, TKey> : IGenericCache<TEntity, TKey>
    where TEntity : EntityBase<TKey>
    where TKey : IEquatable<TKey>
{
    private readonly Dictionary<TKey, TEntity> _cache = new();

    public TEntity? Get(TKey id) => _cache.TryGetValue(id, out var entity) ? entity : default;
    public void Set(TEntity entity) => _cache[entity.Id] = entity;
}

#endregion

#region Third generic service - Different arity (single type parameter)

/// <summary>
/// A simpler generic service with only one type parameter constraint.
/// Uses same methodNameHint "GenericRepository" to test overloading by arity.
/// This SHOULD work because C# allows overloading by type parameter count.
/// </summary>
public interface ISimpleGenericService<T>
    where T : class, new()
{
    T Create();
}

/// <summary>
/// Implementation with simpler constraint.
/// Same methodNameHint "GenericRepository" - tests overload by arity.
/// Expected: AddGenericRepository&lt;T&gt;() alongside AddGenericRepository&lt;TEntity, TKey&gt;()
/// </summary>
[Register(typeof(ISimpleGenericService<>), ServiceLifetime.Transient, "GenericRepository")]
public class SimpleGenericService<T> : ISimpleGenericService<T>
    where T : class, new()
{
    public T Create() => new T();
}

#endregion

#region Fourth scenario - Incompatible constraints with same arity

/// <summary>
/// Another 2-type-parameter service with DIFFERENT constraints.
/// This tests the edge case where we CANNOT combine into same method.
/// </summary>
public interface IKeyValueStore<TKey, TValue>
    where TKey : notnull
    where TValue : class
{
    void Store(TKey key, TValue value);
    TValue? Retrieve(TKey key);
}

/// <summary>
/// Implementation with different constraints than GenericRepository.
/// Same methodNameHint "GenericRepository" but incompatible constraints.
/// GenericRepository has: where TEntity : EntityBase&lt;TKey&gt;, where TKey : IEquatable&lt;TKey&gt;
/// This has: where TKey : notnull, where TValue : class
///
/// Cannot generate same method signature - must use suffix.
/// Expected: AddGenericRepository_KeyValueStore&lt;TKey, TValue&gt;()
/// </summary>
[Register(typeof(IKeyValueStore<,>), ServiceLifetime.Scoped, "GenericRepository")]
public class KeyValueStore<TKey, TValue> : IKeyValueStore<TKey, TValue>
    where TKey : notnull
    where TValue : class
{
    private readonly Dictionary<TKey, TValue> _store = new();

    public void Store(TKey key, TValue value) => _store[key] = value;
    public TValue? Retrieve(TKey key) => _store.TryGetValue(key, out var value) ? value : default;
}

#endregion

#region Fifth scenario - Separate methodNameHint for comparison

/// <summary>
/// Service with its own methodNameHint for clean generation.
/// </summary>
public interface IAuditService<TAuditEntry>
    where TAuditEntry : class, new()
{
    void Log(TAuditEntry entry);
}

[Register(typeof(IAuditService<>), ServiceLifetime.Singleton, "AuditServices")]
public class AuditService<TAuditEntry> : IAuditService<TAuditEntry>
    where TAuditEntry : class, new()
{
    public void Log(TAuditEntry entry) { /* log */ }
}

#endregion

#region Usage example - demonstrating the generated methods work

/// <summary>
/// A sample class that satisfies the "class, new()" constraint.
/// </summary>
public class SampleClass
{
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// A sample audit entry class.
/// </summary>
public class AuditLogEntry
{
    public DateTime Timestamp { get; set; }
    public string Action { get; set; } = string.Empty;
}

/// <summary>
/// Example of how the generated extension methods are used.
/// This code compiles, proving the generator works correctly!
/// </summary>
public static class GenericServicesUsageExample
{
    public static void ConfigureServices(IServiceCollection services)
    {
        // ============================================================
        // GENERATED METHODS FOR "GenericRepository" methodNameHint:
        // ============================================================

        // 1. Two-type-parameter version (GenericRepository + GenericCache - compatible constraints):
        services.AddGenericRepository<UserEntity, Guid>();
        services.AddGenericRepository<ProductEntity, int>();

        // 2. Single-type-parameter version (SimpleGenericService - different arity = valid overload):
        services.AddGenericRepository<SampleClass>();

        // 3. Two-type-parameter with INCOMPATIBLE constraints (KeyValueStore - suffixed method):
        services.AddGenericRepository_KeyValueStore<string, UserEntity>();

        // ============================================================
        // GENERATED METHOD FOR "AuditServices" methodNameHint:
        // ============================================================
        services.AddAuditServices<AuditLogEntry>();
    }
}

#endregion
