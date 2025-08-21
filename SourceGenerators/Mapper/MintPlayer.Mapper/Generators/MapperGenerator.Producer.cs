using MintPlayer.Mapper.Models;
using MintPlayer.SourceGenerators.Tools;
using System.CodeDom.Compiler;

namespace MintPlayer.Mapper.Generators;

public sealed class MapperProducer : Producer
{
    private readonly IEnumerable<TypeToMap> typesToMap;
    public MapperProducer(IEnumerable<TypeToMap> typesToMap, string rootNamespace) : base(rootNamespace, "Mappers.g.cs")
    {
        this.typesToMap = typesToMap;
    }

    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
