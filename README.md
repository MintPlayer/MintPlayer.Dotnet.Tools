# MintPlayer.ObservableCollection
[![NuGet Version](https://img.shields.io/nuget/v/MintPlayer.ObservableCollection.svg?style=flat)](https://www.nuget.org/packages/MintPlayer.ObservableCollection)
[![NuGet](https://img.shields.io/nuget/dt/MintPlayer.ObservableCollection.svg?style=flat)](https://www.nuget.org/packages/MintPlayer.ObservableCollection)
[![Build Status](https://travis-ci.org/MintPlayer/MintPlayer.ObservableCollection.svg?branch=master)](https://travis-ci.org/MintPlayer/MintPlayer.ObservableCollection)
![.NET Core](https://github.com/MintPlayer/MintPlayer.ObservableCollection/workflows/.NET%20Core/badge.svg)
[![License](https://img.shields.io/badge/License-Apache%202.0-green.svg)](https://opensource.org/licenses/Apache-2.0)
[![Codacy Badge](https://api.codacy.com/project/badge/Grade/db8d61e702624302968b20e9746f7447)](https://app.codacy.com/gh/MintPlayer/MintPlayer.ObservableCollection?utm_source=github.com&utm_medium=referral&utm_content=MintPlayer/MintPlayer.ObservableCollection&utm_campaign=Badge_Grade_Dashboard)

Extended version of System.Collections.ObjectModel.ObservableCollection. This class allows you to:
1) Use AddRange, invoking the CollectionChanged event only once
2) Monitor properties of the items in the collection
3) Be used within a WPF project
4) Change the ObservableCollection from another Thread
## Installation
### NuGet package manager
Open the NuGet package manager and install the **MintPlayer.ObservableCollection** package in the project
### Package manager console
Install-Package MintPlayer.ObservableCollection

## WPF support
Xamarin.Forms is by default able to deal with Range Operations. WPF however (both .NET Framework and .NET Core) is unable to deal with Range Operations. This ObservableCollection does in fact support range operations (AddRange, RemoveRange) for WPF too.

A working example can be found in the [following repository](https://github.com/PieterjanDeClippel/WpfRangeOperations)
## Usage
### Example 1

    var collection = new ObservableCollection<string>();
    collection.CollectionChanged += (sender, e) =>
    {
        Console.WriteLine($"Collection changed:");
        if (!e.NewItems.IsNullOrEmpty())
        {
            var newItemsArray = new string[e.NewItems.Count];
            e.NewItems.CopyTo(newItemsArray, 0);
            Console.WriteLine($"- items added: {string.Join(", ", newItemsArray)}");
        }
        if (!e.OldItems.IsNullOrEmpty())
        {
            var oldItemsArray = new string[e.OldItems.Count];
            e.OldItems.CopyTo(oldItemsArray, 0);
            Console.WriteLine($"- items removed: {string.Join(", ", oldItemsArray)}");
        }
    };

    collection.Add("Michael");
    //collection.Enabled = false;
    collection.Add("Junior");
    //collection.Enabled = true;
    collection.Add("Jackson");

### Example 2

    public class Person : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }

        private string firstname;
        public string FirstName
        {
            get { return firstname; }
            set
            {
                firstname = value;
                OnPropertyChanged();
            }
        }

        private string lastname;
        public string LastName
        {
            get { return lastname; }
            set
            {
                lastname = value;
                OnPropertyChanged();
            }
        }

        public string FullName => $"{firstname} {lastname}";
    }
 
Program.cs

    var collection = new ObservableCollection<Person>();
    collection.CollectionChanged += (sender, e) =>
    {
        Console.WriteLine($"Collection changed:");
        if (!e.NewItems.IsNullOrEmpty())
        {
            var newItemsArray = new Person[e.NewItems.Count];
            e.NewItems.CopyTo(newItemsArray, 0);
            Console.WriteLine($"- items added: {string.Join(", ", newItemsArray.Select(p => p.FullName))}");
        }
        if (!e.OldItems.IsNullOrEmpty())
        {
            var oldItemsArray = new Person[e.OldItems.Count];
            e.OldItems.CopyTo(oldItemsArray, 0);
            Console.WriteLine($"- items removed: {string.Join(", ", oldItemsArray.Select(p => p.FullName))}");
        }
    };
    collection.ItemPropertyChanged += (sender, e) =>
    {
        Console.WriteLine($"Item property changed: {e.PropertyName}");
    };

    collection.AddRange(new[] {
        new Person { FirstName = "John", LastName = "Doe" },
        new Person { FirstName = "Jimmy", LastName = "Fallon" },
        new Person { FirstName = "Michael", LastName = "Douglas" }
    });

    collection[1].LastName = "Knibble";
