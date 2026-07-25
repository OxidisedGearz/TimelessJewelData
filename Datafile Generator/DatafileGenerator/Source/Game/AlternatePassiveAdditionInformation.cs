using System;
using System.Collections.Generic;
using DatafileGenerator.Data.Models;

namespace DatafileGenerator.Game;

public class AlternatePassiveAdditionInformation
{
    public AlternatePassiveAddition AlternatePassiveAddition { get; private set; }

    public IReadOnlyDictionary<int, int> StatRolls { get; private set; }

    public AlternatePassiveAdditionInformation(AlternatePassiveAddition alternatePassiveAddition, IReadOnlyDictionary<int, int> statRolls)
    {
        ArgumentNullException.ThrowIfNull(alternatePassiveAddition, nameof(alternatePassiveAddition));

        AlternatePassiveAddition = alternatePassiveAddition;
        StatRolls = statRolls;
    }
}
