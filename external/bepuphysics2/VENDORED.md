# BepuPhysics 2 (vendored)

Source copy of https://github.com/bepu/bepuphysics2 at commit c230dd1
("Fix HalfWidth not being included in the angular expansion calculation of boxes", 2026-09-19),
only `BepuPhysics`, `BepuUtilities` and `CommonSettings.props`. License: Apache-2.0 (LICENSE.md).

Why not NuGet: 2.5.0-beta.29 still has the old friction bug in convex manifolds (fixed in 3bd72d2);
creature gaits are tuned on the fixed physics. To update: copy the two folders from a newer commit.
Always built in Release (see ../Directory.Build.props).
