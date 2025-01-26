using MintPlayer.SourceGenerators.Models;
using MintPlayer.SourceGenerators.Tools.ValueComparers;

namespace MintPlayer.SourceGenerators.ValueComparers;

public class ServiceRegistrationComparer : ValueComparer<ServiceRegistration>
{
    protected override bool AreEqual(ServiceRegistration x, ServiceRegistration y)
    {
        if (!IsEquals(x.Lifetime, y.Lifetime)) return false;
        if (!IsEquals(x.ServiceTypeName, y.ServiceTypeName)) return false;
        if (!IsEquals(x.ImplementationTypeName, y.ImplementationTypeName)) return false;

        return true;
    }
}
