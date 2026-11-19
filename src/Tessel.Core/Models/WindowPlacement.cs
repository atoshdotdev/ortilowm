namespace Tessel.Core.Models;

public sealed record WindowPlacement(
    nint Handle,
    int X,
    int Y,
    int Width,
    int Height
);
