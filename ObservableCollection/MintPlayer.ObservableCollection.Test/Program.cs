using System;
using System.Linq;
using System.Threading;
using MintPlayer.ObservableCollection.Extensions;
using MintPlayer.ObservableCollection.Test.Extensions;

namespace MintPlayer.ObservableCollection.Test
{
    static class Program
    {
        static void Main(string[] args)
        {
            //Demo1();
            //Demo2();
            //Demo3();
            DemoMaxItemCount();
            Console.ReadKey();
        }

        private static void Demo1()
        {
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
            collection.Enabled = false;
            collection.Add("Junior");
            collection.Enabled = true;
            collection.Add("Jackson");
        }

        private static void Demo2()
        {
            var collection = new ObservableCollection<Person>();
            collection.CollectionChanged += (sender, e) =>
            {
                Console.WriteLine($"Collection changed:");
                if (!e.NewItems.IsNullOrEmpty())
                {
                    Console.WriteLine($"- items added: {string.Join(", ", e.NewItems.ToArray<Person>().Select(p => p.FullName))}");
                }
                if (!e.OldItems.IsNullOrEmpty())
                {
                    Console.WriteLine($"- items removed: {string.Join(", ", e.OldItems.ToArray<Person>().Select(p => p.FullName))}");
                }
            };
            collection.ItemPropertyChanged += (sender, e) =>
            {
                Console.WriteLine($"Item property changed: {e.PropertyName}");
            };

            new Thread(new ThreadStart(() =>
            {
                var person1 = new Person { FirstName = "John", LastName = "Doe" };
                var person2 = new Person { FirstName = "Jimmy", LastName = "Fallon" };
                var person3 = new Person { FirstName = "Michael", LastName = "Douglas" };

                // Add 3 items at once
                collection.AddRange([person1, person2, person3]);

                // Change an item property
                collection[1].LastName = "Knibble";

                // Replace item
                collection[1] = new Person { FirstName = "Sim", LastName = "Salabim" };

                // Remove 2 items at once
                collection.RemoveRange([person1, person3]);

                // Add some more people
                var person4 = new Person { FirstName = "Johnny", LastName = "Logan" };
                var person5 = new Person { FirstName = "Kiddy", LastName = "Bull" };
                var person6 = new Person { FirstName = "Jacky", LastName = "Chan" };
                collection.AddRange([person4, person5, person6]);

                // Remove range using index
                collection.RemoveRange(1, 2);
            })).Start();
        }

        private static void Demo3()
        {
            var col = new ObservableCollection<Person>();
            var people = new[] {
                new Person { FirstName = "Pieterjan", LastName = "De Clippel" },
                new Person { FirstName = "Sam", LastName = "Hunt" },
                new Person { FirstName = "Michael", LastName = "Jefferson" },
                new Person { FirstName = "Bill", LastName = "Belichick" },
            };
            col.AddRange(people);
        }

        private static void DemoMaxItemCount()
        {
            Console.WriteLine("MaxItemCount demo");
            const int maxItemCount = 20;
            var col = new ObservableCollection<int>();
            col.AddRange(Enumerable.Range(0, maxItemCount));
            Console.WriteLine(string.Join(", ", col));

            // Should remove from head
            col.Add(20, maxItemCount);
            Console.WriteLine(string.Join(", ", col));

            // Should remove from tail
            col.Insert(1, 21, maxItemCount);
            Console.WriteLine(string.Join(", ", col));

            // Should remove from head
            col.Insert(maxItemCount / 2, 22, maxItemCount);
            Console.WriteLine(string.Join(", ", col));

            // Should remove from tail
            col.Insert(maxItemCount / 2 - 1, 23, maxItemCount);
            Console.WriteLine(string.Join(", ", col));
        }
    }
}
