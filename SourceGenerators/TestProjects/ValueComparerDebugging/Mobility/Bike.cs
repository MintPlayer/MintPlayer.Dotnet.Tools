﻿using MintPlayer.ValueComparerGenerator.Attributes;

namespace ValueComparerDebugging.Mobility;

[AutoValueComparer]
public partial class Bike : Vehicle
{
    public bool HasBell { get; set; }
    public bool HasBasket { get; set; }
    public int NumberOfGears { get; set; }
    public string FrameMaterial { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
}
