using MintPlayer.SourceGenerators.Models;
using MintPlayer.SourceGenerators.Tools.ValueComparers;
using System;
using System.Collections.Generic;
using System.Text;

namespace MintPlayer.SourceGenerators.ValueComparers
{
    public class ClassDeclarationValueComparer : ValueComparer<ClassDeclaration>
    {
        protected override bool AreEqual(ClassDeclaration x, ClassDeclaration y)
        {
            if (!IsEquals(x.Name, y.Name)) return false;

            return true;
        }
    }
}
