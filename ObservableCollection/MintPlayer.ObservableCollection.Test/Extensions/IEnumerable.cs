using System.Collections;

namespace MintPlayer.ObservableCollection.Test.Extensions
{
    public static class IEnumerable
    {
        public static bool IsNullOrEmpty(this System.Collections.IList list)
        {
            if (list == null)
            {
                return true;
            }
            else
            {
                return list.Count == 0;
            }
        }
        public static bool IsNullOrEmpty<T>(this System.Collections.Generic.IList<T> list)
        {
            if (list == null)
            {
                return true;
            }
            else
            {
                return list.Count == 0;
            }
        }

        public static T[] ToArray<T>(this IList list)
        {
            var array = new T[list.Count];
            list.CopyTo(array, 0);
            return array;
        }
    }
}
