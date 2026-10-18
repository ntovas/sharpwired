// Handwired source: src/json-search/loop-emit.ts (emitLoopWgsl). The f32 loop writer: each row of each stage is
// one f32 statement of FloatBody. The keep function k is around each literal, each product and each partial
// sum. So each value equals the CPU runner bit for bit (apart from the subnormal rule of the phase 5 plan,
// decision 4). Each register is an f32 var, and each field an array of f32 values. A field of bits is one u32
// word of the bits and one u32 word of the signs for each position. A value of a field of bits can be -0, and
// the word of the signs keeps it. The kernel around the stages, the loads, the checks and the writes are the
// parts that both loop writers share (LoopKernelText.cs, LoopIntegerWriter.cs).
// Differences from Handwired:
// - loop-emit.ts writes the rows without keep, and holds a field of bits as one word with no signs.
// - The rows of a stage are written from its outputs back, in the order that each row needs (Handwired
//   orderRowsByUse). A constant that only a constant table reads gets no statement.

namespace Sharpwired.Gpu;

/// <summary>
/// The f32 loop writer (loop-emit.ts): each operation is an f32 statement with keep. A value is the CPU value bit for
/// bit, apart from a value of magnitude below 2^-126 on a device that flushes subnormal values.
/// </summary>
public static class LoopFloatWriter
{
    /// <summary>Write the kernel of the loop program. A salt that is not 0 adds a comment, so that the shader text is new.</summary>
    public static LoopWgslModule Write(LoopProgram program, uint salt = 0) => LoopKernelWriter.Write(program, GpuWriter.Float32, salt);
}
