using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace Spike.S3.Generator
{
    /// <summary>
    /// Classifies one <c>Pipeline.ExecuteAsync(Func&lt;CancellationToken, ValueTask&lt;T&gt;&gt;, CancellationToken)</c>
    /// call site and, when safe, produces the closure-free source rewrite
    /// <c>Pipeline.ExecuteAsync(static (__s, ct) =&gt; body', state, ct)</c>.
    /// </summary>
    internal static class SiteAnalyzer
    {
        private const string PipelineType = "Spike.S3.Pipeline";
        private const string NoInterceptAttribute = "Spike.S3.NoInterceptAttribute";

        // Tuple element names the compiler rejects (CS8125/CS8126).
        private static readonly HashSet<string> ReservedTupleNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Rest", "CompareTo", "Deconstruct", "Equals", "GetHashCode", "ToString", "GetType",
        };

        public static SiteModel? Analyze(GeneratorSyntaxContext ctx, CancellationToken ct)
        {
            var inv = (InvocationExpressionSyntax)ctx.Node;
            var model = ctx.SemanticModel;
            if (model.GetSymbolInfo(inv, ct).Symbol is not IMethodSymbol method) return null;
            var def = method.OriginalDefinition;
            if (def.Name != "ExecuteAsync" || def.ContainingType?.ToDisplayString() != PipelineType
                || def.Parameters.Length != 2 || def.TypeParameters.Length != 1)
                return null;

            var location = model.GetInterceptableLocation(inv, ct);
            if (location is null) return null;

            var site = DescribeSite(model, inv, ct, out var enclosingMethod);
            var display = location.GetDisplayLocation();

            SiteModel Make(string outcome, string detail, string rewrite = "") =>
                new SiteModel(site, display, outcome, detail, rewrite, location.Version, location.Data);

            if (enclosingMethod is not null && enclosingMethod.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == NoInterceptAttribute))
                return Make(Outcomes.Suppressed, "[NoIntercept] on the enclosing method (measurement control)");

            var argSyntax = inv.ArgumentList.Arguments[0].Expression;
            var argOp = model.GetOperation(argSyntax, ct);
            var anon = argOp switch
            {
                IAnonymousFunctionOperation a => a,
                IDelegateCreationOperation { Target: IAnonymousFunctionOperation a } => a,
                _ => null,
            };

            if (anon is null)
            {
                var target = argOp is IDelegateCreationOperation dc ? dc.Target : argOp;
                if (target is IMethodReferenceOperation mr)
                    return mr.Method.IsStatic
                        ? Make(Outcomes.NotNeeded, "static method group: the compiler caches the delegate (C# 11+), already 0 B")
                        : Make(Outcomes.Fallback, "instance method group: allocates a delegate bound to the receiver; lowerable in principle to (static (s, c) => s.M(c), receiver), not done in the spike");
                return Make(Outcomes.NotNeeded, "argument is an existing delegate value, nothing to lower");
            }

            if (argSyntax is AnonymousMethodExpressionSyntax)
                return Make(Outcomes.Fallback, "anonymous method (delegate { }) syntax: not handled");
            if (argSyntax is not LambdaExpressionSyntax lambda) return Make(Outcomes.Fallback, "unexpected syntax");
            if (lambda.Modifiers.Any(SyntaxKind.StaticKeyword))
                return Make(Outcomes.NotNeeded, "static lambda: cannot capture, already 0 B");

            // ---- 1. Find what the lambda captures from outside itself. ----
            var captured = new List<ISymbol>();       // locals + parameters, in first-use order
            var captureRefs = new List<IOperation>(); // references to them inside the lambda
            var thisRefs = new List<IInstanceReferenceOperation>();
            string? blocker = null;

            foreach (var op in anon.Body.Descendants())
            {
                if (IsInsideNameOf(op, anon)) continue;
                switch (op)
                {
                    case ILocalReferenceOperation lr when IsDeclaredOutside(lr.Local, lambda):
                        Add(lr.Local, op);
                        break;
                    case IParameterReferenceOperation pr when IsDeclaredOutside(pr.Parameter, lambda):
                        Add(pr.Parameter, op);
                        break;
                    case IInstanceReferenceOperation ir when ir.ReferenceKind == InstanceReferenceKind.ContainingTypeInstance:
                        if (ir.Syntax is BaseExpressionSyntax)
                            blocker ??= "uses base.Member: base (non-virtual) dispatch cannot be expressed through a state value";
                        else if (IsInsideNestedFunction(op, anon))
                            blocker ??= "`this` is used inside a nested lambda/local function: lowering would move the closure allocation into every invocation, no win";
                        else
                            thisRefs.Add(ir);
                        break;
                    case IMethodReferenceOperation { Method.MethodKind: MethodKind.LocalFunction } m when !m.Method.IsStatic && IsDeclaredOutside(m.Method, lambda):
                        blocker ??= $"refers to non-static local function '{m.Method.Name}' of the enclosing method";
                        break;
                    case IInvocationOperation { TargetMethod.MethodKind: MethodKind.LocalFunction } i when !i.TargetMethod.IsStatic && IsDeclaredOutside(i.TargetMethod, lambda):
                        blocker ??= $"calls non-static local function '{i.TargetMethod.Name}' of the enclosing method";
                        break;
                }
            }

            void Add(ISymbol symbol, IOperation op)
            {
                if (!captured.Contains(symbol, SymbolEqualityComparer.Default)) captured.Add(symbol);
                captureRefs.Add(op);
                if (IsInsideNestedFunction(op, anon))
                    blocker ??= $"captured '{symbol.Name}' is used inside a nested lambda/local function: lowering would allocate a closure per invocation instead of per call, no win";
            }

            if (captured.Count == 0 && thisRefs.Count == 0 && blocker is null)
                return Make(Outcomes.NotNeeded, "non-capturing lambda: the compiler caches the delegate, already 0 B");
            if (blocker is not null) return Make(Outcomes.Fallback, blocker);

            // ---- 2. Every captured variable must be effectively read-only. ----
            var member = EnclosingMemberRoots(inv);
            var notes = new List<string>();
            string? writeBlocker = null;
            foreach (var symbol in captured)
            {
                if (symbol is not ILocalSymbol && symbol is not IParameterSymbol)
                {
                    writeBlocker ??= $"captures a {symbol.Kind} ('{symbol.Name}'): not supported";
                    continue;
                }
                if (ReservedTupleNames.Contains(symbol.Name) || IsItemN(symbol.Name))
                {
                    writeBlocker ??= $"captured '{symbol.Name}' collides with a ValueTuple member name";
                    continue;
                }
                var (writtenInside, writtenOutside, sharedWithOtherClosure) = ScanUses(model, member, symbol, lambda, ct);
                if (writtenInside)
                    writeBlocker ??= $"captured '{symbol.Name}' is written inside the lambda (the closure shares the variable; a state copy would lose the write)";
                else if (writtenOutside)
                    writeBlocker ??= $"captured '{symbol.Name}' is written elsewhere in the method (a later write is visible to the closure, not to a state copy; earlier writes are rejected conservatively)";
                if (sharedWithOtherClosure)
                    notes.Add($"'{symbol.Name}' is also captured by another closure, so the scope still allocates its display class");
            }

            // ---- 3. Build the rewrite (also for write-blocked shapes, to show what a naive lowering would do). ----
            var rewrite = BuildRewrite(inv, lambda, anon, captured, captureRefs, thisRefs, model, ct);

            if (writeBlocker is not null)
                return Make(Outcomes.Fallback, writeBlocker, "NAIVE (wrong): " + rewrite);

            var detail = "captures " + string.Join(", ", captured.Select(s => s.Name).Concat(thisRefs.Count > 0 ? new[] { "this" } : Array.Empty<string>()));
            if (notes.Count > 0) detail += "; NOTE: " + string.Join("; ", notes);
            return Make(Outcomes.Lowered, detail, rewrite);
        }

        private static bool IsItemN(string name) =>
            name.StartsWith("Item", StringComparison.Ordinal) && name.Length > 4 && name.Substring(4).All(char.IsDigit);

        private static string DescribeSite(SemanticModel model, InvocationExpressionSyntax inv, CancellationToken ct, out IMethodSymbol? ordinary)
        {
            ordinary = null;
            for (var s = model.GetEnclosingSymbol(inv.SpanStart, ct); s is not null; s = s.ContainingSymbol)
            {
                if (s is IMethodSymbol { MethodKind: MethodKind.Ordinary or MethodKind.Constructor or MethodKind.PropertyGet } m)
                {
                    ordinary = m;
                    return m.ContainingType.Name + "." + m.Name;
                }
            }
            return "<unknown>";
        }

        private static bool IsDeclaredOutside(ISymbol symbol, SyntaxNode lambda) =>
            symbol.DeclaringSyntaxReferences.Length > 0
            && symbol.DeclaringSyntaxReferences.All(r => r.SyntaxTree != lambda.SyntaxTree || !lambda.Span.Contains(r.Span));

        private static bool IsInsideNameOf(IOperation op, IOperation stop)
        {
            for (var p = op.Parent; p is not null && p != stop; p = p.Parent)
                if (p is INameOfOperation) return true;
            return false;
        }

        private static bool IsInsideNestedFunction(IOperation op, IOperation stop)
        {
            for (var p = op.Parent; p is not null && p != stop; p = p.Parent)
                if (p is IAnonymousFunctionOperation or ILocalFunctionOperation) return true;
            return false;
        }

        /// <summary>The syntax nodes whose bodies can see the captured variable.</summary>
        private static IReadOnlyList<SyntaxNode> EnclosingMemberRoots(SyntaxNode node)
        {
            var member = node.Ancestors().OfType<MemberDeclarationSyntax>().FirstOrDefault();
            if (member is GlobalStatementSyntax && member.Parent is CompilationUnitSyntax cu)
                return cu.Members.OfType<GlobalStatementSyntax>().ToList();
            return member is null ? new[] { node.SyntaxTree.GetRoot() } : new SyntaxNode[] { member };
        }

        private static (bool writtenInside, bool writtenOutside, bool shared) ScanUses(
            SemanticModel model, IReadOnlyList<SyntaxNode> roots, ISymbol symbol, LambdaExpressionSyntax lambda, CancellationToken ct)
        {
            bool inside = false, outside = false, shared = false;
            foreach (var root in roots)
            foreach (var id in root.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
            {
                if (id.Identifier.ValueText != symbol.Name) continue;
                if (!SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(id, ct).Symbol, symbol)) continue;
                var inLambda = lambda.Span.Contains(id.Span);
                if (!inLambda && id.Ancestors().Any(a => a is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax
                                                      && !a.Span.Contains(lambda.Span)
                                                      && !IsStaticFunction(a)))
                    shared = true;
                var op = model.GetOperation(id, ct);
                if (op is null || !IsWrite(op)) continue;
                if (inLambda) inside = true; else outside = true;
            }
            return (inside, outside, shared);
        }

        private static bool IsStaticFunction(SyntaxNode n) => n switch
        {
            AnonymousFunctionExpressionSyntax a => a.Modifiers.Any(SyntaxKind.StaticKeyword),
            LocalFunctionStatementSyntax l => l.Modifiers.Any(SyntaxKind.StaticKeyword),
            _ => false,
        };

        /// <summary>
        /// Conservative "is this reference a write (or an alias that could be written through)?".
        /// Covers assignment, compound/coalesce assignment, ++/--, ref/out arguments, deconstruction
        /// targets, address-of, ref locals / ref reassignment, and mutation of a mutable struct through
        /// a field write or a non-readonly member call.
        /// </summary>
        private static bool IsWrite(IOperation op)
        {
            if (op is ILocalReferenceOperation { IsDeclaration: true }) return false; // `out var x` declares
            var parent = op.Parent;
            switch (parent)
            {
                case ISimpleAssignmentOperation a when a.Target == op: return true;
                case ISimpleAssignmentOperation { IsRef: true } a when a.Value == op: return true; // r = ref x
                case ICompoundAssignmentOperation a when a.Target == op: return true;
                case ICoalesceAssignmentOperation a when a.Target == op: return true;
                case IIncrementOrDecrementOperation: return true;
                case IArgumentOperation arg when arg.Parameter is { RefKind: RefKind.Ref or RefKind.Out }: return true;
                case IAddressOfOperation: return true;
                case ITupleOperation t: return IsWrite(t);
                case IDeconstructionAssignmentOperation d when d.Target == op: return true;
                case IVariableInitializerOperation { Parent: IVariableDeclaratorOperation decl } when decl.Symbol.RefKind != RefKind.None: return true;
                case IReturnOperation: return op.Syntax.Parent is RefExpressionSyntax;
                case IFieldReferenceOperation f when f.Instance == op && op.Type?.IsValueType == true: return IsWrite(f);
                case IPropertyReferenceOperation p when p.Instance == op && op.Type is { IsValueType: true, IsReadOnly: false }:
                    return IsWrite(p) || p.Property.GetMethod is { IsReadOnly: false };
                case IInvocationOperation i when i.Instance == op && op.Type is { IsValueType: true, IsReadOnly: false }:
                    return !i.TargetMethod.IsReadOnly;
            }
            return op.Syntax.Parent is RefExpressionSyntax; // any other `ref x` alias
        }

        private static string BuildRewrite(
            InvocationExpressionSyntax inv, LambdaExpressionSyntax lambda, IAnonymousFunctionOperation anon,
            List<ISymbol> captured, List<IOperation> captureRefs, List<IInstanceReferenceOperation> thisRefs,
            SemanticModel model, CancellationToken ct)
        {
            var body = lambda.Body;
            var bodyText = body.ToString();
            var stateName = "__s";
            while (bodyText.Contains(stateName)) stateName += "_";

            var hasThis = thisRefs.Count > 0;
            var selfName = "self";
            while (captured.Any(s => s.Name == selfName)) selfName += "_";
            var single = captured.Count + (hasThis ? 1 : 0) == 1;

            string Access(string name) => single ? stateName : stateName + "." + name;

            var edits = new List<(TextSpan span, string text)>();
            foreach (var r in captureRefs)
            {
                if (r.Syntax is not IdentifierNameSyntax id) continue;
                var name = id.Identifier.ValueText;
                var access = Access(name);
                // Keep inferred member names (anonymous-type projections, tuple literals) stable.
                if (id.Parent is AnonymousObjectMemberDeclaratorSyntax { NameEquals: null })
                    access = name + " = " + access;
                else if (id.Parent is ArgumentSyntax { NameColon: null, Parent: TupleExpressionSyntax })
                    access = name + ": " + access;
                edits.Add((id.Span, access));
            }
            foreach (var t in thisRefs)
            {
                if (t.IsImplicit)
                    edits.Add((new TextSpan(t.Syntax.SpanStart, 0), Access(selfName) + "."));
                else
                    edits.Add((t.Syntax.Span, Access(selfName)));
            }

            var sb = new StringBuilder(bodyText);
            foreach (var e in edits.GroupBy(e => e.span).Select(g => g.First()).OrderByDescending(e => e.span.Start))
                sb.Remove(e.span.Start - body.SpanStart, e.span.Length).Insert(e.span.Start - body.SpanStart, e.text);

            // State expression: the value itself for one capture, a named tuple otherwise.
            var parts = captured.Select(s => (name: s.Name, expr: s.Name)).ToList();
            if (hasThis) parts.Add((selfName, "this"));
            var stateExpr = single ? parts[0].expr : "(" + string.Join(", ", parts.Select(p => p.name + ": " + p.expr)) + ")";

            // Parameter list: keep the cancellation-token parameter's name (and type, if it was explicit).
            string ctParam;
            string stateParam = stateName;
            switch (lambda)
            {
                case SimpleLambdaExpressionSyntax s:
                    ctParam = s.Parameter.ToString();
                    break;
                case ParenthesizedLambdaExpressionSyntax p:
                    ctParam = p.ParameterList.Parameters[0].ToString();
                    if (p.ParameterList.Parameters[0].Type is not null)
                    {
                        var stateType = single
                            ? TypeOf(captured.Count == 1 ? captured[0] : null, model)
                            : "(" + string.Join(", ", captured.Select(s => TypeOf(s, model) + " " + s.Name)
                                .Concat(hasThis ? new[] { anon.Symbol.ContainingType.ToDisplayString() + " " + selfName } : Array.Empty<string>())) + ")";
                        if (single && hasThis) stateType = anon.Symbol.ContainingType.ToDisplayString();
                        stateParam = stateType + " " + stateName;
                    }
                    break;
                default:
                    ctParam = "ct";
                    break;
            }

            var prefix = new StringBuilder();
            foreach (var attr in lambda.AttributeLists) prefix.Append(attr).Append(' ');
            prefix.Append("static ");
            if (lambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword)) prefix.Append("async ");
            if (lambda is ParenthesizedLambdaExpressionSyntax { ReturnType: { } rt }) prefix.Append(rt).Append(' ');

            var newLambda = $"{prefix}({stateParam}, {ctParam}) => {sb}";
            var ctArg = inv.ArgumentList.Arguments[1].ToString();
            return $"{inv.Expression}({newLambda}, {stateExpr}, {ctArg})";
        }

        private static string TypeOf(ISymbol? s, SemanticModel model) => s switch
        {
            ILocalSymbol l => l.Type.ToDisplayString(),
            IParameterSymbol p => p.Type.ToDisplayString(),
            _ => "object",
        };
    }
}
