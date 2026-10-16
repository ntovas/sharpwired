# Sharpwired

Sharpwired converts C# and F# methods into numeric programs: fixed lists of FP32 (32-bit float) operations that are neural nets with weights that are written, not trained. It follows [Handwired](https://github.com/chenglou/handwired) and adds an IL (Intermediate Language) translator.

See [docs/design.md](docs/design.md) for the design.

## Tests

Run both commands:

```sh
dotnet test
dotnet test -c Release
```

The Release run is required: only the Release build optimizes Sharpwired.Core fully, so only it can show an FMA (fused multiply-add) in the reference evaluator.
