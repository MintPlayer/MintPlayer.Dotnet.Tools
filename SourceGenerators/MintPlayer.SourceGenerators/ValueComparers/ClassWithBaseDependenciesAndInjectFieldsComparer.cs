using MintPlayer.SourceGenerators.Models;
using MintPlayer.SourceGenerators.Tools.ValueComparers;

namespace MintPlayer.SourceGenerators.ValueComparers;

public class ClassWithBaseDependenciesAndInjectFieldsComparer : ValueComparer<ClassWithBaseDependenciesAndInjectFields>
{
    protected override bool AreEqual(ClassWithBaseDependenciesAndInjectFields x, ClassWithBaseDependenciesAndInjectFields y)
    {
        if (!IsEquals(x.FileName, y.FileName)) return false;
        if (!IsEquals(x.ClassName, y.ClassName)) return false;
        //if (x.InjectFields is null) return y.InjectFields is null;
        //if (y.InjectFields is null) return false;
        //if (x.BaseDependencies is null) return y.BaseDependencies is null;
        //if (y.BaseDependencies is null) return false;

        //if (!IsEquals(x.InjectFields?.Count, y.InjectFields?.Count)) return false;
        //if (!IsEquals(x.BaseDependencies?.Count, y.BaseDependencies?.Count)) return false;

        //for (var i = 0; i < x.InjectFields?.Count; i++)
        //    if (!IsEquals(x.InjectFields[i], y.InjectFields![i])) return false;

        //for (var i = 0; i < x.BaseDependencies?.Count; i++)
        //    if (!IsEquals(x.BaseDependencies[i], y.BaseDependencies![i])) return false;

        if (!IsEquals(x.InjectFields, y.InjectFields)) return false;
        if (!IsEquals(x.BaseDependencies, y.BaseDependencies)) return false;

        return true;
    }
}
