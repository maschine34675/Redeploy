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

        var method = methods[0];
        var locals = LocalNames(method);
        var lines = new List<string>();
        foreach (var statement in method.Body.Statements)
            Walk(statement, 0, lines, locals);
        return lines;
    }

    private static void Walk(StatementSyntax statement, int depth, List<string> lines, ISet<string> locals)
    {
        switch (statement)
        {
            case BlockSyntax block:
                // Braces are layout; the owning statement already contributed the nesting.
                foreach (var inner in block.Statements)
                    Walk(inner, depth, lines, locals);
                return;

            case IfStatementSyntax conditional:
                Add(lines, depth, "if (" + Text(conditional.Condition) + ")");
                Walk(conditional.Statement, depth + 1, lines, locals);
                if (conditional.Else != null)
                {
                    Add(lines, depth, "else");
                    Walk(conditional.Else.Statement, depth + 1, lines, locals);
                }
                return;

            case WhileStatementSyntax loop:
                Add(lines, depth, "while (" + Text(loop.Condition) + ")");
                Walk(loop.Statement, depth + 1, lines, locals);
                return;

            case DoStatementSyntax loop:
                Add(lines, depth, "do");
                Walk(loop.Statement, depth + 1, lines, locals);
                Add(lines, depth, "while (" + Text(loop.Condition) + ")");
                return;

            case ForStatementSyntax loop:
                Add(lines, depth, Text(loop.WithStatement(SyntaxFactory.Block())));
                Walk(loop.Statement, depth + 1, lines, locals);
                return;

            case ForEachStatementSyntax loop:
                Add(lines, depth, Text(loop.WithStatement(SyntaxFactory.Block())));
                Walk(loop.Statement, depth + 1, lines, locals);
                return;

            case UsingStatementSyntax scope:
                Add(lines, depth, Text(scope.WithStatement(SyntaxFactory.Block())));
                Walk(scope.Statement, depth + 1, lines, locals);
                return;

            case LockStatementSyntax scope:
                Add(lines, depth, Text(scope.WithStatement(SyntaxFactory.Block())));
                Walk(scope.Statement, depth + 1, lines, locals);
                return;

            case TryStatementSyntax attempt:
                Add(lines, depth, "try");
                Walk(attempt.Block, depth + 1, lines, locals);
                foreach (var handler in attempt.Catches)
                {
                    var header = "catch";
                    if (handler.Declaration != null)
                        header += " " + Text(handler.Declaration);           // the declaration carries its own parentheses
                    if (handler.Filter != null)
                        header += " when (" + Text(handler.Filter.FilterExpression) + ")";
                    Add(lines, depth, header);
                    Walk(handler.Block, depth + 1, lines, locals);
                }
                if (attempt.Finally != null)
                {
                    Add(lines, depth, "finally");
                    Walk(attempt.Finally.Block, depth + 1, lines, locals);
                }
                return;

            default:
                // Any other statement, including ones with nested bodies (switch, labeled, checked),
                // is kept whole: a change anywhere inside it still changes its line.
                if (IsTimingStatement(statement, locals))
                    return;
                Add(lines, depth, Text(statement));
                return;
        }
    }

    /// <summary>
    /// <c>var timing = RespawnPipelineTiming.X(...);</c> or <c>timing.X(...);</c>, where every argument is
    /// a literal or a plain name of one of RunRespawn's own parameters or locals. Anything else could
    /// run code (a call, an assignment, a lambda, even a property getter behind a member access), so
    /// such a statement stays in the order.
    /// </summary>
    public static bool IsTimingStatement(StatementSyntax statement, ISet<string> locals)
    {
        if (statement is LocalDeclarationStatementSyntax local)
        {
            return local.Declaration.Variables.Count == 1
                   && local.Declaration.Variables[0].Identifier.ValueText == TimingLocal
                   && local.Declaration.Variables[0].Initializer?.Value is InvocationExpressionSyntax start
                   && start.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax type }
                   && type.Identifier.ValueText == TimingType
                   && AreInert(start.ArgumentList, locals);
        }

        return statement is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax call }
               && call.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax target }
               && target.Identifier.ValueText == TimingLocal
               && AreInert(call.ArgumentList, locals);
    }

    private static bool AreInert(ArgumentListSyntax arguments, ISet<string> locals)
        => arguments.Arguments.All(argument =>
            argument.RefKindKeyword.IsKind(SyntaxKind.None)
            && argument.NameColon == null
            && (argument.Expression is LiteralExpressionSyntax
                || argument.Expression is IdentifierNameSyntax name && locals.Contains(name.Identifier.ValueText)));

    /// <summary>Parameter and local names declared anywhere in the method (never "timing" itself).</summary>
    private static ISet<string> LocalNames(MethodDeclarationSyntax method)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in method.ParameterList.Parameters)
            names.Add(parameter.Identifier.ValueText);
        foreach (var node in method.Body.DescendantNodes())
        {
            switch (node)
            {
                case VariableDeclaratorSyntax variable:
                    names.Add(variable.Identifier.ValueText);
                    break;
                case SingleVariableDesignationSyntax designation:
                    names.Add(designation.Identifier.ValueText);
                    break;
                case CatchDeclarationSyntax { Identifier.ValueText.Length: > 0 } caught:
                    names.Add(caught.Identifier.ValueText);
                    break;
                case ForEachStatementSyntax loop:
                    names.Add(loop.Identifier.ValueText);
                    break;
            }
        }
        names.Remove(TimingLocal);
        return names;
    }

    private static void Add(List<string> lines, int depth, string text)
        => lines.Add(new string(' ', depth * 2) + text);

    // ------------------------------------------------------------------ events

    /// <summary>
    /// Every read of a public Core event's delegate that does not hand it to the SignalDispatcher.
    /// Allowed are only <c>CoreSignals.&lt;Signal&gt;.Raise(E, ...)</c> with the event as first argument,
    /// <c>nameof(E)</c>, and <c>E</c> as the left side of an assignment (<c>+=</c>, <c>-=</c>, <c>= null</c>).
    /// Everything else is a way around the dispatcher: <c>E(...)</c>, <c>E?.Invoke(...)</c>,
    /// <c>E.GetInvocationList()</c>, <c>var h = E; h(...)</c>, <c>Volatile.Read(ref E)</c>, passing <c>E</c>
    /// to a helper. C# only lets the declaring type (and types nested in it) read an event's delegate,
    /// so only uses there count, and only with no receiver, <c>this</c> or the declaring type's name as
    /// receiver; <c>driver.OnRaidEnded()</c> merely shares a name.
    /// </summary>
    public static List<string> EventDelegateUsesOutsideDispatcher(IEnumerable<(string Path, string Source)> files)
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
            foreach (var name in root.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (!events.TryGetValue(name.Identifier.ValueText, out var owners))
                    continue;

                ExpressionSyntax reference = name;
                switch (name.Parent)
                {
                    case MemberAccessExpressionSyntax access when access.Name == name:
                        var qualifier = QualifierName(access.Expression);
                        if (qualifier != "this" && !owners.Contains(qualifier))
                            continue;                                          // another object's member of that name
                        reference = access;
                        break;
                    case MemberBindingExpressionSyntax:                        // x?.E: another object's member
                    case QualifiedNameSyntax:                                  // part of a namespace or type name
                    case NameColonSyntax:                                      // E: named argument
                        continue;
                }

                var enclosing = name.Ancestors().OfType<TypeDeclarationSyntax>().Select(type => type.Identifier.ValueText);
                if (!enclosing.Any(owners.Contains) || IsDispatcherUse(reference))
                    continue;

                var line = name.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                var statement = name.Ancestors().OfType<StatementSyntax>().FirstOrDefault() ?? (SyntaxNode)reference;
                violations.Add($"{System.IO.Path.GetFileName(path)}:{line}: {Text(statement)}");
            }
        }
        return violations;
    }

    private static bool IsDispatcherUse(ExpressionSyntax reference)
    {
        switch (reference.Parent)
        {
            case AssignmentExpressionSyntax assignment when assignment.Left == reference:
                return true;

            case ArgumentSyntax argument when argument.Parent is ArgumentListSyntax { Parent: InvocationExpressionSyntax call } list:
                if (call.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" })
                    return true;
                return list.Arguments.IndexOf(argument) == 0
                       && argument.RefKindKeyword.IsKind(SyntaxKind.None)
                       && call.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Raise" } raise
                       && RootName(raise.Expression) == "CoreSignals";

            default:
                return false;
        }
    }

    /// <summary>"this", a simple or dotted type name's last part, or a placeholder that matches no type.</summary>
    private static string QualifierName(ExpressionSyntax expression) => expression switch
    {
        ThisExpressionSyntax => "this",
        IdentifierNameSyntax name => name.Identifier.ValueText,
        MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
        _ => "<expression>",
    };

    private static string RootName(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax name => name.Identifier.ValueText,
        MemberAccessExpressionSyntax access => RootName(access.Expression),
        _ => null,
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
    /// block of a try whose catch-all swallows the exception. "Directly": no lambda or local function
    /// between the try and the call, because those run later, outside the try. A catch that throws
    /// (rethrow or a new exception) guards nothing.
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
                    if (attempt.Catches.Any(IsSwallowingCatchAll))
                        return true;
                    break;                                                       // try/finally or a narrow catch: keep looking outward
                case AnonymousFunctionExpressionSyntax:
                case LocalFunctionStatementSyntax:
                case MemberDeclarationSyntax:
                    return false;
            }
        }
        return false;
    }

    private static bool IsSwallowingCatchAll(CatchClauseSyntax handler)
    {
        if (handler.Filter != null)
            return false;
        if (handler.Declaration != null)
        {
            var type = handler.Declaration.Type.ToString();
            if (type != "Exception" && type != "System.Exception")
                return false;
        }

        // A throw anywhere in the handler's own code (not in a lambda it only creates) lets the failure out.
        return !handler.Block.DescendantNodes(descendIntoChildren: child => child is not AnonymousFunctionExpressionSyntax && child is not LocalFunctionStatementSyntax)
            .Any(child => child is ThrowStatementSyntax || child is ThrowExpressionSyntax);
    }

    // ------------------------------------------------------------------ replacement profile

    /// <summary>
    /// Compat adapters that ask whether a body carries "the game's profile". During an own-Scav
    /// replacement the engine creates the body with the replacement profile and commits
    /// <c>game.Profile</c> only afterwards, so such a comparison is false for the very body being
    /// built. A read of <c>game.Profile</c> (<c>_game</c>, <c>Game</c>, <c>CurrentGame</c>) is allowed only inside
    /// <c>ReplacementProfileRule.CarriesRaidProfile(...)</c>, in <c>BodyReplacementContext</c> (the owner
    /// of the expected profile), or in the listed methods that run after the commit.
    /// </summary>
    public static readonly (string File, string Method)[] CommittedGameProfileReaders =
    {
        ("HealingAutoCancelCompat.cs", "Current"),      // Never true while IsCreating: also needs game.LocalPlayer == body.
        ("PipDisablerCompat.cs", "ObserveLocalOptic"),  // Same: Game.LocalPlayer must already be the observed body.
    };

    public static List<string> GameProfileReadsOutsideTheReplacementRule(IEnumerable<(string Path, string Source)> files)
    {
        var violations = new List<string>();
        foreach (var (path, source) in files)
        {
            var parts = path.Split('\\', '/');
            var file = parts[^1];
            if (!parts.Contains("Compat") || file == "BodyReplacementContext.cs")
                continue;

            var root = Parse(source, path).GetCompilationUnitRoot();
            foreach (var node in root.DescendantNodes())
            {
                string receiver;
                if (node is MemberAccessExpressionSyntax access && access.Name.Identifier.ValueText == "Profile")
                    receiver = LastName(access.Expression);
                else if (node is MemberBindingExpressionSyntax binding && binding.Name.Identifier.ValueText == "Profile")
                    receiver = LastName(node.Ancestors().OfType<ConditionalAccessExpressionSyntax>()
                        .First(chain => chain.WhenNotNull.Span.Contains(node.Span)).Expression);
                else
                    continue;

                if (receiver == null || !Regex.IsMatch(receiver, @"^(_?[gG]ame|CurrentGame)$"))
                    continue;
                if (node.Ancestors().OfType<InvocationExpressionSyntax>().Any(call =>
                        call.Expression is MemberAccessExpressionSyntax rule
                        && rule.Name.Identifier.ValueText == "CarriesRaidProfile"
                        && LastName(rule.Expression) == "ReplacementProfileRule"))
                    continue;
                var method = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText
                             ?? node.Ancestors().OfType<PropertyDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText;
                if (CommittedGameProfileReaders.Contains((file, method)))
                    continue;

                violations.Add($"{file}:{node.GetLocation().GetLineSpan().StartLinePosition.Line + 1} ({method})");
            }
        }
        return violations;
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
