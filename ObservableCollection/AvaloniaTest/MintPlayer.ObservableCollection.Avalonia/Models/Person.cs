using CommunityToolkit.Mvvm.ComponentModel;

namespace MintPlayer.ObservableCollection.Avalonia.Models;

public class PersonAgeEqualityComparer : IEqualityComparer<Person>
{
    public bool Equals(Person x, Person y)
    {
        if (ReferenceEquals(x, y))
            return true;

        return x.Age == y.Age;
    }

    public int GetHashCode(Person obj)
    {
        // Custom logic for generating hash code
        return obj.Age.GetHashCode();
    }
}

public partial class Person : ObservableObject, IEquatable<Person>
{
    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private int _age;

    public Person(string name, int age)
    {
        _name = name;
        _age = age;
    }

    public override string ToString()
    {
        return $"{nameof(Name)}: {Name}, {nameof(Age)}: {Age}";
    }

    public bool Equals(Person? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return _name == other._name && _age == other._age;
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((Person)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_name, _age);
    }

    public static bool operator ==(Person? left, Person? right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(Person? left, Person? right)
    {
        return !Equals(left, right);
    }
}