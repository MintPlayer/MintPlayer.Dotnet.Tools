// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");

public interface IFaceBase
{
    public string? Name { get; set; }
}

public interface IFace : IFaceBase
{
    //public string? Name { get; set; }
}

public class Face : IFace
{
    public string? Name { get; set; }
}
