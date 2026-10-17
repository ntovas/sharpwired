// No Handwired source. Small F# functions for the tests of the IL translator. The tests find a line
// by its marker comment ("marker: <name>"), so an edit of this file does not break them. This
// assembly has an embedded portable PDB.

namespace Sharpwired.IL.FSharpInputs

open Sharpwired.Annotations

module FrontEnd =
    [<Handwired>]
    let conditional (c: bool) (a: float32) (b: float32) = if c then a else b

    [<Handwired>]
    let twoStatements (x: float32) =
        let y = x * 0.5f
        y - 26.0f // marker: fs-second-statement

    [<Handwired>]
    let andChain (x: float32) (y: float32) (z: float32) (a: float32) (b: float32) =
        if x > 0.0f && y > 0.0f && z > 0.0f then a else b

    [<Handwired>]
    let constantLoop (x: float32) =
        let mutable s = 0.0f
        for _ in 0 .. 3 do
            s <- s + x
        s

/// A class record with two floats.
type Point =
    { [<Range(-100.0, 100.0)>] X: float32
      [<Range(-100.0, 100.0)>] Y: float32 }

/// A struct record with two floats.
[<Struct>]
type Weights =
    { [<Range(-100.0, 100.0)>] A: float32
      [<Range(-100.0, 100.0)>] B: float32 }

/// The inputs of the translation tests of task 4: the F# constructs of the plan, section
/// "Decisions", question 8. Each [<Handwired>] helper has NoInlining, so that the F# optimizer keeps
/// the call in Release.
module Translation =
    open System.Runtime.CompilerServices

    [<Handwired>]
    let recordSum (p: Point) = p.X + p.Y

    [<Handwired>]
    let structRecordProduct (w: Weights) = w.A * w.B

    [<Handwired>]
    let structTuple (x: float32) (y: float32) = struct (x + y, x * 0.5f)

    [<Handwired>]
    let systemTuple (x: float32) (y: float32) = (x + y, x - y)

    [<Handwired>]
    let choose (c: bool) (x: float32) (y: float32) = if c then x else y

    [<Handwired>]
    let pick ([<Range(0.0, 4.0)>] n: int) (a: float32) (b: float32) (c: float32) =
        match n with
        | 0 -> a
        | 1 -> b
        | _ -> c

    [<Handwired>]
    let allThree ([<Range(-100.0, 100.0)>] x: float32) ([<Range(-100.0, 100.0)>] y: float32) ([<Range(-100.0, 100.0)>] z: float32) =
        x > 0.0f && y > 0.0f && z > 0.0f

    [<Handwired>]
    let andNot (a: bool) (b: bool) = a && not b

    [<Handwired>]
    let minMaxAbs ([<Range(-100.0, 100.0)>] x: float32) ([<Range(-100.0, 100.0)>] y: float32) = struct (min x y, max x y, abs x)

    // The then side comes first in the IL of F#.
    [<Handwired>]
    let bothSides (c: bool) ([<Range(-100.0, 100.0)>] x: float32) ([<Range(-100.0, 100.0)>] y: float32) =
        if c then x * y else max x y

    let inline twice (x: float32) = x * 2.0f

    [<Handwired>]
    let useInline (x: float32) = twice x + 1.0f

    [<Handwired; MethodImpl(MethodImplOptions.NoInlining)>]
    let insideBox ([<Range(-100.0, 100.0)>] x: float32) ([<Range(-100.0, 100.0)>] y: float32) =
        x >= 0.0f && x < 10.0f && y >= 0.0f && y < 10.0f

    [<Handwired>]
    let armedInside (armed: bool) ([<Range(-100.0, 100.0)>] x: float32) ([<Range(-100.0, 100.0)>] y: float32) =
        armed && insideBox x y

    // The refusals.

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let apply (f: float32 -> float32) (x: float32) = f x

    [<Handwired>]
    let closure (x: float32) (y: float32) =
        apply (fun v -> v + y) x // marker: fs-closure

    [<Handwired>]
    let sequence (x: float32) =
        Seq.sum (seq { yield x; yield x * 2.0f }) // marker: fs-seq

/// The inputs of the loop tests of task 5 (plan, section "Decisions", question 5): the F# loop
/// shapes for ... in, for ... to and while, with a constant count (form 1) and with a runtime count
/// with a maximum (form 2).
module Loops =
    [<Handwired>]
    let repeatAddForIn (x: float32) =
        let mutable s = 0.0f
        for _ in 0 .. 3 do
            s <- s + x
        s

    [<Handwired>]
    let repeatAddForTo (x: float32) =
        let mutable s = 0.0f
        for i = 0 to 3 do
            s <- s + x
        s

    [<Handwired>]
    let repeatAddWhile (x: float32) =
        let mutable s = 0.0f
        let mutable i = 0
        while i < 4 do
            s <- s + x
            i <- i + 1
        s

    [<Handwired>]
    let countedSum ([<Range(0.0, 8.0)>] n: int) (x: float32) =
        let mutable s = 0.0f
        for _ in 0 .. n - 1 do
            s <- s + x
        s

    [<Handwired>]
    let countedSumTo ([<Range(0.0, 8.0)>] n: int) (x: float32) =
        let mutable s = 0.0f
        for i = 1 to n do
            s <- s + x
        s

    [<Handwired>]
    let countedSumWhile ([<Range(0.0, 8.0)>] n: int) (x: float32) =
        let mutable s = 0.0f
        let mutable i = 0
        while i < n do
            s <- s + x
            i <- i + 1
        s

    [<Handwired; MaxIterations(12)>]
    let halve ([<Range(0.0, 4096.0)>] x: float32) =
        let mutable y = x
        while y > 1.0f do
            y <- y * 0.5f
        y

    [<Handwired>]
    let noMaximum ([<Range(0.0, 1000000.0)>] x: float32) =
        let mutable y = x
        while y > 1.0f do // marker: fs-no-maximum
            y <- y * 0.5f
        y
