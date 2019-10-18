namespace MintPlayer.ObservableCollection.Test.Extensions
{
    public static class IEnumerable
    {
        public static bool IsNullOrEmpty(this System.Collections.IList list)
        {
            if (list == null) return true;
            return list.Count == 0;
        }
        public static bool IsNullOrEmpty<T>(this System.Collections.Generic.IList<T> list)
        {
            if (list == null) return true;
            return list.Count == 0;
        }
    }
}
