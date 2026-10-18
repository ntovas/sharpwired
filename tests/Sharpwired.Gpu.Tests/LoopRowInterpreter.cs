// Handwired source: tests/json-search/wgsl-interpreter.ts (a strict interpreter of the WGSL subset that
// loop-emit.ts and bit-emit.ts write) and tests/json-search/interpreted-gpu.ts (a run of a loop shader over
// documents on the CPU). LoopRowInterpreter parses the text of a loop kernel of LoopFloatWriter or
// LoopIntegerWriter and runs it in C# for each document. Each f32 operation rounds on its own, as WGSL
// does when no compiler fuses or regroups an operation. i32 and u32 operations wrap, as in WGSL. The
// helper functions of the prelude (k, relu, is_nan, is_finite, ordered, is_bit, ones, is_nonzero, on_grid)
// are built-in functions with the meaning of their WGSL bodies. The interpreter is strict: it refuses an
// operand of the wrong type, a mix of two bit operators without parentheses, a conversion of NaN or of a
// value out of the range of the target type, and an index outside an array or a buffer. Such a case is a
// writer fault, which a GPU can hide. It says nothing about what a GPU compiler does; GpuLoopRunnerTests
// checks that on each adapter.

using System.Globalization;
using System.Text.RegularExpressions;

namespace Sharpwired.Gpu.Tests;

/// <summary>The buffers of one run of a loop kernel over the documents 0..Documents - 1.</summary>
internal sealed record KernelInputs(
    float[] Shared,
    uint[] Packed,
    float[][] Buffers,
    int Documents,
    int WindowFirst,
    int WindowCount,
    float[]?[] Targets,
    int DocumentValues,
    int Positions,
    int PositionGroups);

/// <summary>The outputs of one run: the status of each document, the document values and the window values.</summary>
internal sealed record KernelRun(uint[] Lanes, float[] DocumentValues, float[] WindowValues);

internal sealed partial class LoopRowInterpreter
{
    private enum Type { F32, I32, U32, Bool }

    private enum Flow { Normal, Break, Return }

    private sealed record Expr(Type Type, Func<Frame, double> Eval);

    private sealed record Variable(int Slot, Type Type, bool Mutable, int Length);

    // The state of one invocation.
    private sealed class Frame(int slots, int arrays)
    {
        public double[] Slots { get; } = new double[slots];

        public double[][] Arrays { get; } = new double[arrays][];

        public KernelInputs Inputs { get; set; } = null!;

        public uint Invocation { get; set; }

        public KernelRun Run { get; set; } = null!;
    }

    private readonly Func<Frame, Flow> main;
    private readonly int slotCount;
    private readonly int arrayCount;

    private LoopRowInterpreter(Func<Frame, Flow> main, int slotCount, int arrayCount)
    {
        this.main = main;
        this.slotCount = slotCount;
        this.arrayCount = arrayCount;
    }

    /// <summary>Parse the kernel text.</summary>
    public static LoopRowInterpreter Compile(string code) => new Parser(code).ParseKernel();

    /// <summary>Run one invocation for each document.</summary>
    public KernelRun Run(KernelInputs inputs)
    {
        var run = new KernelRun(new uint[inputs.Documents], new float[inputs.DocumentValues * inputs.Documents], new float[inputs.PositionGroups * inputs.WindowCount * inputs.Positions]);
        for (var document = 0; document < inputs.Documents; document++)
        {
            var frame = new Frame(slotCount, arrayCount) { Inputs = inputs, Invocation = (uint)document, Run = run };
            main(frame);
        }
        return run;
    }

    [GeneratedRegex(@"\G(0x[0-9a-fA-F]*\.[0-9a-fA-F]+p[+-]?[0-9]+f|0x[0-9a-fA-F]+[iu]?|[0-9]+\.[0-9]+f|[0-9]+[iu]?|[A-Za-z_][A-Za-z0-9_]*|>>|<<|<=|>=|==|!=|&&|\|\||->|[-+*/%&|^!~<>=(){}\[\],;:.@])")]
    private static partial Regex TokenPattern();

    private static float ToF(double value) => (float)value;

    // WGSL: the conversion of an f32 value to i32 rounds toward zero. A NaN or a value past the i32 range
    // is a writer fault here.
    private static double ToI32(double value)
    {
        if (double.IsNaN(value) || value >= 2_147_483_648.0 || value < -2_147_483_648.0) throw new InvalidOperationException($"i32 of {value.ToString("R", CultureInfo.InvariantCulture)}");
        return Math.Truncate(value) == 0 ? 0 : Math.Truncate(value);
    }

    private static double ToU32(double value)
    {
        if (double.IsNaN(value) || value >= 4_294_967_296.0 || value <= -1) throw new InvalidOperationException($"u32 of {value.ToString("R", CultureInfo.InvariantCulture)}");
        return Math.Truncate(value) == 0 ? 0 : Math.Truncate(value);
    }

    private static int OrderKey(float value)
    {
        var bits = BitConverter.SingleToInt32Bits(value);
        return bits ^ ((bits >> 31) & 0x7fffffff);
    }

    // The parser of the kernel text, with the scopes of the names.
    private sealed partial class Parser
    {
        private readonly List<string> tokens = [];
        private readonly List<Dictionary<string, Variable>> scopes = [];
        private readonly Dictionary<string, int> bindings = new(StringComparer.Ordinal);
        private int position;
        private int slots;
        private int arrays;

        public Parser(string code)
        {
            var lines = code.Split('\n').Select(line =>
            {
                var comment = line.IndexOf("//", StringComparison.Ordinal);
                return comment >= 0 ? line[..comment] : line;
            });
            var text = string.Join(" ", lines);
            var index = 0;
            while (index < text.Length)
            {
                if (char.IsWhiteSpace(text[index]))
                {
                    index++;
                    continue;
                }
                var match = TokenPattern().Match(text, index);
                if (!match.Success || match.Index != index) throw new InvalidOperationException($"unexpected WGSL text: {text.Substring(index, Math.Min(40, text.Length - index))}");
                tokens.Add(match.Value);
                index += match.Length;
            }
        }

        public LoopRowInterpreter ParseKernel()
        {
            Func<Frame, Flow>? main = null;
            while (position < tokens.Count)
            {
                if (Peek() == "@" && tokens[position + 1] == "group")
                {
                    // @group(0) @binding(n) var<...> name: type;
                    position += 5;
                    Expect("@");
                    Expect("binding");
                    Expect("(");
                    var binding = int.Parse(Next(), CultureInfo.InvariantCulture);
                    Expect(")");
                    Expect("var");
                    SkipUntil(">");
                    bindings[Next()] = binding;
                    SkipUntil(";");
                }
                else if (Peek() == "struct")
                {
                    SkipUntil("{");
                    SkipBlock();
                }
                else if (Peek() == "@" && tokens[position + 1] == "compute")
                {
                    SkipUntil("fn");
                    Expect("main");
                    SkipUntil(")");
                    SkipUntil(")");
                    scopes.Add(new Dictionary<string, Variable>(StringComparer.Ordinal));
                    main = ParseBlock();
                }
                else if (Peek() == "fn")
                {
                    SkipUntil("{");
                    SkipBlock();
                }
                else
                {
                    throw new InvalidOperationException($"unexpected top-level token {Peek()}");
                }
            }
            if (main is null) throw new InvalidOperationException("the kernel has no main");
            return new LoopRowInterpreter(main, slots, arrays);
        }

        private string Peek() => position < tokens.Count ? tokens[position] : "";

        private string Next() => tokens[position++];

        private void Expect(string token)
        {
            if (Peek() != token) throw new InvalidOperationException($"expected {token}, got {Peek()} at token {position}: {string.Join(" ", tokens.Skip(Math.Max(0, position - 8)).Take(16))}");
            position++;
        }

        private void SkipUntil(string token)
        {
            while (Next() != token)
            {
            }
        }

        // Skip to the brace that closes the brace before the position.
        private void SkipBlock()
        {
            var depth = 1;
            while (depth > 0)
            {
                var token = Next();
                if (token == "{") depth++;
                if (token == "}") depth--;
            }
        }

        private Variable Declare(string name, Type type, bool mutable, int length = 0)
        {
            if (scopes[^1].ContainsKey(name)) throw new InvalidOperationException($"{name} is declared two times in one scope");
            var variable = new Variable(length > 0 ? arrays++ : slots++, type, mutable, length);
            scopes[^1][name] = variable;
            return variable;
        }

        private Variable? Find(string name)
        {
            for (var scope = scopes.Count - 1; scope >= 0; scope--)
            {
                if (scopes[scope].TryGetValue(name, out var variable)) return variable;
            }
            return null;
        }

        // "{" statements "}", with the open brace at the position.
        private Func<Frame, Flow> ParseBlock()
        {
            Expect("{");
            scopes.Add(new Dictionary<string, Variable>(StringComparer.Ordinal));
            var statements = new List<Func<Frame, Flow>>();
            while (Peek() != "}") statements.Add(ParseStatement());
            Expect("}");
            scopes.RemoveAt(scopes.Count - 1);
            var list = statements.ToArray();
            return frame =>
            {
                foreach (var statement in list)
                {
                    var flow = statement(frame);
                    if (flow != Flow.Normal) return flow;
                }
                return Flow.Normal;
            };
        }

        private static Type ParseScalarType(string token) => token switch
        {
            "f32" => Type.F32,
            "i32" => Type.I32,
            "u32" => Type.U32,
            "bool" => Type.Bool,
            _ => throw new InvalidOperationException($"unknown type {token}"),
        };

        private Func<Frame, Flow> ParseStatement()
        {
            var token = Peek();
            switch (token)
            {
                case "{":
                    return ParseBlock();
                case "let":
                case "var":
                    return ParseDeclaration();
                case "if":
                    {
                        Next();
                        Expect("(");
                        var condition = Typed(ParseExpression(), Type.Bool);
                        Expect(")");
                        var then = ParseBlock();
                        Func<Frame, Flow>? otherwise = null;
                        if (Peek() == "else")
                        {
                            Next();
                            otherwise = ParseBlock();
                        }
                        return frame => condition.Eval(frame) != 0 ? then(frame) : otherwise?.Invoke(frame) ?? Flow.Normal;
                    }
                case "for":
                    {
                        Next();
                        Expect("(");
                        scopes.Add(new Dictionary<string, Variable>(StringComparer.Ordinal));
                        var initial = ParseDeclaration();
                        var condition = Typed(ParseExpression(), Type.Bool);
                        Expect(";");
                        var update = ParseAssignment(endToken: ")");
                        Expect(")");
                        var body = ParseBlock();
                        scopes.RemoveAt(scopes.Count - 1);
                        return frame =>
                        {
                            initial(frame);
                            while (condition.Eval(frame) != 0)
                            {
                                var flow = body(frame);
                                if (flow == Flow.Break) break;
                                if (flow == Flow.Return) return Flow.Return;
                                update(frame);
                            }
                            return Flow.Normal;
                        };
                    }
                case "loop":
                    {
                        Next();
                        var body = ParseBlock();
                        return frame =>
                        {
                            while (true)
                            {
                                var flow = body(frame);
                                if (flow == Flow.Break) return Flow.Normal;
                                if (flow == Flow.Return) return Flow.Return;
                            }
                        };
                    }
                case "break":
                    Next();
                    Expect(";");
                    return _ => Flow.Break;
                case "return":
                    Next();
                    Expect(";");
                    return _ => Flow.Return;
                default:
                    {
                        var assignment = ParseAssignment(endToken: ";");
                        Expect(";");
                        return assignment;
                    }
            }
        }

        // let name: T = e; var name: T = e; var name: array<T, N>;
        private Func<Frame, Flow> ParseDeclaration()
        {
            var mutable = Next() == "var";
            var name = Next();
            Expect(":");
            if (Peek() == "array")
            {
                Next();
                Expect("<");
                var elementType = ParseScalarType(Next());
                Expect(",");
                var length = int.Parse(Next(), CultureInfo.InvariantCulture);
                Expect(">");
                Expect(";");
                var array = Declare(name, elementType, mutable: true, length);
                return frame =>
                {
                    frame.Arrays[array.Slot] = new double[length];
                    return Flow.Normal;
                };
            }
            var type = ParseScalarType(Next());
            Expect("=");
            var value = Typed(ParseExpression(), type);
            Expect(";");
            var variable = Declare(name, type, mutable);
            var slot = variable.Slot;
            return frame =>
            {
                frame.Slots[slot] = value.Eval(frame);
                return Flow.Normal;
            };
        }

        // name = e, name[i] = e, or buffer[i] = e.
        private Func<Frame, Flow> ParseAssignment(string endToken)
        {
            var name = Next();
            Expr? index = null;
            if (Peek() == "[")
            {
                Next();
                index = ParseExpression();
                Expect("]");
            }
            Expect("=");
            var value = ParseExpression();
            if (Peek() != endToken) throw new InvalidOperationException($"expected {endToken} after the assignment of {name}");
            if (index is null)
            {
                var variable = Find(name) ?? throw new InvalidOperationException($"unknown variable {name}");
                if (!variable.Mutable || variable.Length > 0) throw new InvalidOperationException($"{name} is not a scalar var");
                var typed = Typed(value, variable.Type);
                var slot = variable.Slot;
                return frame =>
                {
                    frame.Slots[slot] = typed.Eval(frame);
                    return Flow.Normal;
                };
            }
            var at = Typed(index, Type.U32, Type.I32);
            if (Find(name) is { Length: > 0 } array)
            {
                var typed = Typed(value, array.Type);
                return frame =>
                {
                    var target = frame.Arrays[array.Slot];
                    target[Index(at.Eval(frame), target.Length, name)] = typed.Eval(frame);
                    return Flow.Normal;
                };
            }
            if (name == "lanes")
            {
                var typed = Typed(value, Type.U32);
                return frame =>
                {
                    var lanes = frame.Run.Lanes;
                    lanes[Index(at.Eval(frame), lanes.Length, name)] = (uint)typed.Eval(frame);
                    return Flow.Normal;
                };
            }
            var floats = Typed(value, Type.F32);
            Func<Frame, float[]> buffer = name switch
            {
                "document_values" => frame => frame.Run.DocumentValues,
                "window_values" => frame => frame.Run.WindowValues,
                _ when name.StartsWith("target", StringComparison.Ordinal) => TargetOf(name),
                _ => throw new InvalidOperationException($"{name} is not a writable buffer"),
            };
            return frame =>
            {
                var target = buffer(frame);
                target[Index(at.Eval(frame), target.Length, name)] = ToF(floats.Eval(frame));
                return Flow.Normal;
            };
        }

        private static Func<Frame, float[]> TargetOf(string name)
        {
            var group = int.Parse(name.AsSpan(6), CultureInfo.InvariantCulture);
            return frame => frame.Inputs.Targets[group] ?? throw new InvalidOperationException($"the kernel writes {name}, which the run has not");
        }

        private static int Index(double value, int length, string name)
        {
            if (value < 0 || value >= length) throw new InvalidOperationException($"index {value} outside {name} of {length} elements");
            return (int)value;
        }

        private static Expr Typed(Expr expression, params Type[] types)
        {
            if (!types.Contains(expression.Type)) throw new InvalidOperationException($"expected {string.Join(" or ", types)}, got {expression.Type}");
            return expression;
        }

        private Expr ParseExpression() => ParseOr();

        private Expr ParseOr()
        {
            var left = ParseAnd();
            while (Peek() == "||")
            {
                Next();
                var (a, b) = (Typed(left, Type.Bool), Typed(ParseAnd(), Type.Bool));
                left = new Expr(Type.Bool, frame => a.Eval(frame) != 0 || b.Eval(frame) != 0 ? 1 : 0);
            }
            return left;
        }

        private Expr ParseAnd()
        {
            var left = ParseBits();
            while (Peek() == "&&")
            {
                Next();
                var (a, b) = (Typed(left, Type.Bool), Typed(ParseBits(), Type.Bool));
                left = new Expr(Type.Bool, frame => a.Eval(frame) != 0 && b.Eval(frame) != 0 ? 1 : 0);
            }
            return left;
        }

        // a & b & c, a | b | c, a ^ b ^ c: WGSL refuses a mix of two of these without parentheses.
        private Expr ParseBits()
        {
            var left = ParseRelational();
            string? op = null;
            while (Peek() is "&" or "|" or "^")
            {
                var current = Next();
                if (op is not null && op != current) throw new InvalidOperationException($"{op} and {current} without parentheses");
                op = current;
                var right = ParseRelational();
                if (left.Type != right.Type || left.Type == Type.F32) throw new InvalidOperationException($"{current} of {left.Type} and {right.Type}");
                var (a, b, type) = (left, right, left.Type);
                left = current switch
                {
                    "&" => new Expr(type, frame => type == Type.I32 ? (int)a.Eval(frame) & (int)b.Eval(frame) : (uint)a.Eval(frame) & (uint)b.Eval(frame)),
                    "|" => new Expr(type, frame => type == Type.I32 ? (int)a.Eval(frame) | (int)b.Eval(frame) : (uint)a.Eval(frame) | (uint)b.Eval(frame)),
                    _ => new Expr(type, frame => type == Type.I32 ? (int)a.Eval(frame) ^ (int)b.Eval(frame) : (uint)a.Eval(frame) ^ (uint)b.Eval(frame)),
                };
            }
            return left;
        }

        private Expr ParseRelational()
        {
            var left = ParseShift();
            if (Peek() is not ("==" or "!=" or "<" or "<=" or ">" or ">=")) return left;
            var op = Next();
            var right = ParseShift();
            if (left.Type != right.Type) throw new InvalidOperationException($"{op} of {left.Type} and {right.Type}");
            var (a, b, type) = (left, right, left.Type);
            Func<double, double, bool> compare = op switch
            {
                "==" => (x, y) => x == y,
                "!=" => (x, y) => x != y,
                "<" => (x, y) => x < y,
                "<=" => (x, y) => x <= y,
                ">" => (x, y) => x > y,
                _ => (x, y) => x >= y,
            };
            // f32 values compare as FP32 values: NaN compares false, and -0 equals +0.
            return new Expr(Type.Bool, frame => compare(a.Eval(frame), b.Eval(frame)) ? 1 : 0);
        }

        private Expr ParseShift()
        {
            var left = ParseAdditive();
            if (Peek() is not ("<<" or ">>")) return left;
            var op = Next();
            var right = Typed(ParseAdditive(), Type.U32);
            var (a, type) = (left, left.Type);
            if (type is not (Type.U32 or Type.I32)) throw new InvalidOperationException($"{op} of {type}");
            return op == "<<"
                ? new Expr(type, frame => type == Type.U32 ? (uint)a.Eval(frame) << (int)((uint)right.Eval(frame) & 31) : unchecked((int)a.Eval(frame) << (int)((uint)right.Eval(frame) & 31)))
                : new Expr(type, frame => type == Type.U32 ? (uint)a.Eval(frame) >> (int)((uint)right.Eval(frame) & 31) : (int)a.Eval(frame) >> (int)((uint)right.Eval(frame) & 31));
        }

        private Expr ParseAdditive()
        {
            var left = ParseMultiplicative();
            while (Peek() is "+" or "-")
            {
                var op = Next();
                left = Arithmetic(op, left, ParseMultiplicative());
            }
            return left;
        }

        private Expr ParseMultiplicative()
        {
            var left = ParseUnary();
            while (Peek() == "*")
            {
                Next();
                left = Arithmetic("*", left, ParseUnary());
            }
            return left;
        }

        private static Expr Arithmetic(string op, Expr left, Expr right)
        {
            if (left.Type != right.Type || left.Type == Type.Bool) throw new InvalidOperationException($"{op} of {left.Type} and {right.Type}");
            var (a, b) = (left, right);
            return left.Type switch
            {
                Type.F32 => op switch
                {
                    "+" => new Expr(Type.F32, frame => ToF(a.Eval(frame)) + ToF(b.Eval(frame))),
                    "-" => new Expr(Type.F32, frame => ToF(a.Eval(frame)) - ToF(b.Eval(frame))),
                    _ => new Expr(Type.F32, frame => ToF(a.Eval(frame)) * ToF(b.Eval(frame))),
                },
                Type.I32 => op switch
                {
                    "+" => new Expr(Type.I32, frame => unchecked((int)a.Eval(frame) + (int)b.Eval(frame))),
                    "-" => new Expr(Type.I32, frame => unchecked((int)a.Eval(frame) - (int)b.Eval(frame))),
                    _ => new Expr(Type.I32, frame => unchecked((int)a.Eval(frame) * (int)b.Eval(frame))),
                },
                _ => op switch
                {
                    "+" => new Expr(Type.U32, frame => unchecked((uint)a.Eval(frame) + (uint)b.Eval(frame))),
                    "-" => new Expr(Type.U32, frame => unchecked((uint)a.Eval(frame) - (uint)b.Eval(frame))),
                    _ => new Expr(Type.U32, frame => unchecked((uint)a.Eval(frame) * (uint)b.Eval(frame))),
                },
            };
        }

        private Expr ParseUnary()
        {
            switch (Peek())
            {
                case "-":
                    {
                        Next();
                        var operand = ParseUnary();
                        return operand.Type switch
                        {
                            Type.F32 => new Expr(Type.F32, frame => -ToF(operand.Eval(frame))),
                            Type.I32 => new Expr(Type.I32, frame => unchecked(-(int)operand.Eval(frame))),
                            _ => throw new InvalidOperationException($"- of {operand.Type}"),
                        };
                    }
                case "!":
                    {
                        Next();
                        var operand = Typed(ParseUnary(), Type.Bool);
                        return new Expr(Type.Bool, frame => operand.Eval(frame) != 0 ? 0 : 1);
                    }
                default:
                    return ParsePostfix();
            }
        }

        private Expr ParsePostfix()
        {
            var token = Next();
            if (token == "(")
            {
                var inner = ParseExpression();
                Expect(")");
                return inner;
            }
            if (char.IsDigit(token[0])) return Literal(token);
            if (Peek() == "(" || (token == "bitcast" && Peek() == "<")) return Call(token);
            if (Peek() == ".")
            {
                Next();
                return Member(token, Next());
            }
            if (Peek() == "[")
            {
                Next();
                var index = Typed(ParseExpression(), Type.U32, Type.I32);
                Expect("]");
                return Read(token, index);
            }
            var variable = Find(token) ?? throw new InvalidOperationException($"unknown name {token}");
            if (variable.Length > 0) throw new InvalidOperationException($"{token} is an array");
            var slot = variable.Slot;
            return new Expr(variable.Type, frame => frame.Slots[slot]);
        }

        private Expr Read(string name, Expr index)
        {
            if (Find(name) is { Length: > 0 } array)
            {
                return new Expr(array.Type, frame =>
                {
                    var values = frame.Arrays[array.Slot];
                    return values[Index(index.Eval(frame), values.Length, name)];
                });
            }
            if (!bindings.ContainsKey(name)) throw new InvalidOperationException($"unknown buffer {name}");
            if (name == "packed_words")
            {
                return new Expr(Type.U32, frame =>
                {
                    var words = frame.Inputs.Packed;
                    return words[Index(index.Eval(frame), words.Length, name)];
                });
            }
            Func<Frame, float[]> buffer = name switch
            {
                "shared_values" => frame => frame.Inputs.Shared,
                _ when name.StartsWith("buffer", StringComparison.Ordinal) => BufferOf(name),
                _ => throw new InvalidOperationException($"the kernel reads {name}, which is not a read-only buffer"),
            };
            return new Expr(Type.F32, frame =>
            {
                var values = buffer(frame);
                return values[Index(index.Eval(frame), values.Length, name)];
            });
        }

        private static Func<Frame, float[]> BufferOf(string name)
        {
            var buffer = int.Parse(name.AsSpan(6), CultureInfo.InvariantCulture);
            return frame => frame.Inputs.Buffers[buffer];
        }

        private static Expr Member(string name, string member) => (name, member) switch
        {
            ("id", "x") => new Expr(Type.U32, frame => frame.Invocation),
            ("params", "first") => new Expr(Type.U32, _ => 0),
            ("params", "count") => new Expr(Type.U32, frame => frame.Inputs.Documents),
            ("params", "zero") => new Expr(Type.U32, _ => 0),
            ("run", "document_count") => new Expr(Type.U32, frame => frame.Inputs.Documents),
            ("run", "window_first") => new Expr(Type.U32, frame => frame.Inputs.WindowFirst),
            ("run", "window_count") => new Expr(Type.U32, frame => frame.Inputs.WindowCount),
            ("run", "targets") => new Expr(Type.U32, frame =>
            {
                var mask = 0u;
                for (var group = 0; group < frame.Inputs.Targets.Length; group++)
                {
                    if (frame.Inputs.Targets[group] is not null) mask |= 1u << group;
                }
                return mask;
            }),
            _ => throw new InvalidOperationException($"unknown member {name}.{member}"),
        };

        private static Expr Literal(string token)
        {
            if (token.EndsWith('f'))
            {
                var value = token.StartsWith("0x", StringComparison.Ordinal) ? HexFloat(token) : double.Parse(token[..^1], CultureInfo.InvariantCulture);
                if ((float)value != value) throw new InvalidOperationException($"the literal {token} is not an FP32 value");
                return new Expr(Type.F32, _ => value);
            }
            var unsigned = token.EndsWith('u');
            if (!unsigned && !token.EndsWith('i')) throw new InvalidOperationException($"the literal {token} has no type suffix");
            var digits = token[..^1];
            var number = digits.StartsWith("0x", StringComparison.Ordinal)
                ? long.Parse(digits[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                : long.Parse(digits, CultureInfo.InvariantCulture);
            if (unsigned ? number > uint.MaxValue : number > int.MaxValue) throw new InvalidOperationException($"the literal {token} is out of range");
            return new Expr(unsigned ? Type.U32 : Type.I32, _ => number);
        }

        private static double HexFloat(string token)
        {
            var dot = token.IndexOf('.', StringComparison.Ordinal);
            var p = token.IndexOf('p', StringComparison.Ordinal);
            var whole = token[2..dot];
            var fraction = token[(dot + 1)..p];
            var exponent = int.Parse(token[(p + 1)..^1], CultureInfo.InvariantCulture);
            var mantissa = (whole.Length == 0 ? 0 : long.Parse(whole, NumberStyles.HexNumber, CultureInfo.InvariantCulture))
                + (long.Parse(fraction, NumberStyles.HexNumber, CultureInfo.InvariantCulture) / Math.Pow(16, fraction.Length));
            return Math.ScaleB(mantissa, exponent);
        }
    }
}
