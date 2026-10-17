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
