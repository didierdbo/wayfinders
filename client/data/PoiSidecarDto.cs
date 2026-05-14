namespace Wayfinders.Client.Data;

internal sealed record PoiSidecarDto(
    int[] AnchorPixel,
    string DisplayName
);
