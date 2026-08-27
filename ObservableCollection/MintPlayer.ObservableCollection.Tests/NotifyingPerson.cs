using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MintPlayer.ObservableCollection.Tests;

/// <summary>
/// An INotifyPropertyChanged item, so the collection takes its
/// areItemsImplementingINotifyPropertyChanged path and wires up ItemPropertyChanged.
/// Ported from the old console harness's Person.
/// </summary>
internal sealed class NotifyingPerson : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string propertyName = "")
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private string _firstName = string.Empty;
    public string FirstName
    {
        get => _firstName;
        set { _firstName = value; OnPropertyChanged(); }
    }

    private string _lastName = string.Empty;
    public string LastName
    {
        get => _lastName;
        set { _lastName = value; OnPropertyChanged(); }
    }

    public string FullName => $"{_firstName} {_lastName}";

    public override string ToString() => FullName;
}

/// <summary>A plain item, so the INotifyPropertyChanged wiring stays out of the way.</summary>
internal sealed record Plain(string Name);

/// <summary>Compares only the first character, to exercise the comparer overloads.</summary>
internal sealed class FirstLetterComparer : IEqualityComparer<string>
{
    public bool Equals(string? x, string? y)
    {
        if (x is null || y is null) return x is null && y is null;
        return x.Length > 0 && y.Length > 0 && x[0] == y[0];
    }

    public int GetHashCode(string obj) => obj.Length == 0 ? 0 : obj[0].GetHashCode();
}
