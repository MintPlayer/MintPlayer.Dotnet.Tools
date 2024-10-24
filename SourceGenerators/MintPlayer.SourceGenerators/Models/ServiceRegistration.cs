using Microsoft.Extensions.DependencyInjection;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.SourceGenerators.ValueComparers;
using System;
using System.Collections.Generic;
using System.Text;

namespace MintPlayer.SourceGenerators.Models
{
    [ValueComparer(typeof(ServiceRegistrationComparer))]
    public class ServiceRegistration
    {
        //public ServiceDependency[] Dependencies { get; set; } = new ServiceDependency[0];
        public string? ServiceTypeName { get; set; }
        public string? ImplementationTypeName { get; set; }
        public ServiceLifetime Lifetime { get; set; }
    }

    //public class ServiceDependency
    //{
    //}
}
