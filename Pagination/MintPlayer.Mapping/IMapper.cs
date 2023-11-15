namespace MintPlayer.Mapping;

public interface IMapper<TSource, TTarget>
{
    Task<TTarget> Map(TSource source);
}
