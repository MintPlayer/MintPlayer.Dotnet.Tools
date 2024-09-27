using System;
using System.Collections.Generic;
using System.Text;

namespace MintPlayer.SourceGenerators.Models
{
    public class FieldDeclaration
    {
        public string? Name { get; set; }
        public string? FullyQualifiedClassName { get; set; }
        public string? ClassName { get; set; }
        public string? Namespace { get; set; }
        public string? FullyQualifiedTypeName { get; set; }
        public string? Type { get; set; }
    }
}
