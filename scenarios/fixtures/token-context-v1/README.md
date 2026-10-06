# Fixed token/context fixtures

All text is synthetic and public-safe. The 256-byte vocabulary is the sequence
0 through 255 (SHA-256 is pinned in tokenizer.json). It is a fixture encoding,
not evidence of production model tokenization. The chat-v1 template pins its
canonical metadata checksum. Both use exported version-1 contracts and the
existing sdeveng registry/rendered-input counter, without a second counter.

Producer revision: `6f4b618bce27c76c994211910fe2767190508acb` in
`simplexidev/sdeveng`. CI checks out this exact revision; local builds default to
the sibling checkout. `SdevEngProducerRoot` can select that pinned checkout.

Run `dotnet run --project src/SdevEng.Metrics -- evaluate-token-fixtures scenarios/fixtures/token-context-v1`.
The command validates content hashes, metadata/assets and measurements, compares
all three checked-in goldens, and emits the producer's measurement contract.
Inference uses a rejecting executor; no model, key, network download or weights
are needed at evaluation time.

Goldens were calculated independently by literal concatenation of four messages:
`<m>` + role + `|` + body + `</m>`, separated by newline, then `<assistant>`.
Each UTF-8 byte costs one fixture token except the three `</m>` + newline + `<m>`
boundaries: each eight-byte sequence costs one token, saving seven each.
Thus total tokens = rendered UTF-8 bytes - 21. This includes empty bodies,
multibyte Unicode (including a combining mark), and tokens crossing component
wrapping boundaries. Rendered digests are SHA-256 of the literal UTF-8 strings.
Attribution must reconcile to the final rendered total. Later workflow consumers
and production tokenizer qualification remain deferred.
