using Microsoft.CodeAnalysis;
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
        public string? FieldName { get; set; }
        public ClassInformation? Class { get; set; }
        public string? Namespace { get; set; }
        public TypeInformation? FieldType { get; set; }
        public bool IsReadonly { get; set; }
    }

    [ValueComparer(typeof(ClassInformationComparer))]
    public class ClassInformation
    {
        public string? Name { get; set; }
        public string? FullyQualifiedName { get; set; }
        public TypeInformation? BaseType { get; set; }
    }

    public class TypeInformation
    {
        public string? Name { get; set; }
        public string? FullyQualifiedName { get; set; }
        public ConstructorInformation[] Constructors { get; set; } = new ConstructorInformation[0];
    }

    public class ConstructorInformation
    {
        public ParameterInformation[] Parameters { get; set; } = new ParameterInformation[0];
    }

    public class ParameterInformation
    {
        public string? Name { get; set; }
        public TypeInformation? Type { get; set; }
    }
}
