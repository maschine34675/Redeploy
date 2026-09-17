using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Redeploy.Tests;

/// <summary>
/// Syntax-only checks over Core's source (docs/RESPAWN-API.md, section 8.4). Roslyn parses the
/// files without references, so nothing here needs the game. Every check takes source text,
/// which lets the tests feed it the faults it exists to catch.
/// </summary>
internal static class SourceGuards
{
    /// <summary>
    /// Measurement inside RunRespawn goes through one local of this type. Its statements are left
    /// out of the golden order as long as they are inert (see <see cref="IsTimingStatement"/>), so
    /// the guard proves that adding or moving a timer did not move anything else.
    /// </summary>
    public const string TimingType = "RespawnPipelineTiming";
    public const string TimingLocal = "timing";

    private static readonly string[] RaiseMembers = { "Invoke", "BeginInvoke", "DynamicInvoke", "GetInvocationList" };

    // ------------------------------------------------------------------ golden order

    /// <summary>
    /// RespawnEngine.RunRespawn as one line per statement, nesting shown by indentation, comments
    /// and layout dropped. Control statements contribute their header; their bodies follow indented.
    /// </summary>
    public static List<string> RunRespawnOutline(string engineSource)
    {
        var root = Parse(engineSource).GetCompilationUnitRoot();
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(m => m.Identifier.ValueText == "RunRespawn"
                        && m.Parent is ClassDeclarationSyntax owner
                        && owner.Identifier.ValueText == "RespawnEngine")
            .ToList();
        if (methods.Count != 1 || methods[0].Body == null)
            throw new InvalidOperationException($"expected exactly one RespawnEngine.RunRespawn with a block body, found {methods.Count}");

        var lines = new List<string>();
        foreach (var statement in methods[0].Body.Statements)
            Walk(statement, 0, lines);
        return lines;
    }

    private static void Walk(StatementSyntax statement, int depth, List<string> lines)
    {
        switch (statement)
        {
            case BlockSyntax block:
                // Braces are layout; the owning statement already contributed the nesting.
                foreach (var inner in block.Statements)
                    Walk(inner, depth, lines);
                return;

            case IfStatementSyntax conditional:
                Add(lines, depth, "if (" + Text(conditional.Condition) + ")");
                Walk(conditional.Statement, depth + 1, lines);
                if (conditional.Else != null)
                {
                    Add(lines, depth, "else");
                    Walk(conditional.Else.Statement, depth + 1, lines);
                }
                return;

            case WhileStatementSyntax loop:
                Add(lines, depth, "while (" + Text(loop.Condition) + ")");
                Walk(loop.Statement, depth + 1, lines);
                return;

            case DoStatementSyntax loop:
                Add(lines, depth, "do");
                Walk(loop.Statement, depth + 1, lines);
                Add(lines, depth, "while (" + Text(loop.Condition) + ")");
                return;

            case ForStatementSyntax loop:
                Add(lines, depth, Text(loop.WithStatement(SyntaxFactory.Block())));
                Walk(loop.Statement, depth + 1, lines);
                return;

            case ForEachStatementSyntax loop:
                Add(lines, depth, Text(loop.WithStatement(SyntaxFactory.Block())));
                Walk(loop.Statement, depth + 1, lines);
                return;

            case UsingStatementSyntax scope:
                Add(lines, depth, Text(scope.WithStatement(SyntaxFactory.Block())));
                Walk(scope.Statement, depth + 1, lines);
                return;

            case LockStatementSyntax scope:
                Add(lines, depth, Text(scope.WithStatement(SyntaxFactory.Block())));
                Walk(scope.Statement, depth + 1, lines);
                return;

            case TryStatementSyntax attempt:
                Add(lines, depth, "try");
                Walk(attempt.Block, depth + 1, lines);
                foreach (var handler in attempt.Catches)
                {
                    var header = "catch";
                    if (handler.Declaration != null)
                        header += " " + Text(handler.Declaration);           // the declaration carries its own parentheses
                    if (handler.Filter != null)
                        header += " when (" + Text(handler.Filter.FilterExpression) + ")";
                    Add(lines, depth, header);
                    Walk(handler.Block, depth + 1, lines);
                }
                if (attempt.Finally != null)
                {
                    Add(lines, depth, "finally");
                    Walk(attempt.Finally.Block, depth + 1, lines);
                }
                return;

            default:
                if (IsTimingStatement(statement))
                    return;
                Add(lines, depth, Text(statement));
                return;
        }
    }

    /// <summary>
    /// <c>var timing = RespawnPipelineTiming.X(...);</c> or <c>timing.X(...);</c>, where every argument
    /// is a literal, a name or a member access. An argument that calls, assigns or builds a lambda
    /// could hide real work, so such a statement stays in the order.
    /// </summary>
    public static bool IsTimingStatement(StatementSyntax statement)
    {
        if (statement is LocalDeclarationStatementSyntax local)
        {
            return local.Declaration.Variables.Count == 1
                   && local.Declaration.Variables[0].Identifier.ValueText == TimingLocal
                   && local.Declaration.Variables[0].Initializer?.Value is InvocationExpressionSyntax start
                   && start.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax type }
                   && type.Identifier.ValueText == TimingType
                   && AreInert(start.ArgumentList);
        }

        return statement is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax call }
               && call.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax target }
               && target.Identifier.ValueText == TimingLocal
               && AreInert(call.ArgumentList);
    }

    private static bool AreInert(ArgumentListSyntax arguments)
        => arguments.Arguments.All(argument =>
            argument.RefKindKeyword.IsKind(SyntaxKind.None)
            && argument.Expression.DescendantNodesAndSelf().All(node =>
                node is LiteralExpressionSyntax
                || node is IdentifierNameSyntax
                || node is MemberAccessExpressionSyntax
                || node is PredefinedTypeSyntax));

    private static void Add(List<string> lines, int depth, string text)
        => lines.Add(new string(' ', depth * 2) + text);

    // ------------------------------------------------------------------ events

    /// <summary>
    /// Every place that raises one of Core's public events itself instead of handing it to the
    /// SignalDispatcher: <c>E(...)</c>, <c>E.Invoke(...)</c>, <c>E?.Invoke(...)</c>, <c>T.E.Invoke(...)</c>,
    /// and a manual walk over <c>E.GetInvocationList()</c>. C# only lets the declaring type (and types
    /// nested in it) touch an event's delegate, so a candidate counts only there, with no receiver,
    /// <c>this</c> or the declaring type's name as receiver. That keeps a driver method that merely shares
    /// an event's name (<c>driver.OnRaidEnded()</c>) out.
    /// </summary>
    public static List<string> DirectEventRaises(IEnumerable<(string Path, string Source)> files)
    {
        var trees = files.Select(file => (file.Path, Root: Parse(file.Source, file.Path).GetCompilationUnitRoot())).ToList();

        // event name -> names of the types declaring an event of that name
        var events = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        void Declare(string name, SyntaxNode declaration)
        {
            if (declaration.Parent is not TypeDeclarationSyntax owner)
                return;
            if (!events.TryGetValue(name, out var owners))
                events[name] = owners = new HashSet<string>(StringComparer.Ordinal);
            owners.Add(owner.Identifier.ValueText);
        }
        foreach (var (_, root) in trees)
        {
            foreach (var field in root.DescendantNodes().OfType<EventFieldDeclarationSyntax>())
                if (field.Modifiers.Any(SyntaxKind.PublicKeyword))
                    foreach (var variable in field.Declaration.Variables)
                        Declare(variable.Identifier.ValueText, field);
            foreach (var property in root.DescendantNodes().OfType<EventDeclarationSyntax>())
                if (property.Modifiers.Any(SyntaxKind.PublicKeyword))
                    Declare(property.Identifier.ValueText, property);
        }

        var violations = new List<string>();
        foreach (var (path, root) in trees)
        {
            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var (name, qualifier) = RaisedEvent(invocation);
                if (name == null || !events.TryGetValue(name, out var owners))
                    continue;

                var enclosing = invocation.Ancestors().OfType<TypeDeclarationSyntax>().Select(type => type.Identifier.ValueText);
                if (!enclosing.Any(owners.Contains))
                    continue;
                if (qualifier != null && qualifier != "this" && !owners.Contains(qualifier))
                    continue;

                var line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                violations.Add($"{System.IO.Path.GetFileName(path)}:{line}: {Text(invocation)}");
            }
        }
        return violations;
    }

    /// <summary>(event candidate, receiver name or null) for a call that would raise a delegate by that name.</summary>
    private static (string Name, string Qualifier) RaisedEvent(InvocationExpressionSyntax invocation)
    {
        switch (invocation.Expression)
        {
            case IdentifierNameSyntax name:                                    // E(...)
                return (name.Identifier.ValueText, null);

            case MemberAccessExpressionSyntax access when RaiseMembers.Contains(access.Name.Identifier.ValueText):
                return Receiver(access.Expression);                            // E.Invoke(...), T.E.Invoke(...)

            case MemberAccessExpressionSyntax access:                          // T.E(...), this.E(...)
                return (access.Name.Identifier.ValueText, QualifierName(access.Expression));

            case MemberBindingExpressionSyntax binding when RaiseMembers.Contains(binding.Name.Identifier.ValueText):
                // E?.Invoke(...): the receiver is the conditional access this call is the tail of.
                var conditional = invocation.Ancestors().OfType<ConditionalAccessExpressionSyntax>()
                    .FirstOrDefault(candidate => candidate.WhenNotNull.Span.Contains(invocation.Span));
                return conditional == null ? (null, null) : Receiver(conditional.Expression);

            default:
                return (null, null);
        }
    }

    private static (string Name, string Qualifier) Receiver(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax name => (name.Identifier.ValueText, null),
        MemberAccessExpressionSyntax access => (access.Name.Identifier.ValueText, QualifierName(access.Expression)),
        _ => (null, null),
    };

    /// <summary>"this", a simple or dotted type name's last part, or a placeholder that matches no type.</summary>
    private static string QualifierName(ExpressionSyntax expression) => expression switch
    {
        ThisExpressionSyntax => "this",
        IdentifierNameSyntax name => name.Identifier.ValueText,
        MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
        _ => "<expression>",
    };

    private static string LastName(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax name => name.Identifier.ValueText,
        MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
        _ => null,
    };

    // ------------------------------------------------------------------ Freecam

    /// <summary>
    /// Calls of <c>FreecamCompatHelper.EnablePatchesIfPresent</c> and whether each sits directly in the
    /// block of a try with a catch-all. "Directly": no lambda or local function between the try and the
    /// call, because those run later, outside the try.
    /// </summary>
    public static (int Guarded, List<string> Unguarded) FreecamEnableSites(IEnumerable<(string Path, string Source)> files)
    {
        var guarded = 0;
        var unguarded = new List<string>();
        foreach (var (path, source) in files)
        {
            var root = Parse(source, path).GetCompilationUnitRoot();
            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Expression is not MemberAccessExpressionSyntax access
                    || access.Name.Identifier.ValueText != "EnablePatchesIfPresent"
                    || LastName(access.Expression) != "FreecamCompatHelper")
                    continue;

                if (IsInsideCatchAllTry(invocation))
                {
                    guarded++;
                }
                else
                {
                    var line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    unguarded.Add($"{System.IO.Path.GetFileName(path)}:{line}");
                }
            }
        }
        return (guarded, unguarded);
    }

    private static bool IsInsideCatchAllTry(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            switch (ancestor)
            {
                case TryStatementSyntax attempt when attempt.Block.Span.Contains(node.Span):
                    if (attempt.Catches.Any(IsCatchAll))
                        return true;
                    break;                                                       // try/finally only: keep looking outward
                case AnonymousFunctionExpressionSyntax:
                case LocalFunctionStatementSyntax:
                case MemberDeclarationSyntax:
                    return false;
            }
        }
        return false;
    }

    private static bool IsCatchAll(CatchClauseSyntax handler)
    {
        if (handler.Filter != null)
            return false;
        if (handler.Declaration == null)
            return true;
        var type = handler.Declaration.Type.ToString();
        return type == "Exception" || type == "System.Exception";
    }

    // ------------------------------------------------------------------ shared

    private static SyntaxTree Parse(string source, string path = "")
        => CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview), path);

    private static string Text(SyntaxNode node)
    {
        var withoutComments = new CommentStripper().Visit(node);
        return Regex.Replace(withoutComments.NormalizeWhitespace().ToFullString(), @"\s+", " ").Trim();
    }

    private sealed class CommentStripper : CSharpSyntaxRewriter
    {
        public override SyntaxTrivia VisitTrivia(SyntaxTrivia trivia)
            => trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
               || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
               || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
               || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia)
                ? default
                : trivia;
    }
}
