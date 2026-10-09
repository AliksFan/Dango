# Accessory image storage foundation

This `net10.0` library is independent of WPF. It accepts static transparent PNGs up to 32 MiB,
4096 pixels per edge and 16,000,000 pixels. It verifies framing, chunk CRCs, animation rejection,
pixel formats, palette/transparency framing and the exact decompressed scanline budget before
invoking a PNG decoder. Ancillary metadata is discarded without decompressing text or profiles.
Images are decoded to RGBA8, fully invisible RGB is cleared, and pixels are re-encoded without
source metadata. The SHA-256 of those canonical bytes is the storage identity.

`FileAccessoryBlobStore.ImportAsync` verifies any claimed source SHA-256 against the received bytes,
and returns both the computed source digest and the independently computed canonical digest.
This provides the local-ID to server-ID mapping needed by the future upload caller. Equivalent
pixels, dimensions and alpha have one canonical object within this pinned codec version.
`CurrentNormalizationVersion`, each normalized image and each import result explicitly report
normalization version `1`; callers must persist it with their upload mapping.

Files are published with a same-directory, no-overwrite rename after flushing a unique temporary
file. Concurrent equivalent imports share one object. Existing objects are verified before reuse;
corrupt objects cause an error and are never silently overwritten. Reads validate the digest and
canonical PNG. A failed live write removes only its own temporary file. Process termination can
leave an unreferenced `.tmp` file; crash cleanup and production garbage collection are future work.
OS flush plus rename does not claim a portable power-loss directory-durability guarantee.

The root is an operator-selected private directory, outside any static-file/web root. The library
rejects linked directories and linked files, but assumes no hostile process can replace its
directories concurrently. A digest is **not** an access grant: every caller must first perform
account/resource authorization. This batch supplies no HTTP endpoint, account ownership, database
references, storage quotas, public visibility, review workflow, garbage collection or cloud backup.

The server integration must limit concurrent decoding and request rates; this batch bounds each
image, not aggregate memory/CPU. No real internet-facing service or Linux runtime was tested here.
The acceptance tests use independently generated PNGs and real files under uniquely named,
checked temporary test directories, never a production storage directory.

## Dependencies checked before introduction

- `StbImageSharp` **2.30.16**: NuGet manifest declares `Unlicense OR MIT`, no transitive dependencies,
  with `net8.0` and `netstandard2.0` assets. The project describes a C# port, with no native binaries.
  The official `ImageResult.FromMemory(bytes, RedGreenBlueAlpha)` implementation was checked at
  package commit `125af70cb557033f2c46aec8e82eaaf72ac49817`; it releases the decoded unmanaged buffer
  in `finally`. [Package](https://www.nuget.org/packages/StbImageSharp/2.30.16)
  [Official API](https://github.com/StbSharp/StbImageSharp/blob/125af70cb557033f2c46aec8e82eaaf72ac49817/src/ImageResult.cs)
- `StbImageWriteSharp` **1.16.7**: NuGet manifest has `netstandard2.0` assets and no dependencies.
  Official README licenses the port as public domain; the upstream `stb_image_write.h` included in
  the project offers MIT or public domain. Official release tag `1.16.7` resolves to
  `276e0a0e389f8bcc79aa6fb4446ff825d0fdb2b4`. The `ImageWriter.WritePng` byte-array overload and
  allocation/free flow were inspected. [Package](https://www.nuget.org/packages/StbImageWriteSharp/1.16.7)
  [Official API](https://github.com/StbSharp/StbImageWriteSharp/blob/1.16.7/src/ImageWriter.cs)
  [Official license declaration](https://github.com/StbSharp/StbImageWriteSharp/blob/1.16.7/README.md#license)

NuGet's vulnerability scan reported no known advisories for these pinned packages on 2026-10-09;
that is not a proof of decoder safety. Dependency or canonical-encoding changes require an explicit
storage identity/version migration; existing digest filenames must not be reinterpreted silently.

## Test command

`dotnet test tests/Dango.AccessoryAssets.Tests/Dango.AccessoryAssets.Tests.csproj`
