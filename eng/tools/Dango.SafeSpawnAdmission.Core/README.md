# Safe spawn semantic fixture

This .NET 10 class library is isolated contract evidence. It has no native, UI,
network, database, account authentication or durable storage dependencies.

`RecoveryRecordCodec` encodes a bounded versioned metadata record, with a SHA256
integrity hash over canonical JSON. Hashes are integrity checks and never grant
authorization. Records contain configuration, room/lifecycle identity and
ordering only; physical state and input history are excluded.

`ConditionalRecoveryStoreFixture.TryClaim` captures the last successful record
without deleting it, increments the bounded runtime revision and allocates an
internal pending epoch. `TryWrite` checks owner/runtime/epoch/record revision and
same-epoch tick fences. It is an in-memory CAS fixture, not a durable transaction.

`RecoveryStateMachine.Prepare` consumes independent latest trusted fictitious
domain facts. `CommitPublication` must be called only after the native owner has
validated the complete healthy group; failed candidates must be disposed by that
owner before `AbortCandidate`. No epoch, frame or reason is published early.
The proof DTO cannot itself certify native health. The external owner supplies
that proof after actual raw validation, and owns same-thread native disposal.

Release ordering is `LeaseGate.PrepareRelease`, latest
`LeaseGate.RevalidateRelease`, actual native Dispose, then `CompleteRelease`.
The caller serializes these steps with the domain transition on its unique
owner. The fixture does not claim distributed atomicity. All active control and
observer leases prevent release; explicit domain sleep eligibility and the full
30 seconds are also required. Lease expiry is exclusive (`now >= expiresAt`).

Ready challenges bind current epoch, member lifecycle, lease, connection,
control generation, binding, configuration and expiration. Issuing a new valid
binding revokes challenges, accepted binding and input for that lease. Multiple
simultaneously valid control leases for one lifecycle are ambiguous facts and
block ready/input; the fixture never chooses the larger generation as authority.
Multiple observers and expired prior controls remain legal domain facts.

`ReadInput` consumes Jump once and clears intent at 350 ms without touching
physical velocity or releasing a world. Exact fixture input replay is rejected
by a strictly increasing trusted `ReceivedAt` per binding. This is a bounded
controlled-clock fixture rule, **not** the formal P2 input ordinal/idempotency
ledger; production receipt timestamps alone cannot prove command identity.

`SemanticsMutationOracle.Evaluate("none")` runs correct fixture decisions through
independent assertions. The named mutations run explicit wrong algorithms:
observerIgnored, earlyRelease, badRecordAsNew, onlyPetId, oldWriter,
unknownFaultAsLost, staleReady. A mutated result must have `OraclePassed=false`;
the console owner records it and exits 1. The oracle observes actual release,
lifecycle match, stored record and input decisions, not error text.

Verification: `dotnet test tests/Dango.Physics.Tests/Dango.Physics.Tests.csproj
-c Release --filter FullyQualifiedName~SafeSpawnRecoverySemanticsTests`.
Windows native, Linux, real multi-client, account, persistence and manual
acceptance remain separate gates owned outside this library.
