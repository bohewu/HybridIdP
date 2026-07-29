using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;

namespace Tests.Application.UnitTests;

public sealed class OperationalAdminBootstrapAuthorizationTests
{
    private const string OptionsTypeName =
        "Core.Application.Options.OperationalAdminBootstrapOptions";
    private const string ValidatorTypeName =
        "Core.Application.Security.OperationalAdminBootstrapTokenValidator";

    public static TheoryData<string, bool> AuthorizationCases() =>
        new()
        {
            { "disabled", false },
            { "missing-digest", false },
            { "empty-digest", false },
            { "short-digest", false },
            { "non-hex-digest", false },
            { "missing-expiry", false },
            { "expiry-at-now", false },
            { "expired", false },
            { "missing-token", false },
            { "empty-token", false },
            { "malformed-token", false },
            { "incorrect-token", false },
            { "valid", true }
        };

    [Fact]
    public void Options_ShouldBeDefaultClosedAndExposeDigestButNoPlaintextSecret()
    {
        var optionsType = RequireType(OptionsTypeName);
        var options = Activator.CreateInstance(optionsType);

        Assert.NotNull(options);
        Assert.Equal("OperationalAdminBootstrap", ReadConstant(optionsType, "Section"));
        Assert.False(ReadProperty<bool>(options!, "Enabled"));
        Assert.True(string.IsNullOrEmpty(ReadProperty<string?>(options!, "TokenSha256Digest")));
        Assert.Null(ReadProperty<DateTimeOffset?>(options!, "ExpiresAtUtc"));

        var forbiddenProperties = optionsType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property =>
                property.PropertyType == typeof(string) &&
                (property.Name.Equals("Token", StringComparison.OrdinalIgnoreCase) ||
                 property.Name.Contains("Plaintext", StringComparison.OrdinalIgnoreCase) ||
                 property.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase)))
            .Select(property => property.Name)
            .ToArray();

        Assert.Empty(forbiddenProperties);
    }

    [Theory]
    [MemberData(nameof(AuthorizationCases))]
    public void IsAuthorized_ShouldFailClosedForInvalidConfigurationOrPresentation(
        string scenario,
        bool expected)
    {
        var now = DateTimeOffset.UtcNow;
        var validToken = CreateToken();
        var validDigest = Digest(validToken);
        var enabled = scenario != "disabled";
        var digest = scenario switch
        {
            "missing-digest" => null,
            "empty-digest" => string.Empty,
            "short-digest" => new string('0', 63),
            "non-hex-digest" => new string('z', 64),
            _ => validDigest
        };
        DateTimeOffset? expiresAtUtc = scenario switch
        {
            "missing-expiry" => null,
            "expiry-at-now" => now,
            "expired" => now.AddMinutes(-1),
            _ => now.AddMinutes(5)
        };
        var presentedToken = scenario switch
        {
            "missing-token" => null,
            "empty-token" => string.Empty,
            "malformed-token" => "not-a-token",
            "incorrect-token" => CreateToken(),
            _ => validToken
        };
        var optionsType = RequireType(OptionsTypeName);
        var validatorType = RequireType(ValidatorTypeName);
        var options = Activator.CreateInstance(optionsType)!;

        SetProperty(options, "Enabled", enabled);
        SetProperty(options, "TokenSha256Digest", digest);
        SetProperty(options, "ExpiresAtUtc", expiresAtUtc);

        var method = validatorType.GetMethod(
            "IsAuthorized",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            [optionsType, typeof(string), typeof(DateTimeOffset)],
            modifiers: null);

        Assert.NotNull(method);
        var actual = method!.Invoke(null, [options, presentedToken, now]);
        Assert.Equal(expected, Assert.IsType<bool>(actual));
    }

    [Fact]
    public void IsAuthorized_ShouldHashWithSha256AndCompareWithFixedTimeEquals()
    {
        var validatorType = RequireType(ValidatorTypeName);
        var authorizationMethods = validatorType
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(method => method.Name == "IsAuthorized")
            .ToArray();

        Assert.NotEmpty(authorizationMethods);
        var reachableCalls = authorizationMethods
            .SelectMany(GetReachableCalls)
            .ToArray();

        Assert.Contains(reachableCalls, method =>
            method.DeclaringType == typeof(SHA256) &&
            method.Name == nameof(SHA256.HashData));
        Assert.Contains(reachableCalls, method =>
            method.DeclaringType == typeof(CryptographicOperations) &&
            method.Name == nameof(CryptographicOperations.FixedTimeEquals));
    }

    private static Type RequireType(string fullName)
    {
        var type = typeof(Core.Application.IApplicationDbContext).Assembly.GetType(fullName);
        Assert.True(type is not null, $"Required operational bootstrap contract type is missing: {fullName}");
        return type!;
    }

    private static string? ReadConstant(Type type, string name) =>
        type.GetField(name, BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue() as string;

    private static T ReadProperty<T>(object target, string name)
    {
        var property = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        return (T)property!.GetValue(target)!;
    }

    private static void SetProperty(object target, string name, object? value)
    {
        var property = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        property!.SetValue(target, value);
    }

    private static string CreateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string Digest(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static IEnumerable<MethodBase> GetReachableCalls(MethodInfo root)
    {
        var pending = new Stack<MethodInfo>();
        var visited = new HashSet<MethodInfo>();
        pending.Push(root);

        while (pending.TryPop(out var current) && visited.Add(current))
        {
            foreach (var called in ReadCalls(current))
            {
                yield return called;
                if (called is MethodInfo child &&
                    child.DeclaringType?.Assembly == root.DeclaringType?.Assembly)
                {
                    pending.Push(child);
                }
            }
        }
    }

    private static IEnumerable<MethodBase> ReadCalls(MethodInfo method)
    {
        var body = method.GetMethodBody();
        var bytes = body?.GetILAsByteArray();
        if (bytes is null)
        {
            yield break;
        }

        var index = 0;
        while (index < bytes.Length)
        {
            OpCode opcode;
            var value = bytes[index++];
            if (value == 0xfe)
            {
                opcode = MultiByteOpCodes[bytes[index++]];
            }
            else
            {
                opcode = SingleByteOpCodes[value];
            }

            if (opcode.OperandType is OperandType.InlineMethod)
            {
                var token = BitConverter.ToInt32(bytes, index);
                MethodBase? resolved = null;
                try
                {
                    resolved = method.Module.ResolveMethod(
                        token,
                        method.DeclaringType?.GetGenericArguments(),
                        method.GetGenericArguments());
                }
                catch (ArgumentException)
                {
                    // Invalid metadata is not expected, but it must not hide the missing call assertions.
                }

                if (resolved is not null)
                {
                    yield return resolved;
                }
            }

            index += OperandSize(opcode.OperandType, bytes, index);
        }
    }

    private static int OperandSize(OperandType operandType, byte[] il, int index) =>
        operandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineI or OperandType.InlineBrTarget or OperandType.InlineField or
                OperandType.InlineMethod or OperandType.InlineSig or OperandType.InlineString or
                OperandType.InlineTok or OperandType.InlineType or OperandType.ShortInlineR => 4,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + (BitConverter.ToInt32(il, index) * 4),
            _ => throw new InvalidOperationException($"Unsupported IL operand type: {operandType}")
        };

    private static readonly OpCode[] SingleByteOpCodes = BuildOpCodeTable(multiByte: false);
    private static readonly OpCode[] MultiByteOpCodes = BuildOpCodeTable(multiByte: true);

    private static OpCode[] BuildOpCodeTable(bool multiByte)
    {
        var table = new OpCode[256];
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not OpCode opcode)
            {
                continue;
            }

            var value = unchecked((ushort)opcode.Value);
            if ((!multiByte && value <= byte.MaxValue) ||
                (multiByte && (value & 0xff00) == 0xfe00))
            {
                table[value & 0xff] = opcode;
            }
        }

        return table;
    }
}
