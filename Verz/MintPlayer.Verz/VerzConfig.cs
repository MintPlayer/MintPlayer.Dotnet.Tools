namespace MintPlayer.Verz
{
    public class VerzConfig
    {
        public VerzFeed[] Feeds { get; set; } = [];
        public VerzSdk[] Sdks { get; set; } = [];
    }

    public class VerzFeed
    {
        public string? Url { get; set; }
    }

    public class VerzSdk
    {

    }
}
