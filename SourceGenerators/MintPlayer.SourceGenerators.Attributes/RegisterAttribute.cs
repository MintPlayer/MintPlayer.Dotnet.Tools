using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;

namespace MintPlayer.SourceGenerators.Attributes
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class RegisterAttribute : Attribute
    {
        public RegisterAttribute(Type interfaceType, ServiceLifetime lifetime) { }
    }
}
