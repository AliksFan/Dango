# Accessory image review prototype

## Accepted rule and batch boundary

Only the canonical image is reviewed. Changing wearing parameters does not require another image
review. A new canonical image needs its own review. The three built-in images need no review.
Composition complaints and the formal report workflow are later work; this batch supplies the
administrator's revocation primitive.

Private backup does not enqueue a review automatically. `RequestReviewAsync` is a separate explicit
request to display an actual stored image to other players. The initial request verifies the real
PNG blob and persists a pending record. One content hash has one shared content conclusion, while
account ownership and image-download/display authorization remain independent decisions.
The administrator preview primitive rejects a private image with no explicit review request.

Each image has a monotonic review revision. A pending image can be approved or rejected, and an
approved image can be revoked. Every decision requires the expected current revision and a reason.
Repeated requests return an existing conclusion without reopening it. Rejected/revoked content
stays private; an appeal/reopening policy is not introduced by this prototype.

The repository stores actual JSON review records in an operator-selected private directory. A
per-image exclusive file lock serializes writers, and the expected revision prevents stale review
screens from overwriting a newer decision. A flushed same-directory temporary file is atomically
renamed to the record. Readers validate the entire history and fail closed on malformed records.
Review records contain image identity, dimensions, normalization version, timestamps and decision
reasons. They contain no account identifiers, credentials, source paths or private outfit library.

The public-image projector requires an explicit display-authorization predicate for every custom
image and a current approved content conclusion. Its result excludes unreviewed/rejected/revoked
instances, including mixed outfits. It rereads decisions on each projection, so revocation removes
every instance of that image from subsequent projections. This primitive does not broadcast a
room update or invalidate already issued downloads; those belong to the future C2/C5 integration.
It filters the current outfit; it does not substitute a previous approved outfit. Image revocation
addresses the image itself, not a complaint about an otherwise approved multi-image composition.
The projector accepts and returns the immutable `Dango.Protocol` outfit manifest; it never returns
caller-owned mutable generic objects after authorizing a captured asset ID.
Public item names are replaced with the generic label `配饰`: private library labels may originate
from local file basenames, are outside image-only review and are not needed for remote rendering.
The original private manifest and every non-name wearing parameter are preserved.

`Dango.AccessoryReview` is a local administrator CLI prototype, not an authenticated administrator
endpoint. It operates on operator-selected blob/review roots, lists the real pending queue, shows
verified image paths, and can explicitly open a PNG in the OS viewer. The blob directory must stay
outside every static-file/web root. Formal authentication, account quotas, moderation roles,
ownership/download grants, room subscriptions, appeals and production garbage collection are
outside this batch. No private blob is uploaded, published or automatically handed to an admin.

The root must be controlled by the operator; linked directories/files are rejected, but malicious
filesystem replacement by another privileged process is outside the contract. Process termination
can leave a temporary file; recovery/cleanup is future work. The tests use only fresh checked
temporary directories and actual PNG files. No new dependency is introduced.

## Acceptance before implementation

- A request for a missing/corrupt blob never creates or approves a review record.
- A pending record survives a new repository instance and exposes the actual stored PNG.
- Two decisions using the same revision cannot both commit; a stale decision preserves the winner.
- Rejecting or revoking never deletes the private image.
- Approval is reused for equal canonical content; altering an outfit does not change review state.
- A revoked image disappears from all fresh public projections, including duplicate references.
- Content approval alone cannot bypass the explicit display-authorization predicate.
- Built-ins pass without records; unknown state/schema/history never appears publicly.

## Prototype CLI

Every command requires `--blob-root` for an existing private blob directory and `--review-root`
for the dedicated private review directory. Commands are `request --id HASH`, `list`, `show --id HASH
[--open]`, `approve --id HASH --revision N --reason TEXT`, `reject ...`, and `revoke ...`.

`dotnet run --project eng/tools/Dango.AccessoryReview -- COMMAND --blob-root BLOB_DIRECTORY --review-root REVIEW_DIRECTORY ...`

`dotnet test tests/Dango.AccessoryModeration.Tests/Dango.AccessoryModeration.Tests.csproj`
