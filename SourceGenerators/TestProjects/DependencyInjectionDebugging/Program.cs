﻿using Microsoft.Extensions.Options;
using MintPlayer.SourceGenerators.Attributes;

namespace DependencyInjectionDebugging;

public static class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("Hello, World!");
    }
}


public class CustomerConfig { }

public partial class CustomerController
{
    [Inject] private readonly IOptions<CustomerConfig> customerOptions;
    [Inject] private readonly ICustomerService customerService;
    [Inject] private readonly IProductService productService;
}

public partial class HttpWithBaseUrlService
{
    [Inject] private readonly HttpClient httpClient;
}

public partial class ApiServiceBase : HttpWithBaseUrlService
{
    [Inject] private readonly ITestService testService;
}

public interface ITestService { }

public interface ICustomerService { }

public interface IProductService { }

public interface ICustomerRepository { }

public interface IProductRepository { }

public partial class CustomerService : ApiServiceBase, ICustomerService
{
    [Inject] private readonly ICustomerRepository customerRepository;
}

public partial class ProductService : ApiServiceBase, IProductService
{
    [Inject] private readonly IProductRepository productRepository;
}