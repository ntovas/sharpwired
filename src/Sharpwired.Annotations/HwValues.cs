// No Handwired source. The result types of the Hw methods. They hold the outputs of the macros
// Macros.AnimatedObject and Macros.ApplySpringMatrix of Sharpwired.Core for one spring.

namespace Sharpwired.Annotations;

/// <summary>The result of Hw.AnimatedObject for one spring.</summary>
/// <param name="Position">What to draw: destination + next displacement, rounded one time.</param>
/// <param name="Displacement">The next displacement from the destination. Write it back as state.</param>
/// <param name="Velocity">The next velocity. Write it back as state.</param>
/// <param name="Active">True while the object moves: not settled and not snapped.</param>
public readonly record struct AnimatedValue(float Position, float Displacement, float Velocity, bool Active);

/// <summary>The result of Hw.ApplySpringMatrix: the displacement and the velocity after the steps of the matrix.</summary>
public readonly record struct SpringValue(float Displacement, float Velocity);
