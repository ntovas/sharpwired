# Generator of tests/Sharpwired.IL.Tests/Inputs/DifferentialInputs*.cs (phase 6 plan, task 1).
import random, sys, os

SEED = 20261018
COUNT = 320
PER_FILE = 32
rng = random.Random(SEED)
LIMIT = 1_000_000

def iv_add(a, b): return (a[0] + b[0], a[1] + b[1])
def iv_sub(a, b): return (a[0] - b[1], a[1] - b[0])
def iv_mul(a, k): return (min(a[0]*k, a[1]*k), max(a[0]*k, a[1]*k))
def ok(iv): return -LIMIT <= iv[0] and iv[1] <= LIMIT

class Method:
    def __init__(self, index):
        self.index = index
        self.S = rng.choice([3, 4, 5])
        self.R = rng.choice([3, 4])
        self.NI = rng.randint(5, 9)
        self.NF = rng.randint(4, 8)
        l0 = rng.randint(-40, 10); h0 = rng.randint(l0 + 1, l0 + 50)
        l1 = rng.randint(-40, 10); h1 = rng.randint(l1 + 1, l1 + 50)
        self.grid = rng.choice(["0.25", "0.5", "1"])
        self.params = (
            f"bool b0, bool b1, bool b2, [Range({l0}, {h0})] int i0, [Range({l1}, {h1})] int i1, [Range(0, 4)] int n,\n"
            f"        [Range(0, {self.S - 1})] int s0, [Range(-8, 8), Grid({self.grid})] float f0, [Range(-8, 8)] float f1,\n"
            f"        [FixedLength({self.NI}), Range(-50, 50)] int[] ti, [FixedLength({self.R * self.S})] bool[] tb,\n"
            f"        [FixedLength({self.NF}), Range(-10, 10), Grid(0.5)] float[] tf")
        self.ints = {"i0": (l0, h0), "i1": (l1, h1), "n": (0, 4), "s0": (0, self.S - 1)}
        self.bools = ["b0", "b1", "b2"]
        self.floats = {"f0": (-8.0, 8.0), "f1": (-8.0, 8.0)}
        self.locals = []
        self.lines = []
        self.mutable_ints = []
        self.counter = 0

    def name(self, prefix):
        self.counter += 1
        return f"{prefix}{self.counter}"

    def A(self):
        n = rng.choice(list(self.ints)); return n, self.ints[n]
    def B(self): return rng.choice(self.bools)
    def F(self):
        n = rng.choice(list(self.floats)); return n, self.floats[n]
    def row(self): return f"{rng.randrange(self.R)} * {self.S} + s0"

    def int_expr(self):
        NI = self.NI
        for _ in range(20):
            t = rng.randrange(17)
            a, ia = self.A(); b, ib = self.A()
            if t == 0: e, iv = f"{a} + {b}", iv_add(ia, ib)
            elif t == 1: e, iv = f"{a} - {b}", iv_sub(ia, ib)
            elif t == 2:
                k = rng.choice([2, 3, -2]); e, iv = f"{a} * {k}", iv_mul(ia, k)
            elif t == 3: e, iv = f"({self.B()} ? 1 : 0)", (0, 1)
            elif t == 4: e, iv = f"{a} + ({self.B()} ? 1 : 0)", iv_add(ia, (0, 1))
            elif t == 5: e, iv = f"({self.B()} ? 1 : 0) + ({self.B()} ? 1 : 0)", (0, 2)
            elif t == 6:
                f = rng.choice(["Min", "Max"])
                e = f"Math.{f}({a}, {b})"
                iv = (min(ia[0], ib[0]), min(ia[1], ib[1])) if f == "Min" else (max(ia[0], ib[0]), max(ia[1], ib[1]))
            elif t == 7:
                lo = rng.randint(-10, 5); hi = rng.randint(lo, lo + 20)
                e, iv = f"Math.Clamp({a}, {lo}, {hi})", (max(lo, min(ia[0], hi)), min(hi, max(ia[1], lo)))
            elif t == 8: e, iv = f"ti[Math.Clamp({a}, 0, {NI - 1})]", (-50, 50)
            elif t == 9: e, iv = f"Hw.ReadOrZero(ti, {a})", (-50, 50)
            elif t == 10:
                k = rng.choice([1, 2, 3]); c = rng.randint(-6, 6)
                e, iv = f"Hw.ReadOrZero(ti, {a} * {k} + {c})", (-50, 50)
            elif t == 11:
                c, ic = self.A(); e, iv = f"{self.B()} ? {a} : {c}", (min(ia[0], ic[0]), max(ia[1], ic[1]))
            elif t == 12: e, iv = f"{self.B()} ? {a} : 0", (min(ia[0], 0), max(ia[1], 0))
            elif t == 13:
                k1 = rng.randint(-5, 5); k2 = rng.randint(-5, 5); e, iv = f"{self.B()} ? {k1} : {k2}", (min(k1, k2), max(k1, k2))
            elif t == 14: e, iv = f"ti[Math.Clamp({a}, 0, {NI - 1})] + ({self.B()} ? 1 : 0)", (-50, 51)
            elif t == 15: e, iv = f"ti[Math.Clamp({a}, 1, {NI - 2})]", (-50, 50)
            else: e, iv = f"ti[Math.Clamp({a} + {b}, 0, {NI - 1})]", (-50, 50)
            if ok(iv): return e, iv
        return "i0", self.ints["i0"]

    def bool_expr(self):
        R, S = self.R, self.S
        t = rng.randrange(12)
        a, _ = self.A(); b, _ = self.A()
        if t == 0: return f"{self.B()} && {self.B()}"
        if t == 1: return f"{self.B()} || !{self.B()}"
        if t == 2: return f"{self.B()} ^ {self.B()}"
        if t == 3:
            a, b = rng.sample(list(self.ints), 2); return f"{a} < {b}"
        if t == 4: return f"{a} == {rng.randint(-3, 3)}"
        if t == 5:
            f, g = rng.sample(list(self.floats), 2); return f"{f} >= {g}"
        if t == 6: return f"tb[{self.row()}]"
        if t == 7: return f"{self.B()} && tb[{self.row()}]"
        if t == 8: return f"{self.B()} && !{self.B()} && tb[{self.row()}]"
        if t == 9: return f"{self.B()} && tb[Math.Clamp({a}, 0, {R * S - 1})]"
        if t == 10: return f"!tb[{self.row()}] || {self.B()}"
        return f"StepBit({self.B()}, tb, {self.row()})"

    def float_expr(self):
        NF = self.NF
        for _ in range(20):
            t = rng.randrange(12)
            f, jf = self.F(); g, jg = self.F(); a, ia = self.A()
            if t == 0: e, iv = f"{f} + {g}", (jf[0] + jg[0], jf[1] + jg[1])
            elif t == 1: e, iv = f"{f} - {g}", (jf[0] - jg[1], jf[1] - jg[0])
            elif t == 2: e, iv = f"{f} * 0.5f", (jf[0] * 0.5, jf[1] * 0.5)
            elif t == 3: e, iv = f"{f} * 3f", (jf[0] * 3, jf[1] * 3)
            elif t == 4: e, iv = f"{f} + ({self.B()} ? 1f : 0f)", (jf[0], jf[1] + 1)
            elif t == 5: e, iv = f"{f} + ({self.B()} ? 1 : 0)", (jf[0], jf[1] + 1)
            elif t == 6: e, iv = f"{self.B()} ? {f} : {g}", (min(jf[0], jg[0]), max(jf[1], jg[1]))
            elif t == 7: e, iv = f"{self.B()} ? {f} : 0.75f", (min(jf[0], 0.75), max(jf[1], 0.75))
            elif t == 8:
                m = rng.choice(["Min", "Max"]); e, iv = f"Math.{m}({f}, {g})", (min(jf[0], jg[0]), max(jf[1], jg[1]))
            elif t == 9: e, iv = f"tf[Math.Clamp({a}, 0, {NF - 1})]", (-10, 10)
            elif t == 10: e, iv = f"Hw.ReadOrZero(tf, {a})", (-10, 10)
            else: e, iv = f"(float){a} * 0.25f", (ia[0] * 0.25, ia[1] * 0.25)
            if abs(iv[0]) <= 1e5 and abs(iv[1]) <= 1e5: return e, iv
        return "f0", self.floats["f0"]

    def new_int(self):
        e, iv = self.int_expr(); v = self.name("v"); self.lines.append(f"int {v} = {e};"); self.ints[v] = iv
        self.locals.append((v, "int")); self.mutable_ints.append(v)

    def statement(self):
        t = rng.randrange(10)
        if t <= 1:
            self.new_int()
        elif t <= 3:
            e = self.bool_expr(); v = self.name("c"); self.lines.append(f"bool {v} = {e};"); self.bools.append(v); self.locals.append((v, "bool"))
        elif t == 4:
            e, iv = self.float_expr(); v = self.name("x"); self.lines.append(f"float {v} = {e};"); self.floats[v] = iv; self.locals.append((v, "float"))
        elif self.mutable_ints:
            v = rng.choice(self.mutable_ints); iv = self.ints[v]
            if t == 5:
                inc = rng.choice(["1", f"({self.B()} ? 1 : 0)", "2"])
                self.lines.append(f"if ({self.B()}) {v} = {v} + {inc};"); self.ints[v] = iv_add(iv, (0, 2))
            elif t == 6:
                self.lines.append(f"if (Mode != 0) {v} = {v} - 1;"); self.ints[v] = iv_add(iv, (-1, -1))
            elif t == 7:
                b = self.B()
                self.lines += ["for (int k = 0; k < 3; k++)", "{", f"    if (tb[k * {self.S} + s0] && {b}) {v} = {v} + 1;", "}"]
                self.ints[v] = iv_add(iv, (0, 3))
            elif t == 8:
                body = f"if (tb[k * {self.S} + s0]) {v} = {v} + 1;" if self.R >= 4 else f"{v} = {v} + ({self.B()} ? 1 : 0);"
                self.lines += ["for (int k = 0; k < n; k++)", "{", f"    {body}", "}"]
                self.ints[v] = iv_add(iv, (0, 4))
            else:
                self.lines.append(f"{v} = Increment({self.B()}, {v});"); self.ints[v] = iv_add(iv, (0, 1))
        else:
            self.new_int()

    def build(self):
        for _ in range(rng.randint(4, 8)):
            self.statement()
        while len(self.locals) < 2:
            self.new_int()
        outs = self.locals[-6:]
        types = ", ".join(k for _, k in outs)
        names = ", ".join(n for n, _ in outs)
        body = "\n".join("        " + l for l in self.lines)
        return (f"    [Handwired]\n    public static ({types}) Differential{self.index:03d}(\n        {self.params})\n    {{\n{body}\n        return ({names});\n    }}\n")

HEADER = """// No Handwired source. Generated inputs for the seeded differential test of the phase 6 plan, task 1.
// Each method mixes the new translator forms with the forms of phase 4. The new forms are a bool as an
// int, Math.Clamp reads, Hw.ReadOrZero, gated reads, the candidate cut, and a branch on an int constant.
// The forms of phase 4 are affine forms, gates, comparisons, Min and Max, loops of form 1 and 2, and
// inlined calls. A script with the fixed seed
// 20261018 wrote the files DifferentialInputs0.cs to DifferentialInputs9.cs one time. Do not edit them by hand.

using Sharpwired.Annotations;

namespace Sharpwired.IL.Tests.Inputs;

public static partial class DifferentialInputs
{
"""

methods = [Method(i).build() for i in range(COUNT)]
out = sys.argv[1]
for f in range(COUNT // PER_FILE):
    text = HEADER
    if f == 0:
        text += """    private const int Mode = 2;

    [Handwired]
    private static int Increment(bool bit, int value) => value + (bit ? 1 : 0);

    [Handwired]
    private static bool StepBit(bool bit, bool[] table, int index) => bit && table[index];

"""
    text += "\n".join(methods[f * PER_FILE:(f + 1) * PER_FILE])
    text += "}\n"
    with open(os.path.join(out, f"DifferentialInputs{f}.cs"), "w") as h:
        h.write(text)
