using System;
using System.Linq;
using MintPlayer.ObservableCollection.Test.Extensions;

namespace MintPlayer.ObservableCollection.Test
{
    class Program
    {
        static void Main(string[] args)
        {
            Demo2();

            Console.ReadKey();
        }

        private static void Demo2()
        {


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

    var person1 = new Person { FirstName = "John", LastName = "Doe" };
    var person2 = new Person { FirstName = "Jimmy", LastName = "Fallon" };
    var person3 = new Person { FirstName = "Michael", LastName = "Douglas" };

    // Add 3 items at once
    collection.AddRange(new[] {
        person1,
        person2,
        person3
    });

    // Change an item property
    collection[1].LastName = "Knibble";

    // Remove 2 items at once
    collection.RemoveRange(new[] { person1, person3 });
        }

        //private static void Demo1()
        //{
        //    var collection = new ObservableCollection<string>();
        //    collection.CollectionChanged += (sender, e) =>
        //    {
        //        Console.WriteLine($"Collection changed:");
        //        if (!e.NewItems.IsNullOrEmpty())
        //        {
        //            var newItemsArray = new string[e.NewItems.Count];
        //            e.NewItems.CopyTo(newItemsArray, 0);
        //            Console.WriteLine($"- items added: {string.Join(", ", newItemsArray)}");
        //        }
        //        if (!e.OldItems.IsNullOrEmpty())
        //        {
        //            var oldItemsArray = new string[e.OldItems.Count];
        //            e.OldItems.CopyTo(oldItemsArray, 0);
        //            Console.WriteLine($"- items removed: {string.Join(", ", oldItemsArray)}");
        //        }
        //    };

        //    collection.Add("Michael");
        //    //collection.Enabled = false;
        //    collection.Add("Junior");
        //    //collection.Enabled = true;
        //    collection.Add("Jackson");
        //}
    }
}
