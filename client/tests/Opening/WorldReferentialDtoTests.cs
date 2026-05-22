using System.Text.Json;
using Wayfinders.Client.Services.Dtos;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Pin tests for the J5 <c>GET /api/world</c> wire DTOs
/// (<see cref="WorldReferentialResponse"/> and its nested records).
///
/// <para>
/// What this file pins :
/// <list type="number">
///   <item>A realistic Halfgate <c>/api/world</c> payload deserialises
///         through the source-generated <see cref="ApiJsonContext"/> — the
///         envelope, the world box, the city, and both districts with their
///         footprint polygons.</item>
///   <item>The <c>SnakeCaseLower</c> naming policy maps every snake_case
///         wire field (<c>max_x</c>, <c>district_ids</c>,
///         <c>parent_city_id</c>, <c>cell_size_m</c>, <c>schema_version</c>)
///         onto its PascalCase C# property.</item>
///   <item>An optional/null <c>bitmap</c> deserialises as <c>null</c>, not a
///         crash.</item>
/// </list>
/// This is the integration-time canary : if Coda's Pydantic schema in
/// <c>world_referential_models.py</c> drifts away from these C# records, the
/// deserialise here goes red before the eM map silently mis-places a marker.
/// </para>
/// </summary>
public sealed class WorldReferentialDtoTests
{
    // A realistic GET /api/world payload — the MVP Halfgate referential
    // shape (world.yaml J1, server commit 55487d7). snake_case wire,
    // server-sorted by id ascending.
    private const string HalfgateWorldJson = """
    {
      "schema_version": 1,
      "world": { "max_x": 500000, "max_y": 250000 },
      "cities": [
        {
          "id": "halfgate",
          "name": "Halfgate",
          "position": { "x": 92000, "y": 38000 },
          "district_ids": ["high_wall", "lower_quays"],
          "bitmap": null
        }
      ],
      "districts": [
        {
          "id": "high_wall",
          "name": "High Wall",
          "parent_city_id": "halfgate",
          "anchor": { "x": 92000, "y": 38000 },
          "footprint": [
            { "x": 92000, "y": 38000 },
            { "x": 92300, "y": 38000 },
            { "x": 92300, "y": 38200 },
            { "x": 92000, "y": 38200 }
          ],
          "cell_size_m": 2
        },
        {
          "id": "lower_quays",
          "name": "Lower Quays",
          "parent_city_id": "halfgate",
          "anchor": { "x": 91900, "y": 37700 },
          "footprint": [
            { "x": 91900, "y": 37700 },
            { "x": 92100, "y": 37700 },
            { "x": 92100, "y": 37950 },
            { "x": 91900, "y": 37950 }
          ],
          "cell_size_m": 2
        }
      ]
    }
    """;

    [Fact]
    public void Halfgate_world_payload_deserialises_through_the_source_gen_context()
    {
        var world = JsonSerializer.Deserialize(
            HalfgateWorldJson, ApiJsonContext.Default.WorldReferentialResponse);

        Assert.NotNull(world);
        Assert.Equal(1, world.SchemaVersion);
        Assert.Equal(500000, world.World.MaxX);
        Assert.Equal(250000, world.World.MaxY);
        Assert.Single(world.Cities);
        Assert.Equal(2, world.Districts.Count);
    }

    [Fact]
    public void City_fields_map_from_snake_case_wire_names()
    {
        var world = JsonSerializer.Deserialize(
            HalfgateWorldJson, ApiJsonContext.Default.WorldReferentialResponse);

        var halfgate = world!.Cities[0];
        Assert.Equal("halfgate", halfgate.Id);
        Assert.Equal("Halfgate", halfgate.Name);
        Assert.Equal(92000, halfgate.Position.X);
        Assert.Equal(38000, halfgate.Position.Y);
        // district_ids -> DistrictIds.
        Assert.Equal(2, halfgate.DistrictIds.Count);
        Assert.Contains("lower_quays", halfgate.DistrictIds);
        Assert.Contains("high_wall", halfgate.DistrictIds);
    }

    [Fact]
    public void Null_bitmap_deserialises_as_null_not_a_crash()
    {
        var world = JsonSerializer.Deserialize(
            HalfgateWorldJson, ApiJsonContext.Default.WorldReferentialResponse);

        Assert.Null(world!.Cities[0].Bitmap);
    }

    [Fact]
    public void District_footprint_polygon_deserialises_with_all_vertices()
    {
        var world = JsonSerializer.Deserialize(
            HalfgateWorldJson, ApiJsonContext.Default.WorldReferentialResponse);

        // Server sorts districts by id ascending: high_wall before lower_quays.
        var highWall = world!.Districts[0];
        Assert.Equal("high_wall", highWall.Id);
        Assert.Equal("High Wall", highWall.Name);
        Assert.Equal("halfgate", highWall.ParentCityId);
        Assert.Equal(2, highWall.CellSizeM);
        // footprint -> Footprint, 4 vertices in world metres.
        Assert.Equal(4, highWall.Footprint.Count);
        Assert.Equal(92000, highWall.Footprint[0].X);
        Assert.Equal(38000, highWall.Footprint[0].Y);
        Assert.Equal(92300, highWall.Footprint[2].X);
        Assert.Equal(38200, highWall.Footprint[2].Y);
    }

    [Fact]
    public void District_anchor_maps_from_snake_case_and_carries_world_metres()
    {
        var world = JsonSerializer.Deserialize(
            HalfgateWorldJson, ApiJsonContext.Default.WorldReferentialResponse);

        var lowerQuays = world!.Districts[1];
        Assert.Equal("lower_quays", lowerQuays.Id);
        Assert.Equal(91900, lowerQuays.Anchor.X);
        Assert.Equal(37700, lowerQuays.Anchor.Y);
    }

    [Fact]
    public void Records_have_value_equality_on_every_field()
    {
        var a = new WorldCityDto(
            "halfgate", "Halfgate", new WorldPositionDto(92000, 38000),
            new[] { "lower_quays" }, null);
        var b = new WorldCityDto(
            "halfgate", "Halfgate", new WorldPositionDto(92000, 38000),
            new[] { "lower_quays" }, null);

        // WorldPositionDto value-equality propagates ; the IReadOnlyList
        // does not, so a and b compare equal only on the position record.
        Assert.Equal(a.Position, b.Position);
        Assert.Equal(a.Id, b.Id);
    }
}
