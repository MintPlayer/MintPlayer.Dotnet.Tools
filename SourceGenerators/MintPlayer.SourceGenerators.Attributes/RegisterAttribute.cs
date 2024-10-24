using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;

namespace MintPlayer.SourceGenerators.Attributes
{
    // For now don't allow multiple registrations of the same service
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class RegisterAttribute : Attribute
    {
        public RegisterAttribute(Type interfaceType, ServiceLifetime lifetime, string methodNameHint = "") { }
    }
}
