using System;
using System.Collections;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MintPlayer.ObservableCollection.Avalonia.Models;
using MintPlayer.ObservableCollection.Events.EventArgs;
using MintPlayer.ObservableCollection.Extensions;

namespace MintPlayer.ObservableCollection.Avalonia.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public ObservableCollection<Person> People { get; } = [];

    [ObservableProperty]
    private string _lastItemChange = "(No property changed yet)";

    public MainViewModel()
    {
        People.Add(new Person("John Doe", 30));
        People.Add(new Person("Jane Doe", 25));

        People.ItemPropertyChanged += PeopleOnItemPropertyChanged;
    }

    private void PeopleOnItemPropertyChanged(object sender, ItemPropertyChangedEventArgs<Person> e)
    {
        LastItemChange = $"{e.Item} changed {e.PropertyName} ({DateTime.Now})";
        
    }

    [RelayCommand]
    public void AddPeople()
    {
        const int count = 5;
        var people = new Person[count];
        for (int i = 0; i < count; i++)
        {
            people[i] = new Person($"Person {i + 1}", Random.Shared.Next(20, 100));
        }

        People.AddRange(people);
    }

    [RelayCommand]
    public void RemoveSelectedPeople(IEnumerable people)
    {
        People.RemoveRange(people);
    }

    [RelayCommand]
    public void RemoveRangePeople()
    {
        if (People.Count <= 1) People.Clear();
        else People.RemoveRange(1,2);
    }

    [RelayCommand]
    public void ClearPeople()
    {
        var people = People.FirstOrDefault();
        People.Clear();

        // Test notification, should not trigger!
        if (people is not null)
        {
            people.Age = Random.Shared.Next(20, 100);
        }
    }
}
