using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Text;

namespace MintPlayer.SourceGenerators.Generators
{
    [Generator(LanguageNames.CSharp)]
    public class DependencyInjectionGenerator : IIncrementalGenerator
    {
        public DependencyInjectionGenerator()
        {
        }
            
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
        }
    }
}
