﻿// See https://aka.ms/new-console-template for more information
using Microsoft.Extensions.DependencyInjection;
using MintPlayer.SourceGenerators.Attributes;

Console.WriteLine("Hello, World!");

public interface IBaseTestService1 { }
public interface ITestService1 : IBaseTestService1 { }
[Register(typeof(IBaseTestService1), ServiceLifetime.Scoped)]
public class TestService1 : ITestService1 { }

public interface ITestService2 { }
[Register(typeof(ITestService2), ServiceLifetime.Transient)]
public class TestService2 : ITestService2 { }

public interface ITestService3
{
    string GetMessage();
}
[Register(typeof(ITestService3), ServiceLifetime.Singleton, "CoreData")]
public class TestService3 : ITestService3
{
    public string GetMessage() => "Hello world";
}

public interface ITestService4 { }
[Register(typeof(ITestService4), ServiceLifetime.Scoped)]
public class TestService4 : ITestService4 { }