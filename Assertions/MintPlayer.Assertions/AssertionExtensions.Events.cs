using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using MintPlayer.Assertions.Events;

namespace MintPlayer.Assertions;

public static partial class AssertionExtensions
{
    /// <summary>
    /// Starts monitoring all supported public events of <paramref name="subject"/>; dispose the
    /// returned monitor to unsubscribe. See <see cref="EventMonitor{T}"/> for the supported event
    /// shapes and the assertion surface (<c>Raise</c>, <c>NotRaise</c>, <c>RaisePropertyChangeFor</c>, …).
    /// </summary>
    [RequiresDynamicCode(EventMonitor<object>.DynamicCodeMessage)]
    public static EventMonitor<T> Monitor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicEvents)] T>(this T subject,
        [CallerArgumentExpression(nameof(subject))] string? subjectExpression = null)
        where T : class
        => new(subject, subjectExpression);
}
