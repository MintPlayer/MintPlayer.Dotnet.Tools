using Newtonsoft.Json.Serialization;
using System.Text.RegularExpressions;

// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");

public interface IFaceBase : IValueProvider
{
    public string? Name { get; set; }
}

public interface IFace : IFaceBase
{
    //public string? Name { get; set; }
}

public class Face : IFace
{
    public Face()
    {
    }

    public string? Name { get; set; }

    public object? GetValue(object target) => null;

    public void SetValue(object target, object? value) { }
}
