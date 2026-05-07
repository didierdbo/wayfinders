using Wayfinders.Client.Scripts.Screens;

namespace Wayfinders.Client.Tests.Opening;

/// <summary>
/// Sanity tests for <see cref="PoiDefinitionDto"/>. The DTO is a
/// <see cref="System.ValueType"/> record struct ; these tests pin its
/// equality semantics + field roundtrip so future refactors on the
/// Godot-side <c>PoiDefinition.ToDto()</c> bridge can rely on a stable
/// shape.
/// </summary>
public sealed class PoiDefinitionDtoTests
{
    [Fact]
    public void Two_dtos_with_same_fields_are_equal()
    {
        var a = new PoiDefinitionDto("halfgate", true, "E3_CITY_HALFGATE", "E2PoiHalfgateTooltip");
        var b = new PoiDefinitionDto("halfgate", true, "E3_CITY_HALFGATE", "E2PoiHalfgateTooltip");

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Dto_is_immutable_record_struct_value_semantics()
    {
        var a = new PoiDefinitionDto("halfgate", true, "E3_CITY_HALFGATE", "E2PoiHalfgateTooltip");
        var b = a with { IsClickable = false };

        Assert.True(a.IsClickable);
        Assert.False(b.IsClickable);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Dto_supports_grisée_shape_with_empty_target_and_tooltip()
    {
        var dto = new PoiDefinitionDto("veylant", false, "", "");

        Assert.False(dto.IsClickable);
        Assert.Equal("", dto.TargetScreenId);
        Assert.Equal("", dto.TooltipKey);
    }
}
