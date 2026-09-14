namespace EfCore.MultiTenancy.EfCore.Administration;

/// <summary>Thrown for a malformed or missing command-line argument to <see cref="TenantAdministrationCommand{TTenant}"/>.</summary>
public sealed class CommandArgumentException : Exception
{
    public CommandArgumentException(string message) : base(message)
    {
    }
}

/// <summary>
/// Minimal <c>--name value</c> / <c>--switch</c> parser for <see cref="TenantAdministrationCommand{TTenant}"/>.
/// A flag repeated more than once collects every value (for e.g. <c>--tenant</c> or
/// <c>--schema</c> given several times); a flag with no following value (end of the
/// argument list, or immediately followed by another <c>--flag</c>) is a boolean switch.
/// </summary>
public sealed class CommandLineArgs
{
    private readonly Dictionary<string, List<string>> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _switches = new(StringComparer.OrdinalIgnoreCase);

    public static CommandLineArgs Parse(IReadOnlyList<string> args)
    {
        var result = new CommandLineArgs();
        for (var i = 0; i < args.Count; i++)
        {
            var token = args[i];
            if (!token.StartsWith("--", StringComparison.Ordinal) || token.Length == 2)
            {
                throw new CommandArgumentException($"Unexpected argument '{token}'; options must be passed as --name value or --switch.");
            }

            var name = token[2..];
            var hasValue = i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
            if (hasValue)
            {
                if (!result._values.TryGetValue(name, out var list))
                {
                    list = new List<string>();
                    result._values[name] = list;
                }

                list.Add(args[++i]);
            }
            else
            {
                result._switches.Add(name);
            }
        }

        return result;
    }

    /// <summary>Every value passed for <paramref name="name"/>, in order given; empty if never passed.</summary>
    public IReadOnlyList<string> GetValues(string name) =>
        _values.TryGetValue(name, out var list) ? list : Array.Empty<string>();

    public bool HasSwitch(string name) => _switches.Contains(name);
}
