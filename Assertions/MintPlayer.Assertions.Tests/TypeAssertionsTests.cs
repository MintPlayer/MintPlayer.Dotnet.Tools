using MintPlayer.Assertions;

namespace MintPlayer.Assertions.Tests;

public class TypeAssertionsTests
{
    [AttributeUsage(AttributeTargets.Class)]
    private sealed class MarkerAttribute : Attribute
    {
        public MarkerAttribute(string name) => Name = name;
        public string Name { get; }
    }

    private interface IAnimal { }
    private abstract class Animal : IAnimal { }
    [Marker("dog")]
    private sealed class Dog : Animal { }
    private static class Helpers { }

    [Fact]
    public void Be_Generic_Passes() => typeof(Dog).Should().Be<Dog>();

    [Fact]
    public void Be_Fails_WhenDifferent()
    {
        var ex = Record.Exception(() => typeof(Dog).Should().Be<Animal>());
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be", ex.Message);
        Assert.Contains("Animal", ex.Message);
        Assert.Contains("Dog", ex.Message);
    }

    [Fact]
    public void Be_Type_Passes() => typeof(Dog).Should().Be(typeof(Dog));

    [Fact]
    public void Be_Type_Fails_WhenDifferent()
    {
        var ex = Record.Exception(() => typeof(Dog).Should().Be(typeof(string)));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be", ex.Message);
    }

    [Fact]
    public void NotBe_Passes_WhenDifferent() => typeof(Dog).Should().NotBe<Animal>();

    [Fact]
    public void NotBe_Fails_WhenEqual()
    {
        var ex = Record.Exception(() => typeof(Dog).Should().NotBe(typeof(Dog)));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Did not expect", ex.Message);
    }

    [Fact]
    public void BeAssignableTo_Passes_ForBaseClass() => typeof(Dog).Should().BeAssignableTo<Animal>();

    [Fact]
    public void BeAssignableTo_Passes_ForInterface() => typeof(Dog).Should().BeAssignableTo<IAnimal>();

    [Fact]
    public void BeAssignableTo_Fails_WhenUnrelated()
    {
        var ex = Record.Exception(() => typeof(Dog).Should().BeAssignableTo<IDisposable>());
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be assignable to", ex.Message);
    }

    [Fact]
    public void BeDerivedFrom_Passes() => typeof(Dog).Should().BeDerivedFrom<Animal>();

    [Fact]
    public void BeDerivedFrom_Fails_ForSelf()
    {
        var ex = Record.Exception(() => typeof(Dog).Should().BeDerivedFrom<Dog>());
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be derived from", ex.Message);
    }

    [Fact]
    public void Implement_Passes() => typeof(Dog).Should().Implement<IAnimal>();

    [Fact]
    public void Implement_Fails_WhenNotImplemented()
    {
        var ex = Record.Exception(() => typeof(Dog).Should().Implement<IDisposable>());
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to implement", ex.Message);
    }

    [Fact]
    public void Implement_Throws_WhenNotAnInterface()
    {
        var ex = Record.Exception(() => typeof(Dog).Should().Implement<Animal>());
        Assert.IsType<ArgumentException>(ex);
    }

    [Fact]
    public void BeDecoratedWith_Passes_AndExposesAttribute()
    {
        var attribute = typeof(Dog).Should().BeDecoratedWith<MarkerAttribute>().Which;
        Assert.Equal("dog", attribute.Name);
    }

    [Fact]
    public void BeDecoratedWith_Fails_WhenNotDecorated()
    {
        var ex = Record.Exception(() => typeof(Animal).Should().BeDecoratedWith<MarkerAttribute>());
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be decorated with", ex.Message);
    }

    [Fact]
    public void BeDecoratedWith_Predicate_Passes()
        => typeof(Dog).Should().BeDecoratedWith<MarkerAttribute>(a => a.Name == "dog");

    [Fact]
    public void BeDecoratedWith_Predicate_Fails_WhenNoMatch()
    {
        var ex = Record.Exception(() => typeof(Dog).Should().BeDecoratedWith<MarkerAttribute>(a => a.Name == "cat"));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("matching the given predicate", ex.Message);
    }

    [Fact]
    public void NotBeDecoratedWith_Passes_WhenNotDecorated() => typeof(Animal).Should().NotBeDecoratedWith<MarkerAttribute>();

    [Fact]
    public void NotBeDecoratedWith_Fails_WhenDecorated()
    {
        var ex = Record.Exception(() => typeof(Dog).Should().NotBeDecoratedWith<MarkerAttribute>());
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Did not expect", ex.Message);
        Assert.Contains("to be decorated with", ex.Message);
    }

    [Fact]
    public void BeAbstract_Passes_ForAbstractClass() => typeof(Animal).Should().BeAbstract();

    [Fact]
    public void BeAbstract_Fails_ForConcreteClass()
    {
        var ex = Record.Exception(() => typeof(Dog).Should().BeAbstract());
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be abstract", ex.Message);
    }

    [Fact]
    public void BeSealed_Passes_ForSealedClass() => typeof(Dog).Should().BeSealed();

    [Fact]
    public void BeSealed_Fails_ForAbstractClass()
    {
        var ex = Record.Exception(() => typeof(Animal).Should().BeSealed());
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be sealed", ex.Message);
    }

    [Fact]
    public void BeStatic_Passes_ForStaticClass() => typeof(Helpers).Should().BeStatic();

    [Fact]
    public void BeStatic_Fails_ForInstanceClass()
    {
        var ex = Record.Exception(() => typeof(Dog).Should().BeStatic());
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be static", ex.Message);
    }

    [Fact]
    public void BeAnInterface_Passes() => typeof(IAnimal).Should().BeAnInterface();

    [Fact]
    public void BeAnInterface_Fails_ForClass()
    {
        var ex = Record.Exception(() => typeof(Dog).Should().BeAnInterface());
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be an interface", ex.Message);
    }

    [Fact]
    public void BeAClass_Passes() => typeof(Dog).Should().BeAClass();

    [Fact]
    public void BeAClass_Fails_ForInterface()
    {
        var ex = Record.Exception(() => typeof(IAnimal).Should().BeAClass());
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be a class", ex.Message);
    }

    [Fact]
    public void NullSubject_Fails()
    {
        Type? type = null;
        var ex = Record.Exception(() => type.Should().BeAClass());
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("but found <null>", ex.Message);
    }

    [Fact]
    public void Chaining_Works() => typeof(Dog).Should().BeAClass().And.BeSealed().And.BeAssignableTo<IAnimal>();
}
