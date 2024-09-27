using MintPlayer.SourceGenerators.Tools;
using MintPlayer.SourceGenerators.ValueComparers;
using System;
using System.Collections.Generic;
using System.Text;

namespace MintPlayer.SourceGenerators.Models
{
    [ValueComparer(typeof(ClassDeclarationValueComparer))]
    internal class ClassDeclaration
    {
        public string? Name { get; set; }
    }
}
