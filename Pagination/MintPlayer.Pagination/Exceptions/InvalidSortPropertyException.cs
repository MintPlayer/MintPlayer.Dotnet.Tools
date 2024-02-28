namespace MintPlayer.Pagination.Exceptions;

public class InvalidSortPropertyException : Exception
{
    internal InvalidSortPropertyException(string propertyName) : base($@"The specified sorting property ""{propertyName}"" does not exist.")
    {
    }
}