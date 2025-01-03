using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MintPlayer.ObservableCollection.Test;

public class Person : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        if (PropertyChanged != null)
        {
            PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    private string firstname;
    public string FirstName
    {
        get => firstname;
        set
        {
            firstname = value;
            OnPropertyChanged();
        }
    }

    private string lastname;
    public string LastName
    {
        get => lastname;
        set
        {
            lastname = value;
            OnPropertyChanged();
        }
    }

    public string FullName => $"{firstname} {lastname}";

    public override string ToString() => FullName;
}
