namespace MintPlayer.SourceGenerators.Debug;

#region Test Case 1: Simple double-quote
/// <summary>
/// Returns "hello" as a string.
/// </summary>
public partial class QuoteTest1 { }
#endregion

#region Test Case 2: XML entity quote (&quot;)
/// <summary>
/// Returns &quot;hello&quot; as a string.
/// </summary>
public partial class QuoteTest2 { }
#endregion

#region Test Case 3: Pre-escaped quote (\")
/// <summary>
/// The pattern is \"quoted\".
/// </summary>
public partial class QuoteTest3 { }
#endregion

#region Test Case 4: Double-escaped quote (\\")
/// <summary>
/// Regex pattern: \\"
/// </summary>
public partial class QuoteTest4 { }
#endregion

#region Test Case 5: Backslash without quote
/// <summary>
/// File path: C:\Users\Test
/// </summary>
public partial class QuoteTest5 { }
#endregion

#region Test Case 6: Mixed quotes and backslashes
/// <summary>
/// Path "C:\Program Files\App" is valid.
/// </summary>
public partial class QuoteTest6 { }
#endregion

#region Test Case 7: Multiple quotes in one line
/// <summary>
/// Use "foo" or "bar" or "baz".
/// </summary>
public partial class QuoteTest7 { }
#endregion

#region Test Case 8: Quote at string boundaries
/// <summary>
/// "Starts with quote and ends with quote"
/// </summary>
public partial class QuoteTest8 { }
#endregion

#region Test Case 9: Empty quotes
/// <summary>
/// Empty string is "".
/// </summary>
public partial class QuoteTest9 { }
#endregion

#region Test Case 10: Single backslash at end
/// <summary>
/// Ends with backslash\
/// </summary>
public partial class QuoteTest10 { }
#endregion
