using System.Threading;
using System.Threading.Tasks;

namespace MintPlayer.CliGenerator.Attributes;

public interface ICliCommand
{
    Task<int> Execute(CancellationToken cancellationToken);
}
