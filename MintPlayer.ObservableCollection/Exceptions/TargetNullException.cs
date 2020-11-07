using System;

namespace MintPlayer.ObservableCollection.Exceptions
{
    public class TargetNullException : Exception
    {
        public TargetNullException() : base("The target of the specified CollectionChanged handler is null")
        {
        }
    }
}
