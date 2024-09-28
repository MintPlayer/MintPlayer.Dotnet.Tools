using MintPlayer.SourceGenerators.Models;
using MintPlayer.SourceGenerators.Tools.ValueComparers;
using System;
using System.Collections.Generic;
using System.Text;

namespace MintPlayer.SourceGenerators.ValueComparers
{
    public class FieldDeclarationComparer : ValueComparer<FieldDeclaration>
    {
        protected override bool AreEqual(FieldDeclaration x, FieldDeclaration y)
        {
            if (!IsEquals(x.FieldType?.FullyQualifiedName, y.FieldType?.FullyQualifiedName)) return false;
            if (!IsEquals(x.Class?.FullyQualifiedName, y.Class?.FullyQualifiedName)) return false;
            if (!IsEquals(x.FieldType, y.FieldType)) return false;
            if (!IsEquals(x.FieldName, y.FieldName)) return false;
            if (!IsEquals(x.Class?.Name, y.Class?.Name)) return false;
            if (!IsEquals(x.Namespace, y.Namespace)) return false;

            return true;
        }
    }
}
