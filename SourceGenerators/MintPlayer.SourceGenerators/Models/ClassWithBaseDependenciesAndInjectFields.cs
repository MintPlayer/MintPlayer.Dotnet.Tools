using MintPlayer.SourceGenerators.Tools;
using System.Collections.Generic;

namespace MintPlayer.SourceGenerators.Models
{
    [ValueComparer(typeof(ValueComparers.ClassWithBaseDependenciesAndInjectFieldsComparer))]
    public class ClassWithBaseDependenciesAndInjectFields
    {
        public string FileName { get; set; } = string.Empty;
        public string ClassName { get; set; } = string.Empty;
        public string? ClassNamespace { get; set; } = string.Empty;
        public IList<InjectField> BaseDependencies { get; set; } = [];
        public IList<InjectField> InjectFields { get; set; } = [];
    }
}
