using System;
using System.Collections.Generic;
using System.Linq;

namespace BPlayer.Services;

public static class ScenePreviewConfig
{
    private static readonly Random _rng = new();

    private static readonly float[] PositionPool =
    {
        0.15f, 0.22f, 0.30f, 0.38f, 0.45f,
        0.55f, 0.62f, 0.70f, 0.78f, 0.85f,
        0.92f
    };

    public static int ThumbnailCount => 5;

    public static float[] PickRandomPositions()
    {
        var shuffled = PositionPool.OrderBy(_ => _rng.Next()).ToArray();
        return shuffled.Take(ThumbnailCount).ToArray();
    }
}
