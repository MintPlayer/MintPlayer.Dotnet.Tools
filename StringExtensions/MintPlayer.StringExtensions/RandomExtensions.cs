namespace MintPlayer.StringExtensions;

public static class RandomExtensions
{
    public static Task<string> RandomString(this Random random, int length = 20)
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        return Task.FromResult(new string(Enumerable.Repeat(chars, length)
          .Select(s => s[random.Next(s.Length)]).ToArray()));
    }
}
