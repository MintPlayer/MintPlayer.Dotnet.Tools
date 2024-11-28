using MintPlayer.SourceGenerators.Tools;
using MintPlayer.SourceGenerators.ValueComparers;
using System;
using System.Collections.Generic;
using System.Text;

namespace MintPlayer.SourceGenerators.Models
{
    [ValueComparer(typeof(FieldDeclarationComparer))]
    public class FieldDeclaration
    {
        public string? Name { get; set; }
        public string? FullyQualifiedClassName { get; set; }
        public string? ClassName { get; set; }
        public string? Namespace { get; set; }
        public string? FullyQualifiedTypeName { get; set; }
        public string? Type { get; set; }
        public Attribute[] Attributes { get; set; } = new Attribute[0];
    }

    public class Attribute
    {
        public string? AttributeType { get; set; }
    }
}
