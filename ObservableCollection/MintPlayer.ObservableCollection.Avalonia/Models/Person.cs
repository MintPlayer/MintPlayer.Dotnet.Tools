using CommunityToolkit.Mvvm.ComponentModel;

namespace MintPlayer.ObservableCollection.Avalonia.Models;

public partial class Person : ObservableObject
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
}