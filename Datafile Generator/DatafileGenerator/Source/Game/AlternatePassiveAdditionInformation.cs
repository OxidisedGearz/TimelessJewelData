using System;
using System.Collections.Generic;
using DatafileGenerator.Data.Models;

namespace DatafileGenerator.Game;

public class AlternatePassiveAdditionInformation
{
    public AlternatePassiveAddition AlternatePassiveAddition { get; private set; }

    public IReadOnlyList<uint> StatRolls { get; private set; }

    public AlternatePassiveAdditionInformation(AlternatePassiveAddition alternatePassiveAddition, IReadOnlyList<uint> statRolls)
    {
        ArgumentNullException.ThrowIfNull(alternatePassiveAddition, nameof(alternatePassiveAddition));
        ArgumentNullException.ThrowIfNull(statRolls, nameof(statRolls));

        AlternatePassiveAddition = alternatePassiveAddition;
        StatRolls = statRolls;
    }
}
