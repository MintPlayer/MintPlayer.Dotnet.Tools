namespace MintPlayer.Mapping;

public interface IMapper<TSource, TTarget>
{
    Task<TTarget> Map(TSource source);
}

//public interface ITwinMapper<TSource, TTarget> : IMapper<TTarget, TSource>
//{
//    Task<TSource> Map(TTarget source);
//}