// Handwired source: tests/json-search/wgsl-interpreter.ts (the functions of the WGSL subset). The calls of the
// kernel text that LoopRowInterpreter runs. The helper functions of the prelude are built-in functions with the
// meaning of their WGSL bodies. The other calls are select, clamp, max, min, floor, bitcast and the conversions.

namespace Sharpwired.Gpu.Tests;

internal sealed partial class LoopRowInterpreter
{
    private sealed partial class Parser
    {
        private Expr Call(string name)
        {
            Type? generic = null;
            if (Peek() == "<")
            {
                Next();
                generic = ParseScalarType(Next());
                Expect(">");
            }
            Expect("(");
            var arguments = new List<Expr>();
            while (Peek() != ")")
            {
                arguments.Add(ParseExpression());
                if (Peek() == ",") Next();
            }
            Expect(")");
            Expr Argument(int index, params Type[] types) => Typed(arguments[index], types);
            switch (name)
            {
                case "k":
                    return Argument(0, Type.F32);
                case "relu":
                    {
                        var g = Argument(0, Type.F32);
                        return new Expr(Type.F32, frame =>
                        {
                            var value = ToF(g.Eval(frame));
                            return float.IsNaN(value) ? value : value > 0 ? value : 0f;
                        });
                    }
                case "is_nan":
                    {
                        var v = Argument(0, Type.F32);
                        return new Expr(Type.Bool, frame => float.IsNaN(ToF(v.Eval(frame))) ? 1 : 0);
                    }
                case "is_finite":
                    {
                        var v = Argument(0, Type.F32);
                        return new Expr(Type.Bool, frame => float.IsFinite(ToF(v.Eval(frame))) ? 1 : 0);
                    }
                case "ordered":
                    {
                        var v = Argument(0, Type.F32);
                        return new Expr(Type.I32, frame => OrderKey(ToF(v.Eval(frame))));
                    }
                case "is_bit":
                    {
                        var v = Argument(0, Type.F32);
                        return new Expr(Type.Bool, frame =>
                        {
                            var bits = BitConverter.SingleToUInt32Bits(ToF(v.Eval(frame)));
                            return (bits & 0x7fffffffu) == 0 || bits == 0x3f800000u ? 1 : 0;
                        });
                    }
                case "ones":
                    {
                        var v = Argument(0, Type.F32);
                        return new Expr(Type.U32, frame => BitConverter.SingleToUInt32Bits(ToF(v.Eval(frame))) == 0x3f800000u ? 1 : 0);
                    }
                case "is_nonzero":
                    {
                        var v = Argument(0, Type.F32);
                        return new Expr(Type.Bool, frame => (BitConverter.SingleToUInt32Bits(ToF(v.Eval(frame))) & 0x7fffffffu) != 0 ? 1 : 0);
                    }
                case "on_grid":
                    {
                        var (v, e) = (Argument(0, Type.F32), Argument(1, Type.I32));
                        return new Expr(Type.Bool, frame =>
                        {
                            var value = (double)ToF(v.Eval(frame));
                            return value == 0 || double.IsInteger(value / Math.ScaleB(1, (int)e.Eval(frame))) ? 1 : 0;
                        });
                    }
                case "select":
                    {
                        var (off, on, condition) = (arguments[0], arguments[1], Argument(2, Type.Bool));
                        if (off.Type != on.Type) throw new InvalidOperationException($"select of {off.Type} and {on.Type}");
                        return new Expr(off.Type, frame => condition.Eval(frame) != 0 ? on.Eval(frame) : off.Eval(frame));
                    }
                case "clamp":
                    {
                        // WGSL clamp is min(max(e, low), high). For f32, WGSL lets max and min return the other
                        // operand of a NaN, and the interpreter does so. The f32 writer selects candidate 0 for a
                        // NaN index after this clamp.
                        var (x, low, high) = (arguments[0], arguments[1], arguments[2]);
                        if (x.Type == Type.Bool || low.Type != x.Type || high.Type != x.Type) throw new InvalidOperationException($"clamp of {x.Type}");
                        return new Expr(x.Type, frame =>
                        {
                            var value = x.Eval(frame);
                            var (lowest, highest) = (low.Eval(frame), high.Eval(frame));
                            if (double.IsNaN(value)) return lowest;
                            return Math.Min(Math.Max(value, lowest), highest);
                        });
                    }
                case "max":
                case "min":
                    {
                        var (a, b) = (arguments[0], arguments[1]);
                        if (a.Type is not (Type.I32 or Type.U32) || b.Type != a.Type) throw new InvalidOperationException($"{name} of {a.Type} and {b.Type}");
                        return name == "max" ? new Expr(a.Type, frame => Math.Max(a.Eval(frame), b.Eval(frame))) : new Expr(a.Type, frame => Math.Min(a.Eval(frame), b.Eval(frame)));
                    }
                case "floor":
                    {
                        var v = Argument(0, Type.F32);
                        return new Expr(Type.F32, frame => MathF.Floor(ToF(v.Eval(frame))));
                    }
                case "bitcast":
                    {
                        var v = arguments[0];
                        return (generic, v.Type) switch
                        {
                            (Type.F32, Type.U32) => new Expr(Type.F32, frame => BitConverter.UInt32BitsToSingle((uint)v.Eval(frame))),
                            (Type.U32, Type.F32) => new Expr(Type.U32, frame => BitConverter.SingleToUInt32Bits(ToF(v.Eval(frame)))),
                            _ => throw new InvalidOperationException($"bitcast<{generic}> of {v.Type}"),
                        };
                    }
                case "f32":
                    {
                        var v = arguments[0];
                        return v.Type switch
                        {
                            Type.F32 => v,
                            Type.I32 => new Expr(Type.F32, frame => (float)(int)v.Eval(frame)),
                            Type.U32 => new Expr(Type.F32, frame => (float)(uint)v.Eval(frame)),
                            _ => new Expr(Type.F32, v.Eval),
                        };
                    }
                case "i32":
                    {
                        var v = arguments[0];
                        return v.Type switch
                        {
                            Type.F32 => new Expr(Type.I32, frame => ToI32(ToF(v.Eval(frame)))),
                            Type.I32 => v,
                            Type.U32 => new Expr(Type.I32, frame => unchecked((int)(uint)v.Eval(frame))),
                            _ => new Expr(Type.I32, v.Eval),
                        };
                    }
                case "u32":
                    {
                        var v = arguments[0];
                        return v.Type switch
                        {
                            Type.F32 => new Expr(Type.U32, frame => ToU32(ToF(v.Eval(frame)))),
                            Type.I32 => new Expr(Type.U32, frame => unchecked((uint)(int)v.Eval(frame))),
                            Type.U32 => v,
                            _ => new Expr(Type.U32, v.Eval),
                        };
                    }
                default:
                    throw new InvalidOperationException($"unknown function {name}");
            }
        }
    }
}
