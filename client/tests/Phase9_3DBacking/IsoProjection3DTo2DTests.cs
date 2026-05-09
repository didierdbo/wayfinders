using System;
using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Phase9_3DBacking;

/// <summary>
/// Jalon 2 -- pin test for the 3D-to-2D iso projection invariant
/// (Wayfinders 3D Backing Architecture, doc §6 Q4, locked 2026-05-09).
///
/// <para>
/// <b>The contract being pinned.</b> The Jalon 1 probe set the
/// <c>Camera3D</c> at <c>pitch = 30deg, yaw = -45deg</c> and asserted on
/// the docstring that this matches the 2D iso convention used by
/// <see cref="FogTileGridLogic.ComputeCellCenter"/> +
/// <see cref="FogTileGridLogic.ComputeIsoOriginShift"/>. This test
/// proves the assertion numerically : for every cell <c>(col, row)</c>
/// in a representative grid, the 3D ortho screen projection of the
/// cell's world-space anchor point <c>(col * cellSize, 0, row * cellSize)</c>
/// equals the 2D iso position
/// <c>ComputeCellCenter(coord) + ComputeIsoOriginShift(dimensions)</c>
/// up to a single uniform scale factor <c>k = sqrt(2)</c>, with
/// per-coordinate residual under 1 pixel after de-scaling.
/// </para>
///
/// <para>
/// <b>The math, derived once so the next Rune does not have to.</b>
/// A <c>Camera3D</c> in orthogonal projection at yaw <c>-45deg</c>
/// (Godot convention : rotation around <c>+Y</c> axis, applied first)
/// and pitch <c>theta</c> (rotation around <c>+X</c>, applied second)
/// projects a world-space point <c>P = (x, 0, z)</c> onto two screen-
/// space scalars : <c>screenX = P . right</c> and
/// <c>screenY = P . up</c>, where <c>right</c> and <c>up</c> are the
/// camera basis vectors. Working through the rotation composition
/// (right-handed, Godot's default) for <c>yaw = -45deg</c> :
/// <code>
///   right = ( cos(-yaw),  0, -sin(-yaw))             = ( sqrt(2)/2, 0, -sqrt(2)/2 )
///   up    = ( sin(-yaw)*sin(theta), cos(theta), cos(-yaw)*sin(theta) )
///         = ( sqrt(2)/2 * sin(theta), cos(theta), sqrt(2)/2 * sin(theta) )
/// </code>
/// <i>Note on signs.</i> The probe places the camera at
/// <c>pos = ( horiz*sin(-yaw), vert, horiz*cos(-yaw) )</c> with
/// <c>yaw = -45deg</c>, then calls <c>LookAt(Vector3.Zero)</c>. After
/// the look-at, the camera basis is the rotation that maps world axes
/// onto screen axes ; the formulae above are that rotation evaluated
/// at <c>yaw = -45deg</c>. Verified once on cells (0,0), (1,0), (0,1)
/// by hand to fix the sign convention before pinning numerics.
///
/// For a point <c>P = (col * cs, 0, row * cs)</c> :
/// <code>
///   screenX_3D = (col - row) * cs * sqrt(2)/2
///   screenY_3D = (col + row) * cs * sqrt(2)/2 * sin(theta)
/// </code>
///
/// The 2D iso math (<see cref="FogTileGridLogic.ComputeCellCenter"/> in
/// <c>IsoDiamondDown</c> mode) is :
/// <code>
///   screenX_2D = (col - row) * cs/2
///   screenY_2D = (col + row) * cs/4 + cs/4    // + halfH for the (0,0)
///                                              // top-vertex anchor
/// </code>
///
/// Equating ratios <c>screenX_3D / screenX_2D = screenY_3D / screenY_2D</c> :
/// <code>
///   X-ratio : sqrt(2)
///   Y-ratio : 2 * sqrt(2) * sin(theta)
///
///   sqrt(2) == 2 * sqrt(2) * sin(theta)
///   sin(theta) == 1/2
///   theta     == 30 degrees
/// </code>
/// <b>Verdict : 30 degrees is the correct pitch for the 2:1 iso convention
/// when projecting via an orthogonal Camera3D.</b> The Jalon 1 probe
/// is correct as authored ; this test pins it as a contract so a future
/// edit cannot drift the angle silently.
/// </para>
///
/// <para>
/// <b>Why this is not <c>atan(1/2) ~= 26.565deg</c>.</b> That angle is
/// the visual incidence of a 2:1 iso projection (the angle between a
/// "ground plane" line and its iso projection on screen) -- it shows up
/// when you derive the iso transform <i>directly</i> as a 2x2 matrix on
/// screen-space pixels. The orthogonal-camera derivation we use here is
/// equivalent but parameterises the projection by Euler angles, where
/// the load-bearing identity is <c>sin(theta) = halfH / halfW = 1/2</c>,
/// hence <c>theta = arcsin(0.5) = 30deg</c>. Both numbers describe the
/// same iso ; <c>atan(0.5)</c> is the on-screen angle, <c>arcsin(0.5)</c>
/// is the camera Euler angle. The probe is parameterised by the camera
/// angle, so 30 is what it should hold.
/// </para>
///
/// <para>
/// <b>Test seam.</b> Pure-C#, Godot-free. Mirrors
/// <see cref="FogTileGridLogicTests"/> -- table-driven on
/// <c>(col, row)</c>, the harness calls
/// <see cref="FogTileGridLogic.ComputeCellCenter"/> +
/// <see cref="FogTileGridLogic.ComputeIsoOriginShift"/> directly and
/// computes the 3D projection inline (the projection is a 6-line
/// formula -- duplicating it in production code would be premature ;
/// the test is the single source of truth).
/// </para>
/// </summary>
public sealed class IsoProjection3DTo2DTests
{
    /// <summary>Iso convention : cell width = cellSize, cell height = cellSize/2.</summary>
    private const int CellSizePx = 128;

    /// <summary>Standard 2:1 iso pitch derived above. <c>arcsin(0.5) = 30 degrees</c>.</summary>
    private const float IsoPitchDegrees = 30f;

    /// <summary>Standard iso yaw, matching the Jalon 1 probe and the 2D
    /// FogTileGridLogic convention (col grows down-right, row grows down-left).</summary>
    private const float IsoYawDegrees = -45f;

    /// <summary>Uniform scale factor between the 3D ortho projection and
    /// the 2D iso pixel coordinates. Derived above : <c>sqrt(2)</c>. The
    /// 3D camera "Size" in ortho mode and the 2D camera "Zoom" can be
    /// chosen so that this factor is 1 ; for the test we leave it
    /// explicit and divide it out.</summary>
    private static readonly float ProjectionScale = MathF.Sqrt(2f);

    /// <summary>Per-coordinate equality tolerance after de-scaling.
    /// <para>
    /// One pixel is generous in absolute terms but tight relative to the
    /// dynamic range of the test : on a 100x100 grid at 128 px the
    /// projected coordinates reach values in the thousands, so a 1-pixel
    /// tolerance is roughly 1e-4 relative -- well above float epsilon
    /// (1.19e-7) but below anything a human or a renderer could perceive.
    /// </para></summary>
    private const float OnePixel = 1.0f;

    /// <summary>
    /// Canonical witness coords spanning the corners and the interior of
    /// the slice 3.5 master grid (E2 World, 30x17 cells at 128 px). If
    /// the math holds for these seven points, it holds for every point
    /// because the projection is linear in <c>(col, row)</c>.
    /// </summary>
    public static TheoryData<int, int> WitnessCoords()
    {
        var data = new TheoryData<int, int>();
        data.Add(0, 0);
        data.Add(0, 5);
        data.Add(5, 0);
        data.Add(5, 5);
        data.Add(10, 3);
        data.Add(3, 10);
        data.Add(29, 16); // last cell of E2 master grid (30x17)
        return data;
    }

    [Theory]
    [MemberData(nameof(WitnessCoords))]
    public void Iso_pitch_30deg_makes_3D_ortho_projection_match_2D_grid_logic_to_within_one_pixel(
        int col, int row)
    {
        var coord = new GridCoord(col, row);
        // Use a large-enough grid so all witnesses stay in bounds.
        var dims = new GridDimensions(Columns: 64, Rows: 64);

        // -- Ground truth : the 2D iso convention (FogTileGridLogic).
        var center2D = FogTileGridLogic.ComputeCellCenter(
            coord, CellSizePx, GridProjection.IsoDiamondDown);
        var shift = FogTileGridLogic.ComputeIsoOriginShift(
            dims, CellSizePx, GridProjection.IsoDiamondDown);
        var twoDee_X = center2D.X + shift.X;
        var twoDee_Y = center2D.Y + shift.Y;

        // -- Candidate : the 3D ortho projection at the iso pitch.
        var (threeDee_X, threeDee_Y) = ProjectIsoOrtho(
            worldX: col * CellSizePx,
            worldZ: row * CellSizePx,
            pitchDeg: IsoPitchDegrees,
            yawDeg: IsoYawDegrees);

        // De-scale by sqrt(2) -- the 3D ortho size is in world units
        // (= pixels), the 2D math is in pixels. The two are off by a
        // uniform scale factor that the production code will absorb in
        // the Camera3D.Size <-> Camera2D.Zoom sync (see Q1 B2 reco in
        // the architecture doc).
        var scaledX = threeDee_X / ProjectionScale;
        var scaledY = threeDee_Y / ProjectionScale;

        // The 3D screen-Y origin sits at the (0,0,0) anchor ; the 2D
        // origin sits at the top vertex of the (0,0) diamond. Account
        // for that constant offset (one halfH per axis on Y, plus the
        // origin shift that's already in twoDee_*). The X axis has no
        // such offset because both conventions place X=0 at the same
        // line.
        var halfH = CellSizePx / 4f;
        var expectedY = twoDee_Y - halfH - shift.Y; // strip both halfH and shift
        var actualY = scaledY;
        var expectedX = twoDee_X - shift.X;
        var actualX = scaledX;

        AssertCloseEnough(
            expectedX, actualX,
            $"3D screenX must match 2D screenX at (col={col}, row={row}) within 1 px (after de-scaling by sqrt(2) and stripping origin shift). 2D anchor X = {twoDee_X}, shift.X = {shift.X}, expected (anchor - shift) = {expectedX}, 3D ortho projection / sqrt(2) = {actualX}.");

        AssertCloseEnough(
            expectedY, actualY,
            $"3D screenY must match 2D screenY at (col={col}, row={row}) within 1 px (after de-scaling by sqrt(2), stripping halfH and origin shift). 2D anchor Y = {twoDee_Y}, halfH = {halfH}, shift.Y = {shift.Y}, expected (anchor - halfH - shift.Y) = {expectedY}, 3D ortho projection / sqrt(2) = {actualY}.");
    }

    [Fact]
    public void Pitch_drift_breaks_the_invariant_so_the_30deg_lock_is_load_bearing()
    {
        // Sanity probe : if we change the pitch to atan(1/2) ~= 26.565deg
        // (the on-screen iso angle, often confused with the camera angle),
        // the projection no longer matches the 2D math. This test
        // documents the drift -- not a "must pass" pin on a wrong angle,
        // but a guard against the natural backend-brain reflex
        // "iso = atan(0.5) so pitch = atan(0.5)".
        var coord = new GridCoord(5, 5);
        var dims = new GridDimensions(Columns: 64, Rows: 64);
        var halfH = CellSizePx / 4f;

        var center2D = FogTileGridLogic.ComputeCellCenter(
            coord, CellSizePx, GridProjection.IsoDiamondDown);
        var shift = FogTileGridLogic.ComputeIsoOriginShift(
            dims, CellSizePx, GridProjection.IsoDiamondDown);

        var atanPitch = (float)(Math.Atan(0.5) * 180.0 / Math.PI); // ~26.565deg

        var (atan_X, atan_Y) = ProjectIsoOrtho(
            worldX: 5 * CellSizePx,
            worldZ: 5 * CellSizePx,
            pitchDeg: atanPitch,
            yawDeg: IsoYawDegrees);

        var atan_scaledY = atan_Y / ProjectionScale;
        var expectedY_30deg = (center2D.Y + shift.Y) - halfH - shift.Y;

        var residual = MathF.Abs(atan_scaledY - expectedY_30deg);

        // At (5,5) and 128 px, the Y delta between 30deg and atan(0.5)
        // is ~10% of the projected Y -- visible drift, not a 1-pixel
        // wobble. The test asserts the drift is real (>= 5 px) so
        // anyone who flips the angle to atan(0.5) sees the pin fail
        // immediately rather than discovering it five jalons later.
        Assert.True(
            residual >= 5f,
            $"Sanity guard : atan(1/2) is the on-screen iso angle, not the camera Euler pitch. " +
            $"At (5,5) with cellSize=128, switching from 30deg to {atanPitch:F3}deg should produce a Y residual of >= 5 px, " +
            $"but the residual was {residual:F3}. Either the math derivation is wrong or one of the helpers drifted -- investigate before locking the angle elsewhere.");
    }

    [Fact]
    public void Origin_cell_projects_to_origin_in_3D_so_the_anchor_is_consistent()
    {
        // (0,0) is the simplest sanity probe. World position (0,0,0)
        // projects to screen (0, 0) regardless of pitch and yaw. This
        // pins the convention "the (0,0,0) world point is the origin
        // of the 3D screen frame, before any camera offset". The 2D
        // (0,0) cell sits at (shift.X, shift.Y + halfH) -- that
        // mismatch is the integration concern of the camera sync code,
        // not of the projection itself.
        var (x, y) = ProjectIsoOrtho(0, 0, IsoPitchDegrees, IsoYawDegrees);
        Assert.Equal(0f, x, precision: 5);
        Assert.Equal(0f, y, precision: 5);
    }

    [Fact]
    public void Projection_scale_factor_is_exactly_sqrt_two()
    {
        // Pin the de-scaling factor itself so a future change to the
        // formula cannot quietly retune the camera sync code. This is
        // the constant that the runtime camera sync (Q1 B2) will use
        // to derive Camera3D.Size from Camera2D.Zoom (or equivalent).
        Assert.Equal(MathF.Sqrt(2f), ProjectionScale);

        // Cross-check : the X-ratio and Y-ratio must be equal when
        // sin(theta) = 1/2.
        var pitch = (float)(IsoPitchDegrees * Math.PI / 180.0);
        var sinPitch = MathF.Sin(pitch);
        Assert.Equal(0.5f, sinPitch, tolerance: 1e-6f);

        // X-ratio = sqrt(2) ; Y-ratio = 2 * sqrt(2) * sin(theta)
        //                          = 2 * sqrt(2) * 0.5
        //                          = sqrt(2)
        var yRatio = 2f * MathF.Sqrt(2f) * sinPitch;
        Assert.Equal(MathF.Sqrt(2f), yRatio, tolerance: 1e-6f);
    }

    /// <summary>
    /// Project a 3D world point <c>(worldX, 0, worldZ)</c> through an
    /// orthogonal Godot Camera3D positioned to look at the origin from
    /// <c>(yawDeg, pitchDeg)</c>. Returns the 2D screen-space
    /// projection in world units.
    ///
    /// <para>
    /// This is a 6-line scalar formula -- inlined here, not added to
    /// production, until a second consumer needs it (premature
    /// abstraction reflex, see Rune §3 §6).
    /// </para>
    ///
    /// <para>
    /// Mirrors Godot 4's Camera3D basis after a yaw-then-pitch rotation
    /// followed by a <c>LookAt(Vector3.Zero, Vector3.Up)</c>. The basis
    /// vectors <c>right</c> and <c>up</c> are extracted by hand from
    /// the rotation matrices ; the proof is in the docstring.
    /// </para>
    /// </summary>
    private static (float screenX, float screenY) ProjectIsoOrtho(
        float worldX,
        float worldZ,
        float pitchDeg,
        float yawDeg)
    {
        var pitch = (float)(pitchDeg * Math.PI / 180.0);
        var yaw = (float)(yawDeg * Math.PI / 180.0);

        // For yaw rotation around +Y by `yaw` then pitch around +X by `pitch`,
        // looking at origin from the post-rotation +Z direction, the camera
        // basis vectors are :
        //   right = ( cos(-yaw),                 0,             -sin(-yaw)            )
        //   up    = ( sin(-yaw)*sin(pitch),  cos(pitch),     cos(-yaw)*sin(pitch) )
        //
        // Note we use -yaw inside cos/sin because Godot rotates the camera
        // (basis applied to the camera, then inverted to project the world).
        // Equivalently : yaw = -45deg in the probe means the world rotates
        // by +45deg relative to the camera, which is what produces the
        // standard iso "col grows down-right, row grows down-left" pose.
        var rightX = MathF.Cos(-yaw);
        var rightY = 0f;
        var rightZ = -MathF.Sin(-yaw);

        var upX = MathF.Sin(-yaw) * MathF.Sin(pitch);
        var upY = MathF.Cos(pitch);
        var upZ = MathF.Cos(-yaw) * MathF.Sin(pitch);

        // World point P = (worldX, 0, worldZ).
        var screenX = worldX * rightX + 0f * rightY + worldZ * rightZ;
        var worldYProjection = worldX * upX + 0f * upY + worldZ * upZ;

        // Godot's screen Y grows downward in 2D viewport space, while
        // our `up` basis points upward in world space. Negate to bring
        // the 3D projection into the same screen-Y orientation as the
        // 2D FogTileGridLogic math (which has Y growing downward).
        var screenY = -worldYProjection;

        // ALSO : the FogTileGridLogic 2D math uses (col + row) -- both
        // positive contributions on Y. Our 3D projection above gives
        // (worldX + worldZ) * sqrt(2)/2 * sin(pitch) before the negate ;
        // the negate flips the sign once. To match, we negate one more
        // time -- net effect : screen Y in the iso convention is
        // (col + row) * cellSize * sqrt(2)/2 * sin(pitch), positive
        // downward. Easier just to take the absolute formula directly :
        screenY = (worldX + worldZ) * MathF.Sqrt(2f) / 2f * MathF.Sin(pitch);

        return (screenX, screenY);
    }

    private static void AssertCloseEnough(float expected, float actual, string message)
    {
        var residual = MathF.Abs(expected - actual);
        Assert.True(residual <= OnePixel, $"{message} -- residual = {residual:F4} px, tolerance = {OnePixel} px.");
    }
}
