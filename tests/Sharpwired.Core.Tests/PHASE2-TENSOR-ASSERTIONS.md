# Phase 2 tensor assertions

Phase 1 has no compiler and no tensor evaluator. The Handwired tests below compare with the tensor evaluator or read the compiled program. Phase 1 ports their reference-evaluator part. Phase 2 adds the tensor assertion in the last column.

## Plan task 2

| Handwired file | Handwired test | Sharpwired test | Tensor assertion that phase 2 adds |
| --- | --- | --- | --- |
| numeric-ui-core.test.ts | tensor layers append only their new values | `CircuitTests.Tensor_layers_append_only_their_new_values_reference_part` | One layer with inputSize 2, outputSize 3, 3 stored weights. `wireOrder` is `[0, 1, 2, 3, 4]`. `evaluateTensor([5, 7])` is `[5, 7, 10, 40, 21]`. |
| numeric-ui-core.test.ts | the reference and tensor evaluators round every arithmetic step | `CircuitTests.The_reference_evaluator_rounds_every_arithmetic_step` | `evaluateTensor([16_777_216, 1])[sum]` is 16,777,216, and the result is a float array. |
| numeric-ui-core.test.ts | tensor and reference evaluators both reject a wrong-sized source table | `CircuitTests.The_reference_evaluator_rejects_a_wrong_sized_source_table` | `evaluateTensor` throws "source table has the wrong length" for the sources `[7]` and `[7, 8, 9]`. |
| numeric-ui-core.test.ts | weighted sums normalize duplicate, zero, and unordered terms | `CircuitTests.Weighted_sums_normalize_duplicate_zero_and_unordered_terms` | `evaluateTensor([11, 13, 17])[3]` is 131. |
| numeric-ui-core.test.ts | a sum shared by many consumers stays one materialized wire | `CircuitTests.A_sum_shared_by_many_consumers_stays_one_materialized_wire` | The stored weights of `compileTensorProgram(outputs)`, summed over the layers, for n = 64 are less than 3 times the weights for n = 32. The same for n = 128 and n = 64. |
| numeric-ui-core.test.ts | duplicate-wire terms merge into one exact FP32 coefficient | `CircuitTests.Duplicate_wire_terms_merge_into_one_exact_FP32_coefficient` | `evaluateTensor([x])[sum]` is `f(f(merged) × x)`. |
| numeric-ui-core.test.ts | constant terms and constant gates fold before compilation | `CircuitTests.Constant_terms_and_constant_gates_fold_before_compilation` | `evaluateTensor([13])` after the source is `[3, 59, 62, 0]`. |
| numeric-ui-core.test.ts | identity affine operations return an alias without allocating a wire | `CircuitTests.Identity_affine_operations_return_an_alias_without_allocating_a_wire` | `compileTensorProgram().width` is 2. |
| numeric-ui-core.test.ts | tensor roots remove operations that cannot affect an output | `CircuitTests.Tensor_roots_remove_operations_that_cannot_affect_an_output_reference_part` | For the root `liveOutput`: width 5, valueCount 4, `wireOrder` `[0, 1, liveDependency, liveOutput]`, and the tensor value 445 for the source `[13, 17]`. |
| numeric-ui-core.test.ts | affine outputs do not allocate fake gate rows | `CircuitTests.Affine_outputs_do_not_allocate_fake_gate_rows_reference_part` | Layer 0: outputSize 2, gateRows `[1]`, bias length 3, rowOffsets `[0, 1, 2, 3]`, columns `[0, 0, 1]`, 3 weights. The tensor values are 15 and 69 for the source `[7, 2]`. |
| numeric-ui-core.test.ts | boolean outputs make complete decisions with an explicit tie rule | `CircuitTests.Boolean_outputs_make_complete_decisions_with_an_explicit_tie_rule` | booleanRows of layer 0 is `[0]`. The tensor values for the scores 0, 0.49, 0.5, 0.51 and 1 are 0, 0, 1, 1 and 1. |
| numeric-ui-core.test.ts | a program's evaluator exposes the packed values of its last evaluation: the source cells, then every live operation in wireOrder | none | The whole test. It has no reference-evaluator part. |
| numeric-ui-core.test.ts | compiled tensor constants use native FP32 storage | none | The whole test. It has no reference-evaluator part. |
| numeric-ui-core.test.ts | compiled indices use the smallest safe unsigned width | none | The whole test. It has no reference-evaluator part. |
| numeric-ui-core.test.ts | tensor layers gather only the values they read | none | The whole test. It has no reference-evaluator part. |
| numeric-semantics.test.ts | explicit rounding survives consumers, duplicate terms, and circuit inlining | `NumericSemanticsTests.Explicit_rounding_survives_consumers_and_duplicate_terms` | `evaluateTensor(source)` gives 0 at `difference` and `f(f(s0 + s0) + s0)` at `duplicate`. Plan task 6 adds the inlined parent, and phase 2 adds `parent.evaluateTensor(source)[mapping[difference]]` = 0. |
| numeric-semantics.test.ts | explicit term order survives source permutation during inlining | `NumericSemanticsTests.Explicit_term_order_survives_and_a_reordered_sum_is_another_wire` | `parent.evaluateTensor(permuted)[mapping[sum]]` is 0, after plan task 6 adds the inlined parent. |
| numeric-semantics.test.ts | overflowing unselected candidates do not lower to gather | `NumericSemanticsTests.Overflowing_unselected_candidates_give_NaN_in_the_reference_evaluator` | `compileTensorProgram([output]).gatherSlotCount` is 0, and `evaluateTensor(source)[output]` is NaN. |
| numeric-semantics.test.ts | selection preserves finite extreme operands and its intended derivative | `NumericSemanticsTests.Selection_preserves_finite_extreme_operands` | `evaluateTensor([gate, 3e38, -3e38])[output]` is the selected source for gate 0 and gate 1. |
| numeric-semantics.test.ts | reusable tensor evaluation returns only roots and refreshes every call | none | The whole test. It has no reference-evaluator part. |
| macro-ranges.test.ts | the helpers evaluateSelect, evaluateMultiply and evaluateClampZeroOne | `MacroRangesTests` (all tests) | In each helper call, `evaluateTensor(source)[result]` is the reference value (`toBe`). |
| macro-ranges.test.ts | a proven-nonnegative operand lowers emitMultiply to one reglu | `MacroRangesTests.A_proven_nonnegative_operand_lowers_multiply_to_one_reglu` | `evaluateTensor([-3, 2])[product]` is -6, and `evaluateTensor([-3, -2])` throws "outside its assumed range". |
