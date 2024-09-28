using MintPlayer.SourceGenerators.Models;
using MintPlayer.SourceGenerators.Tools.ValueComparers;
using System;
using System.Collections.Generic;
using System.Text;

namespace MintPlayer.SourceGenerators.ValueComparers
{
    public class ClassInformationComparer : ValueComparer<ClassInformation>
    {
        protected override bool AreEqual(ClassInformation x, ClassInformation y)
        {
            if (!IsEquals(x.FullyQualifiedName, y.FullyQualifiedName)) return false;
            if (!IsEquals(x.Name, y.Name)) return false;
            if (!IsEquals(x.BaseType, y.BaseType)) return false;

            return true;
        }
    }
}
